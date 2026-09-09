using M3A.Domain.ValueObjects;

namespace M3A.Domain.Tests.ValueObjects;

/// <summary>Guards the shared id format contract: <c>[A-Za-z0-9\-]+</c>.</summary>
public class ResourceIdTests
{
    [Theory]
    [InlineData("abc123")]
    [InlineData("ABC-123")]
    [InlineData("a")]
    public void IsValid_ReturnsTrue_ForSupportedFormats(string id) =>
        Assert.True(ResourceId.IsValid(id));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("has space")]
    [InlineData("under_score")]
    [InlineData("slash/es")]
    public void IsValid_ReturnsFalse_ForUnsupportedFormats(string? id) =>
        Assert.False(ResourceId.IsValid(id));

    [Fact]
    public void New_ProducesValidId() => Assert.True(ResourceId.IsValid(ResourceId.New()));
}
