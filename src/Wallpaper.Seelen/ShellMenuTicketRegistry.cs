using System.Security.Cryptography;
using Wallpaper.Core.FileOperations;

namespace Wallpaper.Seelen;

public sealed record ShellMenuLaunch(
    string Ticket,
    string RequestId,
    FileCommandTarget Target,
    int ScreenX,
    int ScreenY,
    long OwnerWindow);

public sealed record ShellMenuCompletion(
    string RequestId,
    bool Succeeded,
    bool CommandInvoked,
    string? Code,
    string? Message);

public sealed record ShellMenuTicketPrepareResult(
    bool Accepted,
    string? Code,
    string? Message,
    string? Ticket)
{
    public static ShellMenuTicketPrepareResult Success(string ticket) =>
        new(true, null, null, ticket);

    public static ShellMenuTicketPrepareResult Failure(string code, string message) =>
        new(false, code, message, null);
}

public sealed class ShellMenuTicketRegistry
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private Entry? _entry;
    private ITimer? _expirationTimer;

    public ShellMenuTicketRegistry()
        : this(TimeProvider.System)
    {
    }

    public ShellMenuTicketRegistry(TimeProvider timeProvider) =>
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public event Action<ShellMenuCompletion>? Completed;

    public ShellMenuTicketPrepareResult Prepare(
        DesktopShellMenuRequest request,
        FileCommandTarget target)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(target);
        ShellMenuCompletion? expired = null;
        ShellMenuTicketPrepareResult result;
        lock (_gate)
        {
            expired = RemoveExpiredPendingEntry();
            if (_entry is not null)
            {
                result = !_entry.Redeemed
                    && _entry.Request == request
                    && _entry.Target == target
                        ? ShellMenuTicketPrepareResult.Success(_entry.Ticket)
                        : ShellMenuTicketPrepareResult.Failure(
                            "shell_menu_busy",
                            "다른 Windows 추가 옵션 메뉴가 이미 열려 있습니다.");
            }
            else
            {
                var ticket = CreateTicket();
                _entry = new Entry(
                    ticket,
                    request,
                    target,
                    _timeProvider.GetUtcNow().Add(TicketLifetime),
                    Redeemed: false);
                ScheduleExpiration(TicketLifetime);
                result = ShellMenuTicketPrepareResult.Success(ticket);
            }
        }

        RaiseCompletion(expired);
        return result;
    }

    public bool TryRedeem(string ticket, out ShellMenuLaunch? launch)
    {
        launch = null;
        ShellMenuCompletion? expired = null;
        lock (_gate)
        {
            expired = RemoveExpiredPendingEntry();
            if (_entry is null
                || _entry.Redeemed
                || !TicketsEqual(_entry.Ticket, ticket))
            {
                // Completion is raised after releasing the lock.
            }
            else
            {
                _entry = _entry with { Redeemed = true };
                StopExpiration();
                launch = new ShellMenuLaunch(
                    _entry.Ticket,
                    _entry.Request.RequestId,
                    _entry.Target,
                    _entry.Request.ScreenX,
                    _entry.Request.ScreenY,
                    _entry.Request.OwnerWindow);
            }
        }

        RaiseCompletion(expired);
        return launch is not null;
    }

    public bool Complete(string ticket, ShellMenuCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        var accepted = false;
        lock (_gate)
        {
            if (_entry is not null
                && _entry.Redeemed
                && string.Equals(_entry.Request.RequestId, completion.RequestId, StringComparison.Ordinal)
                && TicketsEqual(_entry.Ticket, ticket))
            {
                _entry = null;
                StopExpiration();
                accepted = true;
            }
        }

        if (accepted)
        {
            Completed?.Invoke(completion);
        }

        return accepted;
    }

    public bool CancelPending(
        string requestId,
        string code,
        string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ShellMenuCompletion? completion = null;
        lock (_gate)
        {
            if (_entry is not null
                && !_entry.Redeemed
                && string.Equals(
                    _entry.Request.RequestId,
                    requestId,
                    StringComparison.Ordinal))
            {
                completion = new ShellMenuCompletion(
                    requestId,
                    Succeeded: false,
                    CommandInvoked: false,
                    code,
                    message);
                _entry = null;
                StopExpiration();
            }
        }

        RaiseCompletion(completion);
        return completion is not null;
    }

    private ShellMenuCompletion? RemoveExpiredPendingEntry()
    {
        if (_entry is null
            || _entry.Redeemed
            || _entry.ExpiresAt > _timeProvider.GetUtcNow())
        {
            return null;
        }

        var completion = new ShellMenuCompletion(
            _entry.Request.RequestId,
            Succeeded: false,
            CommandInvoked: false,
            "shell_menu_expired",
            "Windows 추가 옵션 메뉴 요청 시간이 만료되었습니다.");
        _entry = null;
        StopExpiration();
        return completion;
    }

    private void ScheduleExpiration(TimeSpan dueTime)
    {
        StopExpiration();
        _expirationTimer = _timeProvider.CreateTimer(
            static state => ((ShellMenuTicketRegistry)state!).ExpirePending(),
            this,
            dueTime,
            Timeout.InfiniteTimeSpan);
    }

    private void ExpirePending()
    {
        ShellMenuCompletion? completion;
        lock (_gate)
        {
            completion = RemoveExpiredPendingEntry();
            if (completion is null && _entry is { Redeemed: false } entry)
            {
                var remaining = entry.ExpiresAt - _timeProvider.GetUtcNow();
                ScheduleExpiration(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            }
        }

        RaiseCompletion(completion);
    }

    private void StopExpiration()
    {
        _expirationTimer?.Dispose();
        _expirationTimer = null;
    }

    private static bool TicketsEqual(string expected, string candidate)
    {
        if (candidate is null || expected.Length != candidate.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(expected),
            System.Text.Encoding.ASCII.GetBytes(candidate));
    }

    private static string CreateTicket()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private void RaiseCompletion(ShellMenuCompletion? completion)
    {
        if (completion is not null)
        {
            Completed?.Invoke(completion);
        }
    }

    private sealed record Entry(
        string Ticket,
        DesktopShellMenuRequest Request,
        FileCommandTarget Target,
        DateTimeOffset ExpiresAt,
        bool Redeemed);
}
