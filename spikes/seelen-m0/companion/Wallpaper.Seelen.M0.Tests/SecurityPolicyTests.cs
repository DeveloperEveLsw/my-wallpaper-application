using Microsoft.AspNetCore.Http;

namespace Wallpaper.Seelen.M0.Companion;

public sealed class SecurityPolicyTests
{
    [Fact]
    public void HostPolicy_UsesExactIpv4LoopbackHostAndPort()
    {
        var policy = new HostPolicy(43127);

        Assert.True(policy.IsAllowed("127.0.0.1:43127"));
        Assert.False(policy.IsAllowed("localhost:43127"));
        Assert.False(policy.IsAllowed("127.0.0.1"));
        Assert.False(policy.IsAllowed("127.0.0.1:43128"));
        Assert.False(policy.IsAllowed("127.0.0.1:43127.example"));
    }

    [Fact]
    public void BearerToken_RequiresOneValid256BitValue()
    {
        var token = Base64Url.CreateRandom256BitValue();
        var headers = new HeaderDictionary();
        headers["Authorization"] = $"Bearer {token}";

        var parsed = RequestAuthentication.TryGetBearerToken(headers, out var actual);

        Assert.True(parsed);
        Assert.Equal(token, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bearer abc")]
    [InlineData("Bearer short")]
    [InlineData("Basic abc")]
    public void BearerToken_RejectsInvalidAuthorization(string authorization)
    {
        var headers = new HeaderDictionary();
        headers["Authorization"] = authorization;

        Assert.False(RequestAuthentication.TryGetBearerToken(headers, out _));
    }
}
