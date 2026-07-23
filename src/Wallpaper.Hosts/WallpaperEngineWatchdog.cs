using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Wallpaper.Hosts;

public static class WallpaperEngineWatchdog
{
    private static readonly TimeSpan InputPollInterval = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan StateReconcileInterval = TimeSpan.FromMilliseconds(250);
    private const string WatchdogArgument = "--wallpaper-engine-watchdog";
    private const string ApplicationProcessArgument = "--application-process-id";
    private const string EngineProcessArgument = "--engine-process-id";
    private const string ParentWindowArgument = "--parent-window-handle";
    private const string WorkerWindowArgument = "--worker-window-handle";

    public static bool TryParse(
        IReadOnlyList<string> arguments,
        out WallpaperEngineWatchdogOptions? options)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (!arguments.Any(argument =>
                argument.Equals(WatchdogArgument, StringComparison.OrdinalIgnoreCase)))
        {
            options = null;
            return false;
        }

        var applicationProcessId = ReadProcessId(arguments, ApplicationProcessArgument);
        var engineProcessId = ReadProcessId(arguments, EngineProcessArgument);
        var parentWindowHandle = ReadWindowHandle(arguments, ParentWindowArgument);
        var workerWindowHandle = ReadWindowHandle(arguments, WorkerWindowArgument);
        options = new WallpaperEngineWatchdogOptions(
            applicationProcessId,
            engineProcessId,
            parentWindowHandle,
            workerWindowHandle);
        return true;
    }

    public static void StartForCurrentProcess(IReadOnlyList<string> applicationArguments)
    {
        ArgumentNullException.ThrowIfNull(applicationArguments);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Wallpaper Engine watchdog는 Windows에서만 사용할 수 있습니다.");
        }

        var engineProcessId = WindowsProcessTree.TryGetParentProcessId(Environment.ProcessId)
            ?? throw new InvalidOperationException("Wallpaper Engine 부모 프로세스를 찾지 못했습니다.");
        using var engineProcess = Process.GetProcessById(engineProcessId);
        if (!engineProcess.ProcessName.Equals("wallpaper32", StringComparison.OrdinalIgnoreCase) &&
            !engineProcess.ProcessName.Equals("wallpaper64", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Wallpaper Engine 부모 프로세스가 아닙니다: {engineProcess.ProcessName}");
        }

        var launchOptions = HostLaunchOptions.Resolve(
            applicationArguments,
            Environment.GetEnvironmentVariable(HostLaunchOptions.EnvironmentVariableName),
            engineProcess.ProcessName,
            AppContext.BaseDirectory);
        if (launchOptions.ParentWindowHandle == 0)
        {
            throw new InvalidOperationException("Wallpaper Engine parent HWND를 찾지 못했습니다.");
        }

        var workerWindowHandle = WindowsWallpaperEngineInterop.GetInteractiveWorkerWindow(
            launchOptions.ParentWindowHandle);

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("현재 Wallpaper Application 실행 경로를 찾지 못했습니다.");
        }

        const string launchScript =
            "& { $watchdogArguments = $args[1..($args.Count - 1)]; " +
            "$watchdog = Start-Process -FilePath $args[0] " +
            "-ArgumentList $watchdogArguments -WindowStyle Hidden -PassThru; " +
            "if ($null -eq $watchdog) { exit 1 } }";
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(launchScript);
        startInfo.ArgumentList.Add(executablePath);
        startInfo.ArgumentList.Add(WatchdogArgument);
        startInfo.ArgumentList.Add(ApplicationProcessArgument);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add(EngineProcessArgument);
        startInfo.ArgumentList.Add(engineProcessId.ToString());
        startInfo.ArgumentList.Add(ParentWindowArgument);
        startInfo.ArgumentList.Add(launchOptions.ParentWindowHandle.ToInt64().ToString());
        startInfo.ArgumentList.Add(WorkerWindowArgument);
        startInfo.ArgumentList.Add(workerWindowHandle.ToInt64().ToString());

        using var broker = Process.Start(startInfo)
            ?? throw new Win32Exception("Wallpaper Engine watchdog broker를 시작하지 못했습니다.");
        if (!broker.WaitForExit(5000) || broker.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "Wallpaper Engine watchdog broker가 정상적으로 완료되지 않았습니다.");
        }
    }

    public static async Task RunAsync(
        WallpaperEngineWatchdogOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Wallpaper Engine watchdog는 Windows에서만 사용할 수 있습니다.");
        }

        Process applicationProcess;
        Process engineProcess;
        try
        {
            applicationProcess = Process.GetProcessById(options.ApplicationProcessId);
            engineProcess = Process.GetProcessById(options.EngineProcessId);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (applicationProcess)
        using (engineProcess)
        {
            ValidateTargets(applicationProcess, engineProcess);
            var inputRouterState = new WallpaperEngineInputRouterState();
            var lastStateReconcile = 0L;
            var routingFaulted = false;
            var applicationExited = false;
            var engineExited = false;
            // PeriodicTimer has one serial consumer and coalesces delayed ticks, so a slow
            // native reconciliation cannot create overlapping callbacks or queued worker threads.
            using var inputTimer = new PeriodicTimer(InputPollInterval);
            try
            {
                while (!applicationExited && !engineExited)
                {
                    var forceReconcile = lastStateReconcile == 0 ||
                        Stopwatch.GetElapsedTime(lastStateReconcile) >= StateReconcileInterval;
                    if (forceReconcile)
                    {
                        lastStateReconcile = Stopwatch.GetTimestamp();
                        applicationExited = applicationProcess.HasExited;
                        engineExited = engineProcess.HasExited;
                        if (applicationExited || engineExited)
                        {
                            break;
                        }
                    }

                    try
                    {
                        WindowsWallpaperEngineInterop.UpdateInteractiveInputFromWatchdog(
                            inputRouterState,
                            options.ApplicationProcessId,
                            options.EngineProcessId,
                            options.ParentWindowHandle,
                            forceReconcile);
                        if (routingFaulted)
                        {
                            Trace.TraceInformation(
                                "Wallpaper Engine watchdog input routing recovered.");
                            routingFaulted = false;
                        }
                    }
                    catch (Exception exception) when (
                        exception is Win32Exception or InvalidOperationException)
                    {
                        if (!routingFaulted)
                        {
                            Trace.TraceWarning(
                                $"Wallpaper Engine watchdog input routing failed: {exception.Message}");
                            routingFaulted = true;
                        }
                    }

                    if (!await inputTimer.WaitForNextTickAsync(cancellationToken))
                    {
                        break;
                    }
                }

                if (engineExited && !applicationExited)
                {
                    applicationProcess.Kill(entireProcessTree: false);
                    await applicationProcess.WaitForExitAsync(cancellationToken);
                }
            }
            finally
            {
                var workerWindow = WindowsWallpaperEngineInterop.GetWatchdogWorkerWindow(
                    inputRouterState,
                    options.WorkerWindowHandle);
                WindowsWallpaperEngineInterop.RestoreInteractiveInputAfterProcessExit(
                    options.ParentWindowHandle,
                    workerWindow);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ValidateTargets(Process applicationProcess, Process engineProcess)
    {
        var currentExecutablePath = Environment.ProcessPath;
        var applicationExecutablePath = applicationProcess.MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(currentExecutablePath) ||
            string.IsNullOrWhiteSpace(applicationExecutablePath) ||
            !Path.GetFullPath(applicationExecutablePath).Equals(
                Path.GetFullPath(currentExecutablePath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "watchdog 대상 앱이 현재 Wallpaper Application 실행 파일과 일치하지 않습니다.");
        }

        if (WindowsProcessTree.TryGetParentProcessId(applicationProcess.Id) != engineProcess.Id)
        {
            throw new InvalidOperationException(
                "watchdog 대상 앱과 Wallpaper Engine의 부모 프로세스 관계가 일치하지 않습니다.");
        }

        try
        {
            if (!engineProcess.HasExited &&
                !engineProcess.ProcessName.Equals("wallpaper32", StringComparison.OrdinalIgnoreCase) &&
                !engineProcess.ProcessName.Equals("wallpaper64", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "watchdog 대상 엔진이 Wallpaper Engine 프로세스가 아닙니다.");
            }
        }
        catch (InvalidOperationException) when (engineProcess.HasExited)
        {
            // The validated parent may exit between lookup and name inspection.
            // RunAsync must still terminate the suspended application in that race.
        }
    }

    private static int ReadProcessId(IReadOnlyList<string> arguments, string name)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!arguments[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= arguments.Count ||
                !int.TryParse(arguments[index + 1], out var processId) ||
                processId <= 0)
            {
                throw new ArgumentException($"{name} 뒤에 유효한 프로세스 ID가 필요합니다.");
            }

            return processId;
        }

        throw new ArgumentException($"watchdog 인자에 {name} 값이 필요합니다.");
    }

    private static nint ReadWindowHandle(IReadOnlyList<string> arguments, string name)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!arguments[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= arguments.Count ||
                !long.TryParse(arguments[index + 1], out var windowHandle) ||
                windowHandle <= 0)
            {
                throw new ArgumentException($"{name} 뒤에 유효한 HWND가 필요합니다.");
            }

            return checked((nint)windowHandle);
        }

        throw new ArgumentException($"watchdog 인자에 {name} 값이 필요합니다.");
    }
}

public sealed record WallpaperEngineWatchdogOptions(
    int ApplicationProcessId,
    int EngineProcessId,
    nint ParentWindowHandle,
    nint WorkerWindowHandle);
