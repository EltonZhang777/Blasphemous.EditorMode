using Blasphemous.EditorMode.Framework;
using System;

namespace Blasphemous.EditorMode.Editing;

/// <summary>Writes a canonical Level Export, then refreshes the next-preload Framework snapshot.</summary>
internal sealed class LevelExportWorkflow
{
    private readonly LevelEditSession _session;
    private readonly LevelFrameworkSnapshotReloader _reloader;

    internal LevelExportWorkflow(
        LevelEditSession session,
        LevelFrameworkSnapshotReloader reloader)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _reloader = reloader ?? throw new ArgumentNullException(nameof(reloader));
    }

    internal static LevelExportWorkflow CreateRuntime(LevelEditSession session)
    {
        return new LevelExportWorkflow(
            session,
            LevelFrameworkSnapshotReloader.CreateRuntime());
    }

    internal LevelEditSessionExportResult Export(string path)
    {
        LevelEditSessionExportResult result = _session.Export(path);
        if (!result.Succeeded)
            return result;

        result.AttachFrameworkReload(
            _reloader.Reload(_session.Room.ExportFileKey, result.Document));
        return result;
    }
}