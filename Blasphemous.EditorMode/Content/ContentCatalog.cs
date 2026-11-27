using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blasphemous.EditorMode.Content;

/// <summary>Disposable preview that is never part of gameplay or the Room Overlay.</summary>
internal sealed class PreviewInstance : IDisposable
{
    private Action _dispose;
    private Action _followCursor;

    internal PreviewInstance(CreatorMetadata metadata, Action dispose, Action followCursor = null)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _dispose = dispose;
        _followCursor = followCursor;
    }

    internal CreatorMetadata Metadata { get; }
    internal bool IsGameplayIsolated { get { return true; } }
    internal bool AffectsRoomOverlay { get { return false; } }
    internal bool IsDisposed { get; private set; }

    internal void FollowCursor()
    {
        if (!IsDisposed && _followCursor != null)
            _followCursor();
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        Action dispose = _dispose;
        _dispose = null;
        _followCursor = null;
        dispose?.Invoke();
    }
}

/// <summary>Content Catalog built from the complete runtime creator inventory.</summary>
internal sealed class ContentCatalog
{
    private readonly Dictionary<string, CreatorMetadata> _byType;
    private readonly Func<CreatorMetadata, PreviewInstance> _previewFactory;

    private ContentCatalog(
        IEnumerable<CreatorMetadata> available,
        Func<CreatorMetadata, PreviewInstance> previewFactory)
    {
        _byType = new Dictionary<string, CreatorMetadata>(StringComparer.Ordinal);
        List<CreatorMetadata> entries = [];
        foreach (CreatorMetadata metadata in available ?? [])
        {
            if (metadata == null || _byType.ContainsKey(metadata.Type))
                continue;

            _byType.Add(metadata.Type, metadata);
            entries.Add(metadata);
        }

        Entries = new ReadOnlyCollection<CreatorMetadata>(entries);
        _previewFactory = previewFactory ?? (metadata => new PreviewInstance(metadata, null));
    }

    internal ReadOnlyCollection<CreatorMetadata> Entries { get; }

    internal static ContentCatalog FromInventory(
        RuntimeCreatorInventory inventory,
        Func<CreatorMetadata, PreviewInstance> previewFactory = null)
    {
        if (inventory == null)
            throw new ArgumentNullException(nameof(inventory));

        return new ContentCatalog(inventory.Entries, previewFactory);
    }

    internal static ContentCatalog FromEntries(
        IEnumerable<CreatorMetadata> available,
        Func<CreatorMetadata, PreviewInstance> previewFactory = null)
    {
        return new ContentCatalog(available, previewFactory);
    }

    internal bool TryGet(string type, out CreatorMetadata metadata)
    {
        metadata = null;
        return !string.IsNullOrEmpty(type) && _byType.TryGetValue(type, out metadata);
    }

    internal bool TryCreatePreview(
        string type,
        out PreviewInstance preview,
        out string error)
    {
        preview = null;
        error = null;
        if (!TryGet(type, out CreatorMetadata metadata))
        {
            error = string.Format(
                "Creator '{0}' is not present in the runtime creator inventory.",
                type ?? "<null>");
            return false;
        }

        try
        {
            preview = _previewFactory(metadata);
            if (preview == null)
            {
                error = "Preview factory returned no isolated preview.";
                return false;
            }

            if (!preview.IsGameplayIsolated || preview.AffectsRoomOverlay)
            {
                preview.Dispose();
                preview = null;
                error = "Preview factory returned a gameplay-affecting instance.";
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            error = "Preview creation failed: " + exception.Message;
            return false;
        }
    }
}