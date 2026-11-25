namespace Blasphemous.EditorMode.Components;

internal readonly struct EditorPointerHit
{
    internal bool BelongsToEditor { get; }
    internal bool HasEnabledSelectable { get; }
    internal bool HasEnabledScrollRect { get; }
    internal bool IsInteractiveControl
    {
        get { return BelongsToEditor && (HasEnabledSelectable || HasEnabledScrollRect); }
    }

    internal EditorPointerHit(
        bool belongsToEditor,
        bool hasEnabledSelectable,
        bool hasEnabledScrollRect)
    {
        BelongsToEditor = belongsToEditor;
        HasEnabledSelectable = hasEnabledSelectable;
        HasEnabledScrollRect = hasEnabledScrollRect;
    }
}