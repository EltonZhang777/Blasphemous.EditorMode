using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Framework;
using Blasphemous.EditorMode.Lifecycle;
using System;
using System.Collections.Generic;

namespace Blasphemous.EditorMode.Editing;

/// <summary>Result of reconciling one room's imported layout and runtime projection.</summary>
internal sealed class RoomOverlayReconcileResult
{
    internal RoomOverlayReconcileResult(
        RoomIdentity room,
        LevelEditSession session,
        RuntimePlacementManager manager,
        ApplyReport report)
    {
        Room = room;
        Session = session;
        Manager = manager;
        Report = report;
    }

    internal RoomIdentity Room { get; }
    internal LevelEditSession Session { get; }
    internal RuntimePlacementManager Manager { get; }
    internal ApplyReport Report { get; }
    internal bool Succeeded { get { return Session != null && Manager != null; } }
    internal string StatusMessage { get { return Report.ToSummary("Room reconciliation"); } }
}

/// <summary>Idempotently rebuilds the current Room Overlay after Framework reload.</summary>
internal sealed class RoomOverlayReconciler
{
    private readonly ContentCatalog _catalog;
    private readonly IRuntimePlacementFactory _factory;
    private RuntimePlacementManager _manager;
    private RoomIdentity _room;
    private LevelEditSession _session;

    internal RoomOverlayReconciler(ContentCatalog catalog, IRuntimePlacementFactory factory)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    internal RoomIdentity CurrentRoom { get { return _room; } }
    internal LevelEditSession CurrentSession { get { return _session; } }
    internal RuntimePlacementManager Manager { get { return _manager; } }

    internal RoomOverlayReconcileResult Apply(RoomIdentity room, FrameworkFileResult file)
    {
        ApplyReport report = new();
        LevelEditSessionImportResult imported = LevelEditSession.Import(room, file, _catalog.Entries);
        report.Merge(imported.Report);
        if (!imported.Succeeded)
            return new RoomOverlayReconcileResult(room, null, null, report);

        RuntimePlacementManager next = new(imported.Session, _catalog, _factory);
        IRuntimePlacementAdoptionPass adoptionPass = _factory as IRuntimePlacementAdoptionPass;
        adoptionPass?.BeginAdoptionPass();

        try
        {
            foreach (PlacementRecord placement in imported.Session.Placements)
            {
                if (next.TryProjectExisting(placement, report))
                    continue;

                next.TryClear(report);
                return new RoomOverlayReconcileResult(room, null, null, report);
            }
        }
        finally
        {
            adoptionPass?.EndAdoptionPass();
        }

        if (report.IsPartial)
            imported.Session.SetState(LevelEditSessionState.Partial, report);

        next.CaptureInitialSnapshot();

        if (_manager != null && !_manager.TryClear(report))
        {
            next.TryClear(report);
            return new RoomOverlayReconcileResult(room, null, null, report);
        }

        _manager = next;
        _room = room;
        _session = imported.Session;
        return new RoomOverlayReconcileResult(room, imported.Session, next, report);
    }

    internal bool TryClear(ApplyReport report)
    {
        if (_manager == null)
        {
            _room = null;
            _session = null;
            return true;
        }

        bool result = _manager.TryClear(report);
        if (result)
        {
            _manager = null;
            _room = null;
            _session = null;
        }

        return result;
    }
}

/// <summary>Compares observable placement state across a room round-trip.</summary>
internal static class RoomOverlayComparer
{
    internal static bool Matches(
        LevelEditSession expected,
        LevelEditSession actual,
        ApplyReport report)
    {
        if (expected == null || actual == null)
        {
            report.Error(ReportCodes.RoundTripInvalid, "$", "Both round-trip sessions are required.");
            return false;
        }
        if (!expected.Room.Equals(actual.Room))
        {
            report.Error(ReportCodes.RoundTripScope, "$", "Room Identity changed during round-trip.");
            return false;
        }
        if (expected.Placements.Count != actual.Placements.Count)
        {
            report.Error(ReportCodes.RoundTripCount, "$", "Placement count changed during round-trip.");
            return false;
        }

        Dictionary<string, PlacementRecord> actualById = new(StringComparer.OrdinalIgnoreCase);
        foreach (PlacementRecord placement in actual.Placements)
            actualById[placement.Id] = placement;

        foreach (PlacementRecord expectedPlacement in expected.Placements)
        {
            if (!actualById.TryGetValue(expectedPlacement.Id, out PlacementRecord actualPlacement))
            {
                report.Error(ReportCodes.RoundTripId, expectedPlacement.Id, "Placement identity was not preserved.");
                return false;
            }
            if (!string.Equals(expectedPlacement.Type, actualPlacement.Type, StringComparison.Ordinal)
                || !expectedPlacement.Transform.Equals(actualPlacement.Transform)
                || expectedPlacement.Ownership != actualPlacement.Ownership
                || expectedPlacement.IsLocked != actualPlacement.IsLocked
                || !string.Equals(expectedPlacement.Condition, actualPlacement.Condition, StringComparison.Ordinal)
                || !SameProperties(expectedPlacement.Properties, actualPlacement.Properties))
            {
                report.Error(ReportCodes.RoundTripState, expectedPlacement.Id, "Placement transform, properties, ownership, lock, or condition changed.");
                return false;
            }
        }

        report.Info(ReportCodes.RoundTripMatch, expected.Room.ExportFileKey, "Observable Room Overlay state matches.");
        return true;
    }

    private static bool SameProperties(
        IEnumerable<string> expected,
        IEnumerable<string> actual)
    {
        List<string> left = [.. expected];
        List<string> right = [.. actual];
        if (left.Count != right.Count)
            return false;
        for (int index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}