namespace Wallpaper.Seelen.M0.Companion;

public sealed class SessionRegistryTests
{
    [Fact]
    public void BootstrapProofAndHello_ConsumeNonceExactlyOnce()
    {
        Assert.True(
            BootstrapNonce.TryParse(Base64Url.CreateRandom256BitValue(), out var nonce));
        var sessions = new SessionRegistry();
        sessions.RegisterBootstrap(nonce!, OriginPolicy.SeelenAppOrigin);
        var challenge = Base64Url.CreateRandom256BitValue();

        var proofCreated = sessions.TryCreateBootstrapProof(
            nonce!.Identifier,
            OriginPolicy.SeelenAppOrigin,
            challenge,
            out var proof);
        var authenticated = sessions.TryAuthenticate(
            nonce.Encoded,
            OriginPolicy.SeelenAppOrigin,
            out var session);
        var replayed = sessions.TryAuthenticate(
            nonce.Encoded,
            OriginPolicy.SeelenAppOrigin,
            out _);

        Assert.True(proofCreated);
        Assert.Equal(
            nonce.CreateProof(Decode(challenge)),
            proof);
        Assert.True(authenticated);
        Assert.NotNull(session);
        Assert.False(replayed);
        Assert.True(
            sessions.ValidateSession(session.Token, OriginPolicy.SeelenAppOrigin));
        Assert.False(sessions.ValidateSession(session.Token, "http://localhost"));
    }

    [Fact]
    public void BootstrapExpiresWithoutCreatingSession()
    {
        var time = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero));
        var sessions = new SessionRegistry(time);
        Assert.True(
            BootstrapNonce.TryParse(Base64Url.CreateRandom256BitValue(), out var nonce));
        sessions.RegisterBootstrap(nonce!, OriginPolicy.SeelenAppOrigin);
        time.Advance(TimeSpan.FromSeconds(31));

        var authenticated = sessions.TryAuthenticate(
            nonce!.Encoded,
            OriginPolicy.SeelenAppOrigin,
            out _);

        Assert.False(authenticated);
    }

    private static byte[] Decode(string encoded)
    {
        Assert.True(Base64Url.TryDecode256BitValue(encoded, out var bytes));
        return bytes!;
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset value = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return value;
        }

        public void Advance(TimeSpan duration)
        {
            value = value.Add(duration);
        }
    }
}
