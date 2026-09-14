using M3A.Repositories.Persistence;

namespace M3A.Repositories.Tests.Persistence;

/// <summary>Covers placeholder expansion for the connection string.</summary>
public class ConnectionStringTemplateTests
{
    private static string? Lookup(string name) => name switch
    {
        "DB_HOST" => "localhost",
        "DB_USERNAME" => "admin",
        "DB_PASSWORD" => "s3cret",
        _ => null,
    };

    [Fact]
    public void Expand_SubstitutesEveryPlaceholder()
    {
        var expanded = ConnectionStringTemplate.Expand(
            "Host=${DB_HOST};Username=${DB_USERNAME};Password=${DB_PASSWORD}", Lookup);

        Assert.Equal("Host=localhost;Username=admin;Password=s3cret", expanded);
    }

    [Fact]
    public void Expand_LeavesStringsWithoutPlaceholdersUntouched()
    {
        const string literal = "Host=localhost;Database=m3a_db;Username=admin;Password=pw";

        Assert.Equal(literal, ConnectionStringTemplate.Expand(literal, Lookup));
    }

    [Fact]
    public void Expand_NamesEveryMissingVariable_WhenOneIsNotSet()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ConnectionStringTemplate.Expand(
                "Host=${DB_HOST};Database=${DB_NAME};Port=${DB_PORT}", Lookup));

        Assert.Contains("DB_NAME", exception.Message, StringComparison.Ordinal);
        Assert.Contains("DB_PORT", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("DB_HOST", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Expand_TreatsEmptyVariableAsMissing() =>
        Assert.Throws<InvalidOperationException>(
            () => ConnectionStringTemplate.Expand("Password=${DB_PASSWORD}", _ => string.Empty));
}
