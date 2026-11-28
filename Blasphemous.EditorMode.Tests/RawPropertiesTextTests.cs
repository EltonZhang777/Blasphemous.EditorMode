using Blasphemous.EditorMode.Components;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class RawPropertiesTextTests
{
    [Fact]
    public void CreationPreservesCommittedOrderAndRawValues()
    {
        string[] properties =
        [
            "  length = 3.0  ",
            "\tOneWayDown",
            "color = #ff00ff"
        ];

        RawPropertiesText result = RawPropertiesText.Create(properties);

        Assert.Equal(
            "  length = 3.0  \n\tOneWayDown\ncolor = #ff00ff",
            result.Text);
        Assert.False(result.ContainsLineBreak);
    }

    [Fact]
    public void CreationUsesEmptyTextWhenNoPropertiesExist()
    {
        RawPropertiesText result = RawPropertiesText.Create([]);

        Assert.Equal(string.Empty, result.Text);
        Assert.False(result.ContainsLineBreak);
    }

    [Theory]
    [InlineData("note=first\nsecond")]
    [InlineData("note=first\rsecond")]
    [InlineData("note=first\r\nsecond")]
    public void CreationDetectsEmbeddedLineBreaksWithoutRewritingThem(string property)
    {
        RawPropertiesText result = RawPropertiesText.Create([property]);

        Assert.Equal(property, result.Text);
        Assert.True(result.ContainsLineBreak);
    }
}