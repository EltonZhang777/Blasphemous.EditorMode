using Blasphemous.EditorMode.Components;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class PropertiesHorizontalScrollTests
{
    [Fact]
    public void Caret_right_of_viewport_scrolls_content_left()
    {
        float offset = PropertiesHorizontalScroll.FollowCaret(
            caretX: 450f,
            currentOffset: 0f,
            viewportWidth: 200f,
            contentWidth: 600f);

        Assert.Equal(-252f, offset, 3);
    }

    [Fact]
    public void Caret_left_of_viewport_scrolls_content_right()
    {
        float offset = PropertiesHorizontalScroll.FollowCaret(
            caretX: 50f,
            currentOffset: -300f,
            viewportWidth: 200f,
            contentWidth: 600f);

        Assert.Equal(-48f, offset, 3);
    }

    [Fact]
    public void Offset_is_clamped_to_content_bounds()
    {
        float offset = PropertiesHorizontalScroll.FollowCaret(
            caretX: 1000f,
            currentOffset: 0f,
            viewportWidth: 200f,
            contentWidth: 300f);

        Assert.Equal(-100f, offset, 3);
    }

    [Fact]
    public void Short_content_never_scrolls()
    {
        float offset = PropertiesHorizontalScroll.FollowCaret(
            caretX: 500f,
            currentOffset: -50f,
            viewportWidth: 200f,
            contentWidth: 150f);

        Assert.Equal(0f, offset, 3);
    }

    [Fact]
    public void Non_positive_viewport_preserves_current_offset()
    {
        float offset = PropertiesHorizontalScroll.FollowCaret(
            caretX: 50f,
            currentOffset: -25f,
            viewportWidth: 0f,
            contentWidth: 600f);

        Assert.Equal(-25f, offset, 3);
    }
}