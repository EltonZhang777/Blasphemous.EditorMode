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

/// <summary>User-facing curated Content Catalog built from a verified creator probe.</summary>
internal sealed class ContentCatalog
{
    private readonly Dictionary<string, CreatorMetadata> _byType;
    private readonly Func<CreatorMetadata, PreviewInstance> _previewFactory;

    private ContentCatalog(
        IEnumerable<CreatorMetadata> available,
        IEnumerable<CreatorProbeDiagnostic> unavailable,
        Func<CreatorMetadata, PreviewInstance> previewFactory)
    {
        _byType = new Dictionary<string, CreatorMetadata>(StringComparer.OrdinalIgnoreCase);
        List<CreatorMetadata> entries = [];
        foreach (CreatorMetadata metadata in available ?? [])
        {
            if (metadata == null || !metadata.IsSupported || _byType.ContainsKey(metadata.Type))
                continue;

            _byType.Add(metadata.Type, metadata);
            entries.Add(metadata);
        }

        Entries = new ReadOnlyCollection<CreatorMetadata>(entries);
        Unavailable = new ReadOnlyCollection<CreatorProbeDiagnostic>(
            [.. unavailable ?? []]);
        _previewFactory = previewFactory ?? (metadata => new PreviewInstance(metadata, null));
    }

    internal ReadOnlyCollection<CreatorMetadata> Entries { get; }
    internal ReadOnlyCollection<CreatorProbeDiagnostic> Unavailable { get; }

    internal static ContentCatalog FromProbe(
        CreatorProbeResult probe,
        Func<CreatorMetadata, PreviewInstance> previewFactory = null)
    {
        if (probe == null)
            throw new ArgumentNullException(nameof(probe));

        return new ContentCatalog(probe.Available, probe.Diagnostics, previewFactory);
    }

    internal static ContentCatalog FromEntries(
        IEnumerable<CreatorMetadata> available,
        Func<CreatorMetadata, PreviewInstance> previewFactory = null)
    {
        return new ContentCatalog(available, [], previewFactory);
    }

    internal bool TryGet(string type, out CreatorMetadata metadata)
    {
        metadata = null;
        return !string.IsNullOrEmpty(type) && _byType.TryGetValue(type, out metadata);
    }

    internal IEnumerable<CreatorMetadata> Search(string text, string category)
    {
        foreach (CreatorMetadata metadata in Entries)
        {
            bool categoryMatches = string.IsNullOrEmpty(category)
                || string.Equals(metadata.Category, category, StringComparison.OrdinalIgnoreCase);
            bool textMatches = string.IsNullOrEmpty(text)
                || metadata.DisplayName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                || metadata.Type.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
            if (categoryMatches && textMatches)
                yield return metadata;
        }
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
            error = string.Format("Creator '{0}' is unavailable or unsupported.", type ?? "<null>");
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