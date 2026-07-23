namespace Wallpaper.Seelen.M0.Companion;

public sealed class CompanionOptionsTests
{
    [Fact]
    public void TryParse_AcceptsExactM0Contract()
    {
        var nonce = Base64Url.CreateRandom256BitValue();

        var parsed = CompanionOptions.TryParse(
            [
                "--bootstrap-nonce",
                nonce,
                "--origin",
                OriginPolicy.SeelenAppOrigin,
            ],
            out var options,
            out var error);

        Assert.True(parsed, error);
        Assert.NotNull(options);
        Assert.Equal(CompanionOptions.DefaultPortStart, options.PortStart);
        Assert.Equal(CompanionOptions.DefaultPortCount, options.PortCount);
    }

    [Theory]
    [InlineData("HTTP://TAURI.LOCALHOST")]
    [InlineData("http://tauri.localhost/")]
    [InlineData("http://localhost")]
    [InlineData("*")]
    public void TryParse_RejectsNonExactOrigin(string origin)
    {
        var parsed = CompanionOptions.TryParse(
            [
                "--bootstrap-nonce",
                Base64Url.CreateRandom256BitValue(),
                "--origin",
                origin,
            ],
            out _,
            out _);

        Assert.False(parsed);
    }

    [Fact]
    public void TryParse_RejectsNonceThatIsNot256Bits()
    {
        var parsed = CompanionOptions.TryParse(
            [
                "--bootstrap-nonce",
                "not-a-256-bit-value",
                "--origin",
                OriginPolicy.SeelenAppOrigin,
            ],
            out _,
            out _);

        Assert.False(parsed);
    }
}
