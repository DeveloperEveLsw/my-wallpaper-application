using Microsoft.Extensions.Primitives;

namespace Wallpaper.Seelen.M0.Companion;

internal static class RequestAuthentication
{
    private const string BearerPrefix = "Bearer ";

    public static bool TryGetBearerToken(
        IHeaderDictionary headers,
        out string? token)
    {
        token = null;
        if (!headers.TryGetValue("Authorization", out StringValues values)
            || values.Count != 1)
        {
            return false;
        }

        var authorization = values[0];
        if (authorization is null
            || !authorization.StartsWith(BearerPrefix, StringComparison.Ordinal)
            || authorization.Length == BearerPrefix.Length)
        {
            return false;
        }

        var candidate = authorization[BearerPrefix.Length..];
        if (!Base64Url.TryDecode256BitValue(candidate, out _))
        {
            return false;
        }

        token = candidate;
        return true;
    }
}
