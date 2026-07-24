using Wallpaper.Core.FileOperations;

namespace Wallpaper.Seelen.Tests;

public sealed class ShellMenuTicketRegistryTests
{
    private static readonly FileCommandTarget Target =
        new(@"C:\Fixture", @"Work\report.txt", FileCommandItemKind.File);

    [Fact]
    public void Prepare_ReusesTheSamePendingRequestAndRejectsAnotherMenu()
    {
        var registry = new ShellMenuTicketRegistry();
        var request = Request("shell-menu-1");

        var first = registry.Prepare(request, Target);
        var repeated = registry.Prepare(request, Target);
        var competing = registry.Prepare(
            Request("shell-menu-2"),
            Target);

        Assert.True(first.Accepted);
        Assert.Equal(first.Ticket, repeated.Ticket);
        Assert.False(competing.Accepted);
        Assert.Equal("shell_menu_busy", competing.Code);
    }

    [Fact]
    public void Redeem_IsSingleUseAndCompletionReleasesTheNextMenu()
    {
        var registry = new ShellMenuTicketRegistry();
        var request = Request("shell-menu-1");
        var prepared = registry.Prepare(request, Target);
        ShellMenuCompletion? observed = null;
        registry.Completed += completion => observed = completion;

        Assert.True(registry.TryRedeem(prepared.Ticket!, out var launch));
        Assert.NotNull(launch);
        Assert.Equal(Target, launch.Target);
        Assert.False(registry.TryRedeem(prepared.Ticket!, out _));

        var completion = new ShellMenuCompletion(
            request.RequestId,
            Succeeded: true,
            CommandInvoked: false,
            null,
            null);
        Assert.True(registry.Complete(prepared.Ticket!, completion));
        Assert.Equal(completion, observed);
        Assert.True(registry.Prepare(Request("shell-menu-2"), Target).Accepted);
    }

    [Fact]
    public void ExpiredTicket_IsRejectedAndReportsACompletion()
    {
        var time = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero));
        var registry = new ShellMenuTicketRegistry(time);
        var request = Request("shell-menu-expired");
        var prepared = registry.Prepare(request, Target);
        ShellMenuCompletion? observed = null;
        registry.Completed += completion => observed = completion;
        time.Advance(TimeSpan.FromSeconds(11));

        Assert.NotNull(observed);
        Assert.Equal("shell_menu_expired", observed.Code);
        Assert.False(registry.TryRedeem(prepared.Ticket!, out _));
        Assert.True(registry.Prepare(Request("shell-menu-next"), Target).Accepted);
    }

    [Fact]
    public void CancelPending_ReleasesATicketThatWasNotRedeemed()
    {
        var registry = new ShellMenuTicketRegistry();
        var request = Request("shell-menu-cancelled");
        var prepared = registry.Prepare(request, Target);

        Assert.True(
            registry.CancelPending(
                request.RequestId,
                "launch_failed",
                "broker launch failed"));
        Assert.False(registry.TryRedeem(prepared.Ticket!, out _));
        Assert.True(registry.Prepare(Request("shell-menu-next"), Target).Accepted);
    }

    private static DesktopShellMenuRequest Request(string requestId) =>
        new(requestId, "file:WORK/REPORT.TXT", -1200, 240, 8192);

    private sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        private AdjustableTimer? _timer;

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            _timer = new AdjustableTimer(
                this,
                callback,
                state,
                dueTime,
                period);
            return _timer;
        }

        public void Advance(TimeSpan duration)
        {
            _now += duration;
            _timer?.FireIfDue();
        }

        private sealed class AdjustableTimer(
            AdjustableTimeProvider owner,
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period) : ITimer
        {
            private DateTimeOffset _dueAt = owner._now.Add(dueTime);
            private bool _disposed;
            private TimeSpan _period = period;

            public bool Change(TimeSpan nextDueTime, TimeSpan nextPeriod)
            {
                if (_disposed)
                {
                    return false;
                }

                _dueAt = owner._now.Add(nextDueTime);
                _period = nextPeriod;
                return true;
            }

            public void Dispose() => _disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void FireIfDue()
            {
                if (_disposed || owner._now < _dueAt)
                {
                    return;
                }

                if (_period == Timeout.InfiniteTimeSpan)
                {
                    _disposed = true;
                }
                else
                {
                    _dueAt = owner._now.Add(_period);
                }

                callback(state);
            }
        }
    }
}
