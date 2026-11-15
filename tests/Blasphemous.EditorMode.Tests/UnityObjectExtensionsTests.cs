using Blasphemous.EditorMode.Lifecycle;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class UnityObjectExtensionsTests
{
    [Fact]
    public void DestroyedUnityObjectIsNotAlive()
    {
        UnityEngine.Object widget = new() { Destroyed = true };

        Assert.NotNull(widget);
        Assert.False(widget.IsAlive());
    }
}
