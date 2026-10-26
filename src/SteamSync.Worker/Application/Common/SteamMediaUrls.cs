using System.Text.RegularExpressions;

namespace SteamSync.Worker.Application.Common;

/// <summary>
/// Steam CDN helpers: persist 40-char content hashes; rebuild absolute URLs at read time.
/// </summary>
public static partial class SteamMediaUrls
{
    [GeneratedRegex(@"([0-9a-f]{40})(?:\.jpe?g)?(?:\?.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HashTailRegex();

    /// <summary>Extracts a 40-char hex hash from a Steam CDN URL, or returns a trimmed hash as-is.</summary>
    public static string? ToStorageHash(string? hashOrUrl)
    {
        if (string.IsNullOrWhiteSpace(hashOrUrl))
        {
            return null;
        }

        var value = hashOrUrl.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            var bare = value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(value)
                : value;
            return bare;
        }

        var match = HashTailRegex().Match(value);
        return match.Success ? match.Groups[1].Value : value;
    }

    public static string RequireTrimmedKey(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{fieldName} is required.", fieldName);
        }

        return value.Trim();
    }
}
