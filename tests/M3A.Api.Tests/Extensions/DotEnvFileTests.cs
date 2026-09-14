using M3A.Api.Extensions;

namespace M3A.Api.Tests.Extensions;

/// <summary>
/// Covers the <c>.env</c> parser. Each test uses variable names unique to itself, because
/// loading writes to the process environment shared by the whole test run.
/// </summary>
public class DotEnvFileTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"m3a-env-{Guid.NewGuid():N}")).FullName;

    private readonly List<string> _touchedVariables = [];

    private string WriteEnvFile(string contents, string? subdirectory = null)
    {
        var directory = subdirectory is null
            ? _directory
            : Directory.CreateDirectory(Path.Combine(_directory, subdirectory)).FullName;

        var path = Path.Combine(_directory, DotEnvFile.FileName);
        File.WriteAllText(path, contents);
        return directory;
    }

    private string Track(string name)
    {
        _touchedVariables.Add(name);
        return name;
    }

    [Fact]
    public void Load_ReturnsNull_WhenNoFileExists() =>
        Assert.Null(DotEnvFile.Load(_directory, $"absent-{Guid.NewGuid():N}"));

    [Fact]
    public void Load_SetsVariables_IgnoringCommentsAndBlankLines()
    {
        var key = Track($"M3A_TEST_HOST_{Guid.NewGuid():N}");
        var start = WriteEnvFile($"# a comment\n\n{key}=localhost\n");

        Assert.NotNull(DotEnvFile.Load(start));
        Assert.Equal("localhost", Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void Load_StripsSurroundingQuotes_AndHonoursExportPrefix()
    {
        var quoted = Track($"M3A_TEST_QUOTED_{Guid.NewGuid():N}");
        var exported = Track($"M3A_TEST_EXPORT_{Guid.NewGuid():N}");
        var start = WriteEnvFile($"{quoted}=\"pa ss\"\nexport {exported}='value'\n");

        DotEnvFile.Load(start);

        Assert.Equal("pa ss", Environment.GetEnvironmentVariable(quoted));
        Assert.Equal("value", Environment.GetEnvironmentVariable(exported));
    }

    [Fact]
    public void Load_DoesNotOverrideAVariableAlreadySetInTheEnvironment()
    {
        var key = Track($"M3A_TEST_EXISTING_{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable(key, "from-environment");
        var start = WriteEnvFile($"{key}=from-file\n");

        DotEnvFile.Load(start);

        Assert.Equal("from-environment", Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void Load_FindsTheFileByWalkingUpFromANestedDirectory()
    {
        var key = Track($"M3A_TEST_NESTED_{Guid.NewGuid():N}");
        var nested = WriteEnvFile($"{key}=found\n", Path.Combine("src", "M3A.Api"));

        Assert.NotNull(DotEnvFile.Load(nested));
        Assert.Equal("found", Environment.GetEnvironmentVariable(key));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        foreach (var name in _touchedVariables)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        Directory.Delete(_directory, recursive: true);
    }
}
