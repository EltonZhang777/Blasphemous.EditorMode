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

/// <summary>Placeholder identity for the Original Objects page before its catalog is attached.</summary>
internal sealed class OriginalObjectCandidate
{
    internal OriginalObjectCandidate(string scene, string path)
    {
        Scene = scene;
        Path = path;
    }

    internal string Scene { get; }
    internal string Path { get; }
}

/// <summary>Routes page-specific candidate lists, selections, and history commands.</summary>
internal sealed class EditorPageController
{
    private readonly RuntimePlacementManager _additions;

    internal EditorPageController(RuntimePlacementManager additions)
    {
        _additions = additions ?? throw new ArgumentNullException(nameof(additions));
        ActivePage = EditorPage.Additions;
    }

    internal EditorPage ActivePage { get; private set; }
    internal RuntimePlacementManager Additions { get { return _additions; } }
    internal ReadOnlyCollection<PlacementCandidate> AdditionCandidates { get { return _additions.Candidates; } }
    internal ReadOnlyCollection<OriginalObjectCandidate> OriginalCandidates
    {
        get { return new ReadOnlyCollection<OriginalObjectCandidate>([]); }
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

    internal bool TrySwitchPage(ApplyReport report)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));

        ActivePage = ActivePage == EditorPage.Additions
            ? EditorPage.OriginalObjects
            : EditorPage.Additions;
        report.Info(
            ReportCodes.PageChanged,
            ActivePage.ToString(),
            "Active editor page: " + ActivePage + ".");
        return true;
    }

    internal bool TryUndo(ApplyReport report)
    {
        return ActivePage == EditorPage.Additions && _additions.TryUndo(report);
    }

    internal bool TryRedo(ApplyReport report)
    {
        return ActivePage == EditorPage.Additions && _additions.TryRedo(report);
    }
}