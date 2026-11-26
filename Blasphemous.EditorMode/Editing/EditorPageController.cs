using Blasphemous.EditorMode.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blasphemous.EditorMode.Editing;

/// <summary>Top-level editor page that owns a distinct selection and command domain.</summary>
internal enum EditorPage
{
    Additions,
    OriginalObjects
}

/// <summary>Routes page-specific candidate lists, selections, and history commands.</summary>
internal sealed class EditorPageController
{
    private readonly RuntimePlacementManager _additions;
    private readonly OriginalObjectPage _originalObjects;

    internal EditorPageController(RuntimePlacementManager additions)
        : this(additions, OriginalObjectPage.Empty)
    {
    }

    internal EditorPageController(
        RuntimePlacementManager additions,
        OriginalObjectPage originalObjects)
    {
        _additions = additions ?? throw new ArgumentNullException(nameof(additions));
        _originalObjects = originalObjects ?? throw new ArgumentNullException(nameof(originalObjects));
        ActivePage = EditorPage.Additions;
        _originalObjects.SetActive(false);
    }

    internal EditorPage ActivePage { get; private set; }
    internal RuntimePlacementManager Additions { get { return _additions; } }
    internal OriginalObjectPage OriginalObjects { get { return _originalObjects; } }
    internal ReadOnlyCollection<PlacementCandidate> AdditionCandidates { get { return _additions.Candidates; } }
    internal int OriginalCatalogCount { get { return _originalObjects.CatalogCount; } }
    internal int OriginalVisibleCount { get { return _originalObjects.VisibleCount; } }
    internal ReadOnlyCollection<OriginalObjectCandidate> OriginalCandidates
    {
        get { return _originalObjects.Candidates; }
    }

    internal bool TryPresentAdditionCandidates(
        IEnumerable<IRuntimePlacementHandle> hits,
        ApplyReport report)
    {
        return ActivePage == EditorPage.Additions
            && _additions.TryPresentCandidates(hits, report);
    }

    internal bool TryChooseAdditionCandidate(
        string identity,
        ApplyReport report,
        out PlacementRecord selected)
    {
        selected = null;
        return ActivePage == EditorPage.Additions
            && _additions.TryChooseCandidate(identity, report, out selected);
    }

    internal bool TrySelectOriginal(
        string identity,
        ApplyReport report,
        out OriginalObjectCatalogEntry selected)
    {
        selected = null;
        return ActivePage == EditorPage.OriginalObjects
            && _originalObjects.TrySelect(identity, report, out selected);
    }

    internal bool TryPresentOriginalCandidates(
        IOriginalObjectHitSource hitSource,
        ApplyReport report)
    {
        return ActivePage == EditorPage.OriginalObjects
            && _originalObjects.TryPresentCandidates(hitSource, report);
    }

    internal bool TryChooseOriginalCandidate(
        string identity,
        ApplyReport report,
        out OriginalObjectCatalogEntry selected)
    {
        selected = null;
        return ActivePage == EditorPage.OriginalObjects
            && _originalObjects.TryChooseCandidate(identity, report, out selected);
    }

    internal bool TrySwitchPage(ApplyReport report)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));

        ActivePage = ActivePage == EditorPage.Additions
            ? EditorPage.OriginalObjects
            : EditorPage.Additions;
        _originalObjects.SetActive(ActivePage == EditorPage.OriginalObjects);
        report.Info(
            ReportCodes.PageChanged,
            ActivePage.ToString(),
            "Active editor page: " + ActivePage + ".");
        return true;
    }

    internal bool TryUndo(ApplyReport report)
    {
        return ActivePage == EditorPage.Additions
            ? _additions.TryUndo(report)
            : _originalObjects.TryUndo(report);
    }

    internal bool TryRedo(ApplyReport report)
    {
        return ActivePage == EditorPage.Additions
            ? _additions.TryRedo(report)
            : _originalObjects.TryRedo(report);
    }

    internal bool TryReset(ApplyReport report)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));

        OriginalObjectPage.OriginalObjectResetPreparation preparation = null;
        if (_originalObjects.HasSession
            && !_originalObjects.TryPrepareReset(report, out preparation))
            return false;

        if (!_additions.TryReset(report))
        {
            if (preparation != null)
                preparation.Rollback(report);

            return false;
        }

        if (preparation != null)
            preparation.Commit(report);

        return true;
    }

    internal bool TryApplyOriginalModification(
        PlacementTransform transform,
        ApplyReport report)
    {
        return ActivePage == EditorPage.OriginalObjects
            && _originalObjects.TryApplySelectedModification(transform, report);
    }

    internal bool TryApplyOriginalProperties(
        string rawText,
        ApplyReport report)
    {
        return ActivePage == EditorPage.OriginalObjects
            && _originalObjects.TryApplySelectedProperties(rawText, report);
    }

    internal bool TryDeleteOriginal(ApplyReport report)
    {
        return ActivePage == EditorPage.OriginalObjects
            && _originalObjects.TryDeleteSelected(report);
    }

    internal bool TryRemoveOriginalEdit(ApplyReport report)
    {
        return ActivePage == EditorPage.OriginalObjects
            && _originalObjects.TryRemoveSelectedEdit(report);
    }
}