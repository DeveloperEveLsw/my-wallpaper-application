using Wallpaper.Seelen.Companion;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        ShellMenuBroker.IsInvocation(args)
            ? ShellMenuBroker.Run(args)
            : CompanionApplication.RunAsync(args).GetAwaiter().GetResult();
}
