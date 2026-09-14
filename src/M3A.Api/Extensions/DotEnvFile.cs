namespace M3A.Api.Extensions;

/// <summary>
/// Minimal <c>.env</c> loader, so local development needs no secrets in committed config.
/// Variables already present in the real environment always win, which is what lets
/// production keep using its own secret store with this code path untouched.
/// </summary>
public static class DotEnvFile
{
    /// <summary>Name of the file searched for, at the repository root.</summary>
    public const string FileName = ".env";

    /// <summary>
    /// Finds the nearest <c>.env</c> walking up from <paramref name="startDirectory"/> and
    /// applies it to the process environment.
    /// </summary>
    /// <returns>The file that was loaded, or <c>null</c> when none was found.</returns>
    public static string? Load(string startDirectory, string fileName = FileName)
    {
        var path = Find(startDirectory, fileName);
        if (path is null)
        {
            return null;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            Apply(line);
        }

        return path;
    }

    /// <summary>Walks up the directory chain looking for <paramref name="fileName"/>.</summary>
    private static string? Find(string startDirectory, string fileName)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>Parses one <c>KEY=VALUE</c> line and sets it unless the variable already exists.</summary>
    private static void Apply(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return;
        }

        if (trimmed.StartsWith("export ", StringComparison.Ordinal))
        {
            trimmed = trimmed["export ".Length..].TrimStart();
        }

        var separator = trimmed.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return;
        }

        var key = trimmed[..separator].TrimEnd();
        if (key.Length == 0 || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
        {
            return;
        }

        Environment.SetEnvironmentVariable(key, Unquote(trimmed[(separator + 1)..].Trim()));
    }

    /// <summary>Strips one matching pair of surrounding quotes, if present.</summary>
    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]
            ? value[1..^1]
            : value;
}
