using Blasphemous.EditorMode.Components;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class RawPropertiesDraftStateTests
{
    [Fact]
    public void FailedApplyPreservesDraftUntilANonPreservingRefreshReloadsCommittedText()
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
    public void SuccessfulApplyReloadsNormalizedCommittedText()
    {
        RawPropertiesDraftState state = new RawPropertiesDraftState();
        state.Reload("placement-1", "old=1", false);
        state.Edit(" old=1 \r\n\r\n");

        Assert.True(state.Reload("placement-1", "old=1", false));
        Assert.Equal("old=1", state.Text);
    }

    [Fact]
    public void SelectionChangeDiscardsDraftEvenWhenTheRefreshRequestsPreservation()
    {
        RawPropertiesDraftState state = new RawPropertiesDraftState();
        state.Reload("placement-1", "old=1", false);
        state.Edit("draft=2");

        Assert.True(state.Reload("placement-2", "new=3", true));
        Assert.Equal("new=3", state.Text);
    }

    [Fact]
    public void SelectionChangeRequestsAViewResetWhenCommittedTextIsUnchanged()
    {
        RawPropertiesDraftState state = new RawPropertiesDraftState();
        state.Reload("placement-1", "same=1", false);

        Assert.True(state.Reload("placement-2", "same=1", true));
        Assert.Equal("same=1", state.Text);
    }

    [Fact]
    public void ExternalCommittedTextChangeDiscardsDraftEvenWhenPreservationIsRequested()
    {
        RawPropertiesDraftState state = new RawPropertiesDraftState();
        state.Reload("placement-1", "old=1", false);
        state.Edit("draft=2");

        Assert.True(state.Reload("placement-1", "external=3", true));
        Assert.Equal("external=3", state.Text);
    }

    [Fact]
    public void ANonPreservingReloadDiscardsADraftAndRestoresCommittedText()
    {
        RawPropertiesDraftState state = new RawPropertiesDraftState();
        state.Reload("placement-1", "old=1", false);
        state.Edit("draft=2");

        Assert.True(state.Reload("placement-1", "old=1", false));
        Assert.Equal("old=1", state.Text);
    }

    [Fact]
    public void EditingAnUnchangedValueIsNotReportedAsAUserEdit()
    {
        RawPropertiesDraftState state = new RawPropertiesDraftState();
        Assert.True(state.Reload(null, string.Empty, false));

        Assert.False(state.Edit(state.Text));
    }
}