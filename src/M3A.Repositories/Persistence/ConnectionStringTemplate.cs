using System.Text.RegularExpressions;

namespace M3A.Repositories.Persistence;

/// <summary>
/// Expands <c>${VARIABLE}</c> placeholders in a connection string. Committed config carries
/// only the shape of the connection; the credentials arrive from the environment.
/// </summary>
public static partial class ConnectionStringTemplate
{
    [GeneratedRegex(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    /// <summary>
    /// Replaces every placeholder in <paramref name="template"/> with the value
    /// <paramref name="lookup"/> returns for it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// One or more placeholders resolved to nothing. Thrown at startup, with the variable
    /// names, rather than letting it surface later as an opaque connection failure.
    /// </exception>
    public static string Expand(string template, Func<string, string?> lookup)
    {
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        var expanded = Placeholder().Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            var value = lookup(name);
            if (string.IsNullOrEmpty(value))
            {
                missing.Add(name);
                return match.Value;
            }

            return value;
        });

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The connection string references environment variable(s) that are not set: " +
                $"{string.Join(", ", missing)}. Set them in the .env file at the repository root " +
                $"(copy .env.example) or in the environment.");
        }

        return expanded;
    }
}
