using System.Net;

namespace Wallpaper.Seelen.Bootstrap;

internal sealed record CompanionOptions(
    BootstrapNonce BootstrapNonce,
    string Origin,
    int PortStart,
    int PortCount)
{
    public const int DefaultPortStart = 43127;
    public const int DefaultPortCount = 9;
    public const int MaximumPortCount = 32;

    public IEnumerable<int> CandidatePorts =>
        Enumerable.Range(PortStart, PortCount);

    public static bool TryParse(
        string[] args,
        out CompanionOptions? options,
        out string? error)
    {
        options = null;
        error = null;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                error = "Arguments must be supplied as --name value pairs.";
                return false;
            }

            if (!values.TryAdd(args[index], args[index + 1]))
            {
                error = $"Duplicate argument: {args[index]}";
                return false;
            }
        }

        if (!values.TryGetValue("--bootstrap-nonce", out var encodedNonce)
            || !BootstrapNonce.TryParse(encodedNonce, out var nonce))
        {
            error = "--bootstrap-nonce must be a 256-bit base64url value.";
            return false;
        }

        if (!values.TryGetValue("--origin", out var origin)
            || !OriginPolicy.IsAllowed(origin))
        {
            error = $"--origin must exactly match {OriginPolicy.SeelenAppOrigin}.";
            return false;
        }

        if (!TryReadInt(values, "--port-start", DefaultPortStart, out var portStart)
            || portStart is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            error = "--port-start must be a valid TCP port.";
            return false;
        }

        if (!TryReadInt(values, "--port-count", DefaultPortCount, out var portCount)
            || portCount is < 1 or > MaximumPortCount
            || portStart + portCount - 1 > IPEndPoint.MaxPort)
        {
            error = $"--port-count must be between 1 and {MaximumPortCount}.";
            return false;
        }

        var knownArguments = new HashSet<string>(StringComparer.Ordinal)
        {
            "--bootstrap-nonce",
            "--origin",
            "--port-start",
            "--port-count",
        };

        var unknownArgument = values.Keys.FirstOrDefault(key => !knownArguments.Contains(key));
        if (unknownArgument is not null)
        {
            error = $"Unknown argument: {unknownArgument}";
            return false;
        }

        options = new CompanionOptions(nonce!, origin, portStart, portCount);
        return true;
    }

    private static bool TryReadInt(
        IReadOnlyDictionary<string, string> values,
        string key,
        int defaultValue,
        out int value)
    {
        if (!values.TryGetValue(key, out var text))
        {
            value = defaultValue;
            return true;
        }

        return int.TryParse(
            text,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }
}
