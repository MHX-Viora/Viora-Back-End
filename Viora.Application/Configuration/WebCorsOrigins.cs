namespace Viora.Application.Configuration;

public static class WebCorsOrigins
{
    private static readonly string[] DefaultOrigins =
    [
        "http://localhost:5173",
        "http://localhost:3000",
        "http://localhost:8081",
        "http://127.0.0.1:8081",
        "https://vioraadmin.vercel.app"
    ];

    public static IReadOnlyList<string> Resolve(
        IEnumerable<string?>? configuredOrigins)
    {
        var configured = (configuredOrigins ?? [])
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .ToArray();
        var source = configured.Length > 0 ? configured : DefaultOrigins;

        var origins = source
            .Select(Normalize)
            .Where(origin => origin is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return origins;
    }

    private static string? Normalize(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme is not ("http" or "https") ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
}
