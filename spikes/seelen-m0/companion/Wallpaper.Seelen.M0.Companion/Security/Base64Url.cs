using System.Buffers.Text;
using System.Security.Cryptography;

namespace Wallpaper.Seelen.M0.Companion;

internal static class Base64Url
{
    public static string CreateRandom256BitValue()
    {
        return Encode(RandomNumberGenerator.GetBytes(32));
    }

    public static string Encode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode256BitValue(string? encoded, out byte[]? bytes)
    {
        bytes = null;
        if (encoded is null || encoded.Length != 43)
        {
            return false;
        }

        Span<byte> utf8 = stackalloc byte[44];
        for (var index = 0; index < encoded.Length; index++)
        {
            var character = encoded[index] switch
            {
                '-' => '+',
                '_' => '/',
                var value => value,
            };

            if (character > 127)
            {
                return false;
            }

            utf8[index] = (byte)character;
        }

        utf8[43] = (byte)'=';
        Span<byte> decoded = stackalloc byte[33];
        var status = Base64.DecodeFromUtf8(utf8, decoded, out var consumed, out var written);
        if (status != System.Buffers.OperationStatus.Done
            || consumed != utf8.Length
            || written != 32)
        {
            return false;
        }

        var result = decoded[..written].ToArray();
        if (!string.Equals(Encode(result), encoded, StringComparison.Ordinal))
        {
            return false;
        }

        bytes = result;
        return true;
    }
}
