using Blasphemous.EditorMode.Components;
using System.Collections.Generic;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class EditorPointerHitPolicyTests
{
    [Fact]
    public void Editor_background_does_not_block_world_input()
    {
        EditorPointerHit[] hits =
        [
            new EditorPointerHit(
                belongsToEditor: true,
                hasEnabledSelectable: false,
                hasEnabledScrollRect: false)
        ];

        Assert.False(EditorPointerHitPolicy.IsPointerOverInteractiveControl(hits));
    }

    [Fact]
    public void Enabled_editor_button_blocks_world_input()
    {
        EditorPointerHit[] hits =
        [
            new EditorPointerHit(
                belongsToEditor: true,
                hasEnabledSelectable: true,
                hasEnabledScrollRect: false)
        ];

        Assert.True(EditorPointerHitPolicy.IsPointerOverInteractiveControl(hits));
    }

    [Fact]
    public void Enabled_editor_scroll_viewport_blocks_world_input()
    {
        List<EditorPointerHit> hits =
        [
            new EditorPointerHit(
                belongsToEditor: true,
                hasEnabledSelectable: false,
                hasEnabledScrollRect: true)
        ];

        Assert.True(EditorPointerHitPolicy.IsPointerOverInteractiveControl(hits));
    }

    [Fact]
    public void Control_outside_the_editor_does_not_block_world_input()
    {
        EditorPointerHit[] hits =
        [
            new EditorPointerHit(
                belongsToEditor: false,
                hasEnabledSelectable: true,
                hasEnabledScrollRect: true)
        ];

        Assert.False(EditorPointerHitPolicy.IsPointerOverInteractiveControl(hits));
    }

    [Fact]
    public void Empty_or_missing_hits_do_not_block_world_input()
    {
        Assert.False(EditorPointerHitPolicy.IsPointerOverInteractiveControl(null));
        Assert.False(EditorPointerHitPolicy.IsPointerOverInteractiveControl([]));
    }
}