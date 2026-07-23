using System.Security.Cryptography;

namespace Wallpaper.Seelen.M0.Companion;

internal sealed class BootstrapNonce
{
    private readonly byte[] value;

    private BootstrapNonce(byte[] value, string encoded)
    {
        this.value = value;
        Encoded = encoded;
        Identifier = Base64Url.Encode(SHA256.HashData(value));
    }

    public string Encoded { get; }

    public string Identifier { get; }

    public static bool TryParse(string? encoded, out BootstrapNonce? nonce)
    {
        nonce = null;
        if (!Base64Url.TryDecode256BitValue(encoded, out var bytes))
        {
            return false;
        }

        nonce = new BootstrapNonce(bytes!, encoded!);
        return true;
    }

    public bool FixedTimeEquals(BootstrapNonce other)
    {
        return CryptographicOperations.FixedTimeEquals(value, other.value);
    }

    public string CreateProof(ReadOnlySpan<byte> challenge)
    {
        return Base64Url.Encode(HMACSHA256.HashData(value, challenge));
    }
}
