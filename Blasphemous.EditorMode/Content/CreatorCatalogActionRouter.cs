using Blasphemous.EditorMode.Diagnostics;
using System;

namespace Blasphemous.EditorMode.Content;

/// <summary>Coordinates catalog selection, Preview ownership, and Place actions.</summary>
internal sealed class CreatorCatalogActionRouter
{
    private readonly CreatorCatalogController _controller;
    private readonly ContentCatalog _catalog;
    private readonly Func<string, bool> _placeSelected;
    private PreviewInstance _preview;

    internal CreatorCatalogActionRouter(
        CreatorCatalogController controller,
        ContentCatalog catalog,
        Func<string, bool> placeSelected)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _placeSelected = placeSelected ?? throw new ArgumentNullException(nameof(placeSelected));
    }

    internal PreviewInstance Preview { get { return _preview; } }

    internal string PreviewStatus
    {
        get
        {
            return _preview == null
                ? "none"
                : _preview.Metadata.DisplayName + " (follows cursor)";
        }
    }

    internal bool TrySelect(string type, out ApplyReport report)
    {
        report = null;
        CreatorMetadata previous = _controller.SelectedEntry;
        if (!_controller.TrySelect(type))
            return false;

        CreatorMetadata current = _controller.SelectedEntry;
        if (previous != null
            && string.Equals(previous.Type, current.Type, StringComparison.Ordinal))
            return true;

        Exception error = ClearPreview();
        if (error == null)
            return true;

        report = new ApplyReport();
        report.Error(
            ReportCodes.PreviewFailed,
            previous == null ? type ?? "$" : previous.Type,
            "Changing the selected creator could not discard the previous Preview: "
                + error.Message);
        return true;
    }

    internal bool TryPreviewSelected(out ApplyReport report)
    {
        report = null;
        if (!_controller.PreviewAvailable)
        {
            report = Unavailable(
                ReportCodes.PreviewFailed,
                "preview",
                _controller.PreviewUnavailableReason);
            return false;
        }

        Exception error = ClearPreview();
        if (error != null)
        {
            report = new ApplyReport();
            report.Error(
                ReportCodes.PreviewFailed,
                _controller.SelectedType,
                "The previous Preview could not be discarded: " + error.Message);
            return false;
        }

        if (!_catalog.TryCreatePreview(
            _controller.SelectedType,
            out PreviewInstance preview,
            out string previewError))
        {
            report = new ApplyReport();
            report.Error(
                ReportCodes.PreviewFailed,
                _controller.SelectedType ?? "$",
                previewError);
            return false;
        }

        _preview = preview;
        report = new ApplyReport();
        report.Info(
            ReportCodes.PreviewCreated,
            _controller.SelectedType,
            "Preview follows the pointer through the level view; Clear removes it. It is not part of the Room Overlay.");
        return true;
    }

    internal bool TryPlaceSelected(out ApplyReport report)
    {
        report = null;
        if (!_controller.PlaceAvailable)
        {
            report = Unavailable(
                ReportCodes.PlaceNotReady,
                "place",
                _controller.PlaceUnavailableReason);
            return false;
        }

        return _placeSelected(_controller.SelectedType);
    }

    internal void FollowPreview()
    {
        if (_preview != null)
            _preview.FollowCursor();
    }

    internal Exception ClearPreview()
    {
        PreviewInstance preview = _preview;
        _preview = null;
        if (preview == null)
            return null;

        try
        {
            preview.Dispose();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static ApplyReport Unavailable(string code, string location, string reason)
    {
        ApplyReport report = new();
        report.Warning(
            code,
            location,
            string.IsNullOrEmpty(reason)
                ? "The selected creator is not available for this action."
                : reason);
        return report;
    }
}