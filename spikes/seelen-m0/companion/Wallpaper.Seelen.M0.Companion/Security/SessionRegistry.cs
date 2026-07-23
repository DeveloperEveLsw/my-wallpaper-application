using System.Collections.Concurrent;

namespace Wallpaper.Seelen.M0.Companion;

internal sealed class SessionRegistry(TimeProvider timeProvider)
{
    private static readonly TimeSpan BootstrapLifetime = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, PendingBootstrap> pending =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ActiveSession> active =
        new(StringComparer.Ordinal);

    public SessionRegistry()
        : this(TimeProvider.System)
    {
    }

    public void RegisterBootstrap(BootstrapNonce nonce, string origin)
    {
        ArgumentException.ThrowIfNullOrEmpty(origin);
        if (!OriginPolicy.IsAllowed(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "Origin is not allowlisted.");
        }

        RemoveExpiredBootstraps();
        pending[nonce.Identifier] = new PendingBootstrap(
            nonce,
            origin,
            timeProvider.GetUtcNow().Add(BootstrapLifetime));
    }

    public bool HasAllowedOrigin(string? origin)
    {
        if (!OriginPolicy.IsAllowed(origin))
        {
            return false;
        }

        RemoveExpiredBootstraps();
        return pending.Values.Any(item => item.Origin == origin)
            || active.Values.Any(item => item.Origin == origin);
    }

    public bool TryAuthenticate(
        string encodedNonce,
        string origin,
        out ActiveSession? session)
    {
        session = null;
        if (!BootstrapNonce.TryParse(encodedNonce, out var suppliedNonce)
            || !pending.TryRemove(suppliedNonce!.Identifier, out var registration)
            || registration.ExpiresAt <= timeProvider.GetUtcNow()
            || registration.Origin != origin
            || !registration.Nonce.FixedTimeEquals(suppliedNonce))
        {
            return false;
        }

        session = new ActiveSession(Base64Url.CreateRandom256BitValue(), origin);
        active[session.Token] = session;
        return true;
    }

    public bool TryCreateBootstrapProof(
        string? nonceIdentifier,
        string? origin,
        string? encodedChallenge,
        out string? proof)
    {
        proof = null;
        if (nonceIdentifier is null
            || origin is null
            || !Base64Url.TryDecode256BitValue(nonceIdentifier, out _)
            || !Base64Url.TryDecode256BitValue(encodedChallenge, out var challenge)
            || !pending.TryGetValue(nonceIdentifier, out var registration)
            || registration.ExpiresAt <= timeProvider.GetUtcNow()
            || registration.Origin != origin)
        {
            return false;
        }

        proof = registration.Nonce.CreateProof(challenge!);
        return true;
    }

    public bool ValidateSession(string? token, string? origin)
    {
        return token is not null
            && origin is not null
            && active.TryGetValue(token, out var session)
            && string.Equals(session.Origin, origin, StringComparison.Ordinal);
    }

    public void RemoveSession(string token)
    {
        active.TryRemove(token, out _);
    }

    private void RemoveExpiredBootstraps()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var item in pending)
        {
            if (item.Value.ExpiresAt <= now)
            {
                pending.TryRemove(item.Key, out _);
            }
        }
    }

    private sealed record PendingBootstrap(
        BootstrapNonce Nonce,
        string Origin,
        DateTimeOffset ExpiresAt);
}

internal sealed record ActiveSession(string Token, string Origin);
