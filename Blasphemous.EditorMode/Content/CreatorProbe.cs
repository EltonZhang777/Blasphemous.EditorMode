using System;
using System.Collections.Generic;

namespace Blasphemous.EditorMode.Content;

/// <summary>Reason a catalog creator was not available.</summary>
internal enum CreatorProbeFailure
{
    Missing,
    Unsupported,
    RegistryUnavailable
}

/// <summary>One nonfatal creator availability diagnostic.</summary>
internal sealed class CreatorProbeDiagnostic
{
    internal CreatorProbeDiagnostic(string type, CreatorProbeFailure failure, string message)
    {
        Type = type;
        Failure = failure;
        Message = message;
    }

    internal string Type { get; }
    internal CreatorProbeFailure Failure { get; }
    internal string Message { get; }
}

/// <summary>Result of checking curated metadata against registered Framework creators.</summary>
internal sealed class CreatorProbeResult
{
    internal CreatorProbeResult(
        IEnumerable<CreatorMetadata> available,
        IEnumerable<CreatorProbeDiagnostic> diagnostics,
        string message)
    {
        Available = new List<CreatorMetadata>(available).AsReadOnly();
        Diagnostics = new List<CreatorProbeDiagnostic>(diagnostics).AsReadOnly();
        Message = message;
    }

    internal IList<CreatorMetadata> Available { get; }
    internal IList<CreatorProbeDiagnostic> Diagnostics { get; }
    internal bool IsReady { get { return Diagnostics.Count == 0; } }
    internal string Message { get; }

    internal static CreatorProbeResult RegistryUnavailable(string message)
    {
        return new CreatorProbeResult(
            new CreatorMetadata[0],
            new[] { new CreatorProbeDiagnostic("<registry>", CreatorProbeFailure.RegistryUnavailable, message) },
            message);
    }
}

/// <summary>Pure creator metadata check, independent of Unity scene state.</summary>
internal static class CreatorProbe
{
    internal static CreatorProbeResult Evaluate(
        IEnumerable<CreatorMetadata> catalog,
        IEnumerable<string> registeredTypes)
    {
        if (catalog == null)
            throw new ArgumentNullException(nameof(catalog));

        var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string type in registeredTypes ?? new string[0])
        {
            if (!string.IsNullOrEmpty(type) && type.Trim().Length > 0)
                registered.Add(type);
        }

        var available = new List<CreatorMetadata>();
        var diagnostics = new List<CreatorProbeDiagnostic>();
        foreach (CreatorMetadata metadata in catalog)
        {
            if (metadata == null)
            {
                diagnostics.Add(new CreatorProbeDiagnostic(
                    "<unknown>",
                    CreatorProbeFailure.Unsupported,
                    "Creator metadata is unsupported and was skipped."));
                continue;
            }

            if (!metadata.IsSupported)
            {
                diagnostics.Add(new CreatorProbeDiagnostic(
                    metadata.Type,
                    CreatorProbeFailure.Unsupported,
                    string.Format("Creator '{0}' is unsupported and was skipped.", metadata.Type)));
                continue;
            }

            if (!registered.Contains(metadata.Type))
            {
                diagnostics.Add(new CreatorProbeDiagnostic(
                    metadata.Type,
                    CreatorProbeFailure.Missing,
                    string.Format(
                        "Creator '{0}' is not registered by the target Level Framework profile.",
                        metadata.Type)));
                continue;
            }

            available.Add(metadata);
        }

        string message = string.Format(
            "Creator probe found {0} available catalog creator(s) and {1} diagnostic(s).",
            available.Count,
            diagnostics.Count);
        return new CreatorProbeResult(available, diagnostics, message);
    }
}
