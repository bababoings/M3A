using System.Text.RegularExpressions;

namespace M3A.Domain.ValueObjects;

/// <summary>
/// The identifier format every resource in this service shares: <c>[A-Za-z0-9\-]+</c>.
/// </summary>
public static partial class ResourceId
{
    /// <summary>The raw pattern, exposed so route constraints and validators stay in sync.</summary>
    public const string Pattern = @"^[A-Za-z0-9\-]+$";

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex Matcher();

    /// <summary>Returns <c>true</c> when <paramref name="value"/> is a well formed resource id.</summary>
    public static bool IsValid(string? value) =>
        !string.IsNullOrEmpty(value) && Matcher().IsMatch(value);

    /// <summary>Generates a new identifier in the supported format.</summary>
    public static string New() => Guid.NewGuid().ToString("N");
}
