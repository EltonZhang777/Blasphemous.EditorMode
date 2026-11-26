using Blasphemous.EditorMode.Components;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class RawPropertiesDraftStateTests
{
    [Fact]
    public void Failed_apply_preserves_draft_until_a_non_preserving_refresh_reloads_committed_text()
    {
        RawPropertiesDraftState state = new RawPropertiesDraftState();
        Assert.True(state.Reload("placement-1", "old=1", false));
        Assert.True(state.Edit("draft=2"));

        Assert.False(state.Reload("placement-1", "old=1", true));
        Assert.Equal("draft=2", state.Text);

        Assert.True(state.Reload("placement-1", "old=1", false));
        Assert.Equal("old=1", state.Text);
    }

    [Fact]
    public void Successful_apply_reloads_normalized_committed_text()
    {
        RawPropertiesDraftState state = new RawPropertiesDraftState();
        state.Reload("placement-1", "old=1", false);
        state.Edit(" old=1 \r\n\r\n");

        Assert.True(state.Reload("placement-1", "old=1", false));
        Assert.Equal("old=1", state.Text);
    }

    [Fact]
    public void Selection_change_discards_draft_even_when_the_refresh_requests_preservation()
    {
        RawPropertiesDraftState state = new RawPropertiesDraftState();
        state.Reload("placement-1", "old=1", false);
        state.Edit("draft=2");

        Assert.True(state.Reload("placement-2", "new=3", true));
        Assert.Equal("new=3", state.Text);
    }

    [Fact]
    public void Selection_change_requests_a_view_reset_when_committed_text_is_unchanged()
    {
        RawPropertiesDraftState state = new RawPropertiesDraftState();
        state.Reload("placement-1", "same=1", false);

        Assert.True(state.Reload("placement-2", "same=1", true));
        Assert.Equal("same=1", state.Text);
    }

    [Fact]
    public void External_committed_text_change_discards_draft_even_when_preservation_is_requested()
    {
        RawPropertiesDraftState state = new RawPropertiesDraftState();
        state.Reload("placement-1", "old=1", false);
        state.Edit("draft=2");

        Assert.True(state.Reload("placement-1", "external=3", true));
        Assert.Equal("external=3", state.Text);
    }

    [Fact]
    public void A_non_preserving_reload_discards_a_draft_and_restores_committed_text()
    {
        RawPropertiesDraftState state = new RawPropertiesDraftState();
        state.Reload("placement-1", "old=1", false);
        state.Edit("draft=2");

        Assert.True(state.Reload("placement-1", "old=1", false));
        Assert.Equal("old=1", state.Text);
    }

    [Fact]
    public void Editing_an_unchanged_value_is_not_reported_as_a_user_edit()
    {
        RawPropertiesDraftState state = new RawPropertiesDraftState();
        Assert.True(state.Reload(null, string.Empty, false));

        Assert.False(state.Edit(state.Text));
    }

    [Fact]
    public void Ordinal_selection_comparison_discards_a_draft_when_only_case_changes()
    {
        RawPropertiesDraftState state = new RawPropertiesDraftState(StringComparer.Ordinal);
        state.Reload("decoration:DECORATION/HERO", "same=1", false);
        state.Edit("draft=2");

        Assert.True(state.Reload("decoration:decoration/hero", "same=1", true));
        Assert.Equal("same=1", state.Text);
    }
}