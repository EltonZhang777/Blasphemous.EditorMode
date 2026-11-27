using Blasphemous.EditorMode.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blasphemous.EditorMode.Content;

internal enum CreatorCatalogSourceState
{
    Unavailable,
    NotRequested,
    Loading,
    Ready,
    Failed
}

/// <summary>Whether one catalog action can run, with the reason when it cannot.</summary>
internal sealed class CreatorCatalogActionAvailability
{
    private CreatorCatalogActionAvailability(bool available, string unavailableReason)
    {
        Available = available;
        UnavailableReason = unavailableReason ?? string.Empty;
    }

    internal bool Available { get; }
    internal string UnavailableReason { get; }

    internal static CreatorCatalogActionAvailability Enabled()
    {
        return new CreatorCatalogActionAvailability(true, string.Empty);
    }

    internal static CreatorCatalogActionAvailability Disabled(string reason)
    {
        return new CreatorCatalogActionAvailability(
            false,
            string.IsNullOrEmpty(reason) ? "Action is unavailable." : reason);
    }
}

/// <summary>One visible creator row and its current source state.</summary>
internal sealed class CreatorCatalogRowSnapshot
{
    internal CreatorCatalogRowSnapshot(
        CreatorMetadata metadata,
        CreatorCatalogSourceState sourceState,
        string reason,
        bool isSelected)
    {
        Metadata = metadata;
        SourceState = sourceState;
        Reason = reason ?? string.Empty;
        IsSelected = isSelected;
    }

    internal CreatorMetadata Metadata { get; }
    internal CreatorCatalogSourceState SourceState { get; }
    internal string Reason { get; }
    internal bool IsSelected { get; }
    internal string Status { get { return SourceState.ToString(); } }
}

/// <summary>Immutable view of the current creator catalog page and selection.</summary>
internal sealed class CreatorCatalogSnapshot
{
    internal CreatorCatalogSnapshot(
        RuntimeCreatorInventoryState registryState,
        string registryMessage,
        IEnumerable<CreatorInventoryDiagnostic> diagnostics,
        IEnumerable<CreatorCatalogRowSnapshot> rows,
        int pageIndex,
        int pageCount,
        int entryCount,
        CreatorMetadata selectedEntry,
        CreatorCatalogSourceState selectedSourceState,
        CreatorCatalogActionAvailability preview,
        CreatorCatalogActionAvailability place,
        string detailsText)
    {
        RegistryState = registryState;
        RegistryMessage = registryMessage ?? string.Empty;
        Diagnostics = new ReadOnlyCollection<CreatorInventoryDiagnostic>(
            [.. diagnostics ?? []]);
        Rows = new ReadOnlyCollection<CreatorCatalogRowSnapshot>(
            [.. rows ?? []]);
        PageIndex = pageIndex;
        PageCount = pageCount;
        EntryCount = entryCount;
        SelectedEntry = selectedEntry;
        SelectedSourceState = selectedSourceState;
        Preview = preview ?? throw new ArgumentNullException(nameof(preview));
        Place = place ?? throw new ArgumentNullException(nameof(place));
        DetailsText = detailsText ?? string.Empty;
    }

    internal RuntimeCreatorInventoryState RegistryState { get; }
    internal string RegistryMessage { get; }
    internal ReadOnlyCollection<CreatorInventoryDiagnostic> Diagnostics { get; }
    internal ReadOnlyCollection<CreatorCatalogRowSnapshot> Rows { get; }
    internal int PageIndex { get; }
    internal int PageCount { get; }
    internal int EntryCount { get; }
    internal bool HasPreviousPage { get { return PageIndex > 0; } }
    internal bool HasNextPage { get { return PageCount > 0 && PageIndex + 1 < PageCount; } }
    internal CreatorMetadata SelectedEntry { get; }
    internal CreatorCatalogSourceState SelectedSourceState { get; }
    internal CreatorCatalogActionAvailability Preview { get; }
    internal CreatorCatalogActionAvailability Place { get; }
    internal string DetailsText { get; }
}

/// <summary>Unity-independent state boundary for browsing and selecting creators.</summary>
internal sealed class CreatorCatalogController
{
    internal const int PageSize = 5;

    private sealed class SourceState
    {
        internal SourceState(CreatorCatalogSourceState state, string reason)
        {
            State = state;
            Reason = reason;
        }

        internal CreatorCatalogSourceState State { get; }
        internal string Reason { get; }
    }

    private readonly RuntimeCreatorInventory _inventory;
    private readonly Dictionary<string, SourceState> _sourceStates =
        new(StringComparer.Ordinal);
    private readonly Func<string, bool> _hasLoadedSource;
    private readonly Queue<string> _requiredSourceLoads = new();
    private readonly HashSet<string> _requiredSourceLoadSet =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _invalidatedLoadedSourceTypes =
        new(StringComparer.Ordinal);
    private int _pageIndex;
    private string _selectedType;
    private string _requestedSourceLoad;
    private string _activeSourceLoadType;
    private int _sourceLoadGeneration;
    private bool _placementReady;
    private string _placementUnavailableReason = "Placement editing is not ready.";

    internal CreatorCatalogController(
        RuntimeCreatorInventory inventory,
        Func<string, bool> hasLoadedSource = null,
        IEnumerable<string> invalidatedLoadedSourceTypes = null)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _hasLoadedSource = hasLoadedSource;
        if (invalidatedLoadedSourceTypes != null)
        {
            foreach (string type in invalidatedLoadedSourceTypes)
            {
                if (!string.IsNullOrEmpty(type))
                    _invalidatedLoadedSourceTypes.Add(type);
            }
        }
        foreach (CreatorMetadata metadata in inventory.Entries)
            _sourceStates[metadata.Type] = CreateInitialSourceState(metadata);
    }

    internal CreatorMetadata SelectedEntry
    {
        get
        {
            CreatorMetadata metadata;
            return _inventory.TryGetExact(_selectedType, out metadata) ? metadata : null;
        }
    }

    internal string SelectedType
    {
        get { return SelectedEntry == null ? null : SelectedEntry.Type; }
    }

    internal int PageIndex { get { return _pageIndex; } }

    internal int PageCount
    {
        get
        {
            int count = _inventory.Entries.Count;
            return count == 0 ? 0 : ((count - 1) / PageSize) + 1;
        }
    }

    internal bool TrySelect(string type)
    {
        CreatorMetadata metadata;
        if (!_inventory.TryGetExact(type, out metadata))
            return false;

        _selectedType = metadata.Type;
        RequestSelectedSourceLoad(metadata);
        return true;
    }

    internal void RequireSourceLoad(string type)
    {
        CreatorMetadata metadata;
        if (!_inventory.TryGetExact(type, out metadata) || !metadata.HasLoader)
            return;

        CreatorCatalogSourceState state = GetSourceState(metadata.Type);
        if (!NeedsSourceLoad(state))
            return;
        if (!_requiredSourceLoadSet.Add(metadata.Type))
            return;

        _requiredSourceLoads.Enqueue(metadata.Type);
    }

    internal bool TryBeginNextSourceLoad(out string type, out int generation)
    {
        type = null;
        generation = _sourceLoadGeneration;
        if (_activeSourceLoadType != null)
            return false;

        while (true)
        {
            string candidate;
            if (_requiredSourceLoads.Count > 0)
            {
                candidate = _requiredSourceLoads.Dequeue();
                _requiredSourceLoadSet.Remove(candidate);
            }
            else if (_requestedSourceLoad != null)
            {
                candidate = _requestedSourceLoad;
                _requestedSourceLoad = null;
            }
            else
            {
                return false;
            }

            if (string.Equals(candidate, _requestedSourceLoad, StringComparison.Ordinal))
                _requestedSourceLoad = null;

            CreatorMetadata metadata;
            if (!_inventory.TryGetExact(candidate, out metadata))
                continue;

            CreatorCatalogSourceState state = GetSourceState(metadata.Type);
            if (!NeedsSourceLoad(state))
                continue;
            if (!metadata.HasLoader)
            {
                TrySetSourceState(metadata.Type, CreatorCatalogSourceState.Unavailable, null);
                continue;
            }
            if (!_invalidatedLoadedSourceTypes.Contains(metadata.Type)
                && _hasLoadedSource != null
                && _hasLoadedSource(metadata.Type))
            {
                TrySetSourceState(metadata.Type, CreatorCatalogSourceState.Ready, null);
                continue;
            }

            if (!TrySetSourceState(
                metadata.Type,
                CreatorCatalogSourceState.Loading,
                "Source is loading."))
                continue;

            _activeSourceLoadType = metadata.Type;
            type = metadata.Type;
            generation = _sourceLoadGeneration;
            return true;
        }
    }

    internal bool CompleteSourceLoad(
        string type,
        int generation,
        bool succeeded,
        string reason)
    {
        if (!string.Equals(_activeSourceLoadType, type, StringComparison.Ordinal))
            return false;

        bool currentGeneration = generation == _sourceLoadGeneration;
        _activeSourceLoadType = null;
        if (!currentGeneration)
            return false;

        return TrySetSourceState(
            type,
            succeeded
                ? CreatorCatalogSourceState.Ready
                : CreatorCatalogSourceState.Failed,
            reason);
    }

    internal void InvalidateActiveSourceLoad()
    {
        _requestedSourceLoad = null;
        if (_activeSourceLoadType == null)
            return;

        string active = _activeSourceLoadType;
        _sourceLoadGeneration++;
        _invalidatedLoadedSourceTypes.Add(active);
        _sourceStates[active] = new SourceState(
            CreatorCatalogSourceState.NotRequested,
            "Source load was cancelled; select the creator to retry.");
    }

    internal ReadOnlyCollection<string> GetInvalidatedLoadedSourceTypes()
    {
        return new ReadOnlyCollection<string>([.. _invalidatedLoadedSourceTypes]);
    }

    internal void ResetSourceStates()
    {
        _sourceLoadGeneration++;
        _requestedSourceLoad = null;
        _requiredSourceLoads.Clear();
        _requiredSourceLoadSet.Clear();
        if (_activeSourceLoadType != null)
            _invalidatedLoadedSourceTypes.Add(_activeSourceLoadType);

        foreach (CreatorMetadata metadata in _inventory.Entries)
            _sourceStates[metadata.Type] = CreateInitialSourceState(metadata);
    }

    internal bool PreviousPage()
    {
        if (_pageIndex <= 0)
            return false;

        _pageIndex--;
        return true;
    }

    internal bool NextPage()
    {
        if (_pageIndex + 1 >= PageCount)
            return false;

        _pageIndex++;
        return true;
    }

    internal bool TrySetSourceState(
        string type,
        CreatorCatalogSourceState state,
        string reason)
    {
        CreatorMetadata metadata;
        if (!_inventory.TryGetExact(type, out metadata))
            return false;
        if (state == CreatorCatalogSourceState.Ready && !metadata.HasLoader)
            return false;

        if (state == CreatorCatalogSourceState.Ready)
            _invalidatedLoadedSourceTypes.Remove(metadata.Type);
        else if (state == CreatorCatalogSourceState.Failed)
            _invalidatedLoadedSourceTypes.Add(metadata.Type);

        _sourceStates[metadata.Type] = new SourceState(
            state,
            NormalizeReason(metadata, state, reason));
        return true;
    }

    internal void SetPlacementReady(bool ready, string reason = null)
    {
        _placementReady = ready;
        _placementUnavailableReason = string.IsNullOrEmpty(reason)
            ? "Placement editing is not ready."
            : reason;
    }

    internal CreatorCatalogSourceState GetSourceState(string type)
    {
        SourceState sourceState;
        return _sourceStates.TryGetValue(type ?? string.Empty, out sourceState)
            ? sourceState.State
            : CreatorCatalogSourceState.Unavailable;
    }

    internal string GetSourceReason(string type)
    {
        SourceState sourceState;
        return _sourceStates.TryGetValue(type ?? string.Empty, out sourceState)
            ? sourceState.Reason
            : "Creator is not available in the runtime inventory.";
    }

    internal bool PreviewAvailable
    {
        get { return string.IsNullOrEmpty(PreviewUnavailableReason); }
    }

    internal string PreviewUnavailableReason
    {
        get
        {
            if (!_inventory.IsRegistryAvailable)
                return RegistryFailureReason();

            CreatorMetadata selected = SelectedEntry;
            if (selected == null)
                return "Select a creator from the catalog.";
            if (!selected.HasLoader)
                return GetSourceReason(selected.Type);

            CreatorCatalogSourceState state = GetSourceState(selected.Type);
            return state == CreatorCatalogSourceState.Ready
                ? null
                : GetSourceReason(selected.Type);
        }
    }

    internal bool PlaceAvailable
    {
        get { return string.IsNullOrEmpty(PlaceUnavailableReason); }
    }

    internal string PlaceUnavailableReason
    {
        get
        {
            string previewReason = PreviewUnavailableReason;
            if (!string.IsNullOrEmpty(previewReason))
                return previewReason;

            CreatorMetadata selected = SelectedEntry;
            if (!selected.HasModifier)
                return "Creator has no modifier; Place is unavailable.";
            if (!_placementReady)
                return _placementUnavailableReason;

            return null;
        }
    }

    internal CreatorCatalogSnapshot CreateSnapshot()
    {
        List<CreatorCatalogRowSnapshot> rows = [];
        int first = _pageIndex * PageSize;
        for (int index = first; index < _inventory.Entries.Count && index < first + PageSize; index++)
        {
            CreatorMetadata metadata = _inventory.Entries[index];
            rows.Add(new CreatorCatalogRowSnapshot(
                metadata,
                GetSourceState(metadata.Type),
                GetSourceReason(metadata.Type),
                string.Equals(metadata.Type, _selectedType, StringComparison.Ordinal)));
        }

        CreatorMetadata selected = SelectedEntry;
        CreatorCatalogActionAvailability preview = PreviewAvailability;
        CreatorCatalogActionAvailability place = PlaceAvailability;
        return new CreatorCatalogSnapshot(
            _inventory.State,
            _inventory.Message,
            _inventory.Diagnostics,
            rows,
            _pageIndex,
            PageCount,
            _inventory.Entries.Count,
            selected,
            selected == null
                ? CreatorCatalogSourceState.Unavailable
                : GetSourceState(selected.Type),
            preview,
            place,
            BuildDetailsText());
    }

    private CreatorCatalogActionAvailability PreviewAvailability
    {
        get
        {
            string reason = PreviewUnavailableReason;
            return string.IsNullOrEmpty(reason)
                ? CreatorCatalogActionAvailability.Enabled()
                : CreatorCatalogActionAvailability.Disabled(reason);
        }
    }

    private CreatorCatalogActionAvailability PlaceAvailability
    {
        get
        {
            string reason = PlaceUnavailableReason;
            return string.IsNullOrEmpty(reason)
                ? CreatorCatalogActionAvailability.Enabled()
                : CreatorCatalogActionAvailability.Disabled(reason);
        }
    }

    private string BuildDetailsText()
    {
        if (!_inventory.IsRegistryAvailable)
            return "Registry unavailable.\n" + FirstDiagnosticMessage();
        if (_inventory.Entries.Count == 0)
        {
            string details = "No creators registered.\n" + _inventory.Message;
            string diagnostics = DiagnosticSummary();
            return string.IsNullOrEmpty(diagnostics)
                ? details
                : details + "\n" + diagnostics;
        }

        CreatorMetadata selected = SelectedEntry;
        if (selected == null)
        {
            string details = "Select a creator from the list.";
            string diagnostics = DiagnosticSummary();
            if (!string.IsNullOrEmpty(diagnostics))
                details += "\n" + diagnostics;
            return details;
        }

        CreatorCatalogActionAvailability preview = PreviewAvailability;
        CreatorCatalogActionAvailability place = PlaceAvailability;
        CreatorCatalogSourceState selectedState = GetSourceState(selected.Type);
        string detailsText = string.Format(
            "{0} [{1}] - {2}{3}\n"
                + "Category: {4} | Source: {5}\n"
                + "Scene: {6} | Path: {7}\n"
                + "Transform: {8}\n"
                + "Preview: {9}\n"
                + "Place: {10}",
            selected.DisplayName,
            selected.Type,
            selectedState,
            selectedState == CreatorCatalogSourceState.Failed
                ? " (select again to retry)"
                : string.Empty,
            selected.Category,
            selected.Source,
            selected.SourceScene,
            selected.SourcePath,
            selected.TransformCapabilities,
            DescribeAction(preview),
            DescribeAction(place));
        string diagnosticSummary = DiagnosticSummary();
        return string.IsNullOrEmpty(diagnosticSummary)
            ? detailsText
            : detailsText + "\n" + diagnosticSummary;
    }

    private string RegistryFailureReason()
    {
        return FirstDiagnosticMessage();
    }

    private string FirstDiagnosticMessage()
    {
        foreach (CreatorInventoryDiagnostic diagnostic in _inventory.Diagnostics)
        {
            if (diagnostic.Severity == ApplySeverity.Error)
                return diagnostic.Message;
        }

        if (_inventory.Diagnostics.Count > 0)
            return _inventory.Diagnostics[0].Message;

        return string.IsNullOrEmpty(_inventory.Message)
            ? "Level Framework creator registry is unavailable."
            : _inventory.Message;
    }

    private string DiagnosticSummary()
    {
        if (_inventory.Diagnostics.Count == 0)
            return string.Empty;

        return string.Format(
            "Diagnostics: {0} | {1}",
            _inventory.Diagnostics.Count,
            FirstDiagnosticMessage());
    }

    private static string DescribeAction(CreatorCatalogActionAvailability availability)
    {
        return availability.Available
            ? "available"
            : "disabled - " + availability.UnavailableReason;
    }

    private void RequestSelectedSourceLoad(CreatorMetadata metadata)
    {
        CreatorCatalogSourceState state = GetSourceState(metadata.Type);
        _requestedSourceLoad =
            NeedsSourceLoad(state)
                ? metadata.Type
                : null;
    }

    private static bool NeedsSourceLoad(CreatorCatalogSourceState state)
    {
        return state == CreatorCatalogSourceState.NotRequested
            || state == CreatorCatalogSourceState.Failed;
    }

    private SourceState CreateInitialSourceState(CreatorMetadata metadata)
    {
        if (!metadata.HasLoader)
        {
            return new SourceState(
                CreatorCatalogSourceState.Unavailable,
                "Creator has no loader; Preview and Place are unavailable.");
        }

        if (!_invalidatedLoadedSourceTypes.Contains(metadata.Type)
            && _hasLoadedSource != null
            && _hasLoadedSource(metadata.Type))
        {
            return new SourceState(
                CreatorCatalogSourceState.Ready,
                string.Empty);
        }

        return new SourceState(
            CreatorCatalogSourceState.NotRequested,
            "Source has not been loaded yet.");
    }

    private static string NormalizeReason(
        CreatorMetadata metadata,
        CreatorCatalogSourceState state,
        string reason)
    {
        if (!string.IsNullOrEmpty(reason))
            return reason;

        switch (state)
        {
            case CreatorCatalogSourceState.Unavailable:
                return metadata.HasLoader
                    ? "Creator source is unavailable."
                    : "Creator has no loader; Preview and Place are unavailable.";
            case CreatorCatalogSourceState.NotRequested:
                return "Source has not been loaded yet.";
            case CreatorCatalogSourceState.Loading:
                return "Source is loading.";
            case CreatorCatalogSourceState.Ready:
                return string.Empty;
            case CreatorCatalogSourceState.Failed:
                return "Creator source failed to load.";
            default:
                return "Creator source is unavailable.";
        }
    }
}