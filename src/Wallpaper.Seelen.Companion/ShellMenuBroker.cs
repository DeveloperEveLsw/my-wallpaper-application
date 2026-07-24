using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using Wallpaper.Core.FileOperations;
using Wallpaper.Infrastructure.Windows.Shell;

namespace Wallpaper.Seelen.Companion;

internal static class ShellMenuBroker
{
    private const string BrokerArgument = "--shell-menu-ticket";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool IsInvocation(string[] args) =>
        args.Length > 0
        && string.Equals(args[0], BrokerArgument, StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length != 2 || !IsValidTicket(args[1]))
        {
            return 20;
        }

        using var pipe = new NamedPipeClientStream(
            ".",
            ShellMenuPipeServer.PipeName,
            PipeDirection.InOut,
            PipeOptions.CurrentUserOnly);
        try
        {
            pipe.Connect(5000);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException)
        {
            return 21;
        }

        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        writer.WriteLine(
            JsonSerializer.Serialize(
                new ShellMenuPipeRedeemRequest(args[1]),
                JsonOptions));

        ShellMenuPipeLaunchResponse? launch;
        try
        {
            var line = reader.ReadLine();
            launch = line is null
                ? null
                : JsonSerializer.Deserialize<ShellMenuPipeLaunchResponse>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return 22;
        }

        if (launch is null
            || !launch.Accepted
            || string.IsNullOrWhiteSpace(launch.RequestId)
            || string.IsNullOrWhiteSpace(launch.RootPath)
            || string.IsNullOrWhiteSpace(launch.RelativePath)
            || !Enum.TryParse<FileCommandItemKind>(launch.Kind, out var kind))
        {
            return 23;
        }

        var completion = ShowMenu(launch, kind);
        try
        {
            writer.WriteLine(
                JsonSerializer.Serialize(
                    new ShellMenuPipeCompletionRequest(
                        launch.RequestId,
                        completion.Succeeded,
                        completion.CommandInvoked,
                        completion.Code,
                        completion.Message),
                    JsonOptions));
        }
        catch (IOException)
        {
            return 24;
        }

        return completion.Succeeded ? 0 : 25;
    }

    private static ShellMenuCompletion ShowMenu(
        ShellMenuPipeLaunchResponse launch,
        FileCommandItemKind kind)
    {
        var commandInvoked = false;
        try
        {
            using var owner = ShellMenuOwnerWindow.Create(launch.ScreenX, launch.ScreenY);
            var target = new FileCommandTarget(
                launch.RootPath!,
                launch.RelativePath!,
                kind);
            var service = new WindowsShellContextMenuService();
            using var session = service.CreateItemContextMenu(target, owner.Handle);
            commandInvoked = session.Show(launch.ScreenX, launch.ScreenY);
            return new ShellMenuCompletion(
                launch.RequestId!,
                Succeeded: true,
                commandInvoked,
                null,
                null);
        }
        catch (FileCommandException exception)
        {
            return new ShellMenuCompletion(
                launch.RequestId!,
                Succeeded: false,
                commandInvoked,
                exception.Error.ToString(),
                exception.Message);
        }
        catch (ShellContextMenuException exception)
        {
            return new ShellMenuCompletion(
                launch.RequestId!,
                Succeeded: false,
                commandInvoked,
                "shell_menu_failed",
                exception.Message);
        }
        catch (Exception exception) when (
            exception is Win32Exception or COMException or InvalidOperationException)
        {
            return new ShellMenuCompletion(
                launch.RequestId!,
                Succeeded: false,
                commandInvoked,
                "shell_menu_failed",
                "Windows 추가 옵션 메뉴를 표시하지 못했습니다.");
        }
        finally
        {
            if (!commandInvoked
                && launch.OwnerWindow > 0
                && IsWindow((nint)launch.OwnerWindow))
            {
                _ = SetForegroundWindow((nint)launch.OwnerWindow);
            }
        }
    }

    private static bool IsValidTicket(string ticket)
    {
        if (ticket.Length != 43
            || ticket.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return false;
        }

        try
        {
            _ = Convert.FromBase64String(
                ticket.Replace('-', '+').Replace('_', '/') + "=");
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    private sealed class ShellMenuOwnerWindow : IDisposable
    {
        private const int WsPopup = unchecked((int)0x80000000);
        private const int WsVisible = 0x10000000;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExLayered = 0x00080000;
        private const uint LwaAlpha = 0x00000002;

        private readonly HwndSource _source;

        private ShellMenuOwnerWindow(HwndSource source)
        {
            _source = source;
            Handle = source.Handle;
        }

        public nint Handle { get; }

        public static ShellMenuOwnerWindow Create(int screenX, int screenY)
        {
            var parameters = new HwndSourceParameters("Wallpaper.Seelen.ShellMenuOwner")
            {
                WindowStyle = WsPopup | WsVisible,
                ExtendedWindowStyle = WsExToolWindow | WsExLayered,
                PositionX = screenX,
                PositionY = screenY,
                Width = 1,
                Height = 1,
            };
            var source = new HwndSource(parameters);
            if (!SetLayeredWindowAttributes(source.Handle, 0, 0, LwaAlpha))
            {
                var error = Marshal.GetLastWin32Error();
                source.Dispose();
                throw new Win32Exception(error);
            }

            return new ShellMenuOwnerWindow(source);
        }

        public void Dispose() => _source.Dispose();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(
            nint window,
            uint colorKey,
            byte alpha,
            uint flags);
    }
}
