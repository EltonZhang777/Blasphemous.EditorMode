using Blasphemous.EditorMode.Editing;
using System;

namespace Blasphemous.EditorMode.Components;

internal sealed class PlacementInspectorRefreshState
{
    private string _selectedId;
    private PlacementRecord _selectedPlacement;

    internal bool SelectionChanged { get; private set; }

    internal bool NeedsRefresh(PlacementRecord placement)
    {
        string selectedId = placement?.Id;
        SelectionChanged = !string.Equals(_selectedId, selectedId, StringComparison.OrdinalIgnoreCase);
        bool changed = SelectionChanged || !ReferenceEquals(_selectedPlacement, placement);
        if (!changed)
            return false;

        _selectedId = selectedId;
        _selectedPlacement = placement;
        return true;
    }
}