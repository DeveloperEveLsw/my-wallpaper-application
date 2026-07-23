namespace Wallpaper.Seelen.Companion;

internal static class CompanionApplication
{
    private const string MutexName = @"Local\Wallpaper.Seelen.Companion.v1";

    public static async Task<int> RunAsync(string[] args)
    {
        if (!CompanionOptions.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        if (!OperatingSystem.IsWindows())
        {
            return 3;
        }

        using var singleton = new Mutex(true, MutexName, out var isPrimary);
        if (!isPrimary)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            try
            {
                return await ProductBootstrapPipe.ForwardToPrimaryAsync(options!, timeout.Token)
                    ? 0
                    : 4;
            }
            catch (OperationCanceledException)
            {
                return 4;
            }
        }

        try
        {
            var desktopRoot = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory,
                Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrWhiteSpace(desktopRoot))
            {
                return 5;
            }

            var sessions = new SessionRegistry();
            sessions.RegisterBootstrap(options!.BootstrapNonce, options.Origin);
            await using var projection = new DesktopProjectionService(desktopRoot);
            await projection.InitializeAsync();
            var visuals = new WindowsVisualResponseService(projection);
            var folderPicker = new WindowsFolderPickerService();

            await using var loopback = await ProductLoopbackServer.StartAsync(
                options,
                sessions,
                projection,
                visuals,
                folderPicker,
                CancellationToken.None);
            var pipe = new ProductBootstrapPipe(sessions, loopback.Port);
            var pipeTask = pipe.RunServerAsync(loopback.Application.Lifetime.ApplicationStopping);

            await loopback.Application.WaitForShutdownAsync();
            try
            {
                await pipeTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during application shutdown.
            }

            return 0;
        }
        finally
        {
            singleton.ReleaseMutex();
        }
    }
}
