namespace Wallpaper.Seelen.M0.Companion;

internal static class CompanionApplication
{
    private const string MutexName = @"Local\Wallpaper.Seelen.M0.Companion.v1";

    public static async Task<int> RunAsync(string[] args)
    {
        if (!CompanionOptions.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("The M0 companion runs on Windows only.");
            return 3;
        }

        using var singleton = new Mutex(true, MutexName, out var isPrimary);
        if (!isPrimary)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            try
            {
                var forwarded = await BootstrapPipe.ForwardToPrimaryAsync(
                    options!,
                    timeout.Token);
                return forwarded ? 0 : 4;
            }
            catch (OperationCanceledException)
            {
                return 4;
            }
        }

        try
        {
            var sessions = new SessionRegistry();
            sessions.RegisterBootstrap(options!.BootstrapNonce, options.Origin);

            await using var loopback = await LoopbackServer.StartAsync(
                options,
                sessions,
                CancellationToken.None);

            var pipe = new BootstrapPipe(sessions, loopback.Port);
            var pipeTask = pipe.RunServerAsync(loopback.Application.Lifetime.ApplicationStopping);

            Console.WriteLine(
                $"Wallpaper Seelen M0 Companion listening on 127.0.0.1:{loopback.Port}.");
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
