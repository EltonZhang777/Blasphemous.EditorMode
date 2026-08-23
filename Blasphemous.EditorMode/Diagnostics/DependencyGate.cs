using System;
using System.Collections.Generic;
using System.Linq;

namespace Blasphemous.EditorMode.Diagnostics;

/// <summary>Explains why a required runtime dependency failed the gate.</summary>
public enum DependencyFailureReason
{
    /// <summary>The dependency was not reported by the runtime.</summary>
    Missing,

    /// <summary>The dependency was found, but its version is below the minimum.</summary>
    VersionTooLow
}

/// <summary>Describes a dependency and the minimum version accepted by Editor Mode.</summary>
public sealed class DependencyRequirement
{
    /// <summary>Creates a dependency requirement.</summary>
    public DependencyRequirement(string id, Version minimumVersion)
    {
        if (string.IsNullOrEmpty(id) || id.Trim().Length == 0)
        {
            throw new ArgumentException("Dependency id is required.", nameof(id));
        }

        Id = id;
        MinimumVersion = minimumVersion ?? throw new ArgumentNullException(nameof(minimumVersion));
    }

    /// <summary>Gets the dependency identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the minimum compatible version.</summary>
    public Version MinimumVersion { get; }
}

/// <summary>Describes a dependency discovered in the running game.</summary>
public sealed class InstalledDependency
{
    /// <summary>Creates an installed dependency entry.</summary>
    public InstalledDependency(string id, Version version)
    {
        if (string.IsNullOrEmpty(id) || id.Trim().Length == 0)
        {
            throw new ArgumentException("Dependency id is required.", nameof(id));
        }

        Id = id;
        Version = version ?? throw new ArgumentNullException(nameof(version));
    }

    /// <summary>Gets the dependency identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the discovered dependency version.</summary>
    public Version Version { get; }
}

/// <summary>Reports one missing or incompatible dependency.</summary>
public sealed class DependencyDiagnostic
{
    internal DependencyDiagnostic(
        string id,
        DependencyFailureReason reason,
        Version requiredVersion,
        Version actualVersion,
        string message)
    {
        Id = id;
        Reason = reason;
        RequiredVersion = requiredVersion;
        ActualVersion = actualVersion;
        Message = message;
    }

    /// <summary>Gets the dependency identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the reason the dependency failed.</summary>
    public DependencyFailureReason Reason { get; }

    /// <summary>Gets the minimum required version.</summary>
    public Version RequiredVersion { get; }

    /// <summary>Gets the installed version, or <see langword="null"/> when missing.</summary>
    public Version ActualVersion { get; }

    /// <summary>Gets an actionable diagnostic message.</summary>
    public string Message { get; }
}

/// <summary>Contains the result and diagnostics produced by the dependency gate.</summary>
public sealed class DependencyGateResult
{
    internal DependencyGateResult(bool isCompatible, IEnumerable<DependencyDiagnostic> diagnostics, string statusMessage)
    {
        IsCompatible = isCompatible;
        Diagnostics = new List<DependencyDiagnostic>(diagnostics).AsReadOnly();
        StatusMessage = statusMessage;
    }

    /// <summary>Gets whether all required dependencies are compatible.</summary>
    public bool IsCompatible { get; }

    /// <summary>Gets the failures found during evaluation.</summary>
    public IEnumerable<DependencyDiagnostic> Diagnostics { get; }

    /// <summary>Gets the human-readable status message.</summary>
    public string StatusMessage { get; }
}

/// <summary>Evaluates the runtime dependency profile before Editor Mode initializes.</summary>
public static class DependencyGate
{
    /// <summary>Evaluates required dependencies against the installed runtime profile.</summary>
    public static DependencyGateResult Evaluate(
        IEnumerable<DependencyRequirement> requirements,
        IEnumerable<InstalledDependency> installedDependencies)
    {
        if (requirements == null)
        {
            throw new ArgumentNullException(nameof(requirements));
        }

        var installedById = new Dictionary<string, InstalledDependency>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in installedDependencies ?? Enumerable.Empty<InstalledDependency>())
        {
            InstalledDependency existing;
            if (!installedById.TryGetValue(dependency.Id, out existing)
                || dependency.Version.CompareTo(existing.Version) > 0)
            {
                installedById[dependency.Id] = dependency;
            }
        }

        var diagnostics = new List<DependencyDiagnostic>();
        var report = new List<string>();
        foreach (var requirement in requirements)
        {
            InstalledDependency installed;
            if (!installedById.TryGetValue(requirement.Id, out installed))
            {
                diagnostics.Add(new DependencyDiagnostic(
                    requirement.Id,
                    DependencyFailureReason.Missing,
                    requirement.MinimumVersion,
                    null,
                    string.Format(
                        "Missing dependency '{0}'. Install version {1} or newer.",
                        requirement.Id,
                        requirement.MinimumVersion)));
                report.Add(string.Format("{0}: missing (required >= {1})", requirement.Id, requirement.MinimumVersion));
                continue;
            }

            report.Add(string.Format(
                "{0}: {1} (required >= {2})",
                requirement.Id,
                installed.Version,
                requirement.MinimumVersion));

            if (installed.Version.CompareTo(requirement.MinimumVersion) < 0)
            {
                diagnostics.Add(new DependencyDiagnostic(
                    requirement.Id,
                    DependencyFailureReason.VersionTooLow,
                    requirement.MinimumVersion,
                    installed.Version,
                    string.Format(
                        "Dependency '{0}' is too old: found {1}, require {2} or newer.",
                        requirement.Id,
                        installed.Version,
                        requirement.MinimumVersion)));
            }
        }

        var isCompatible = diagnostics.Count == 0;
        var prefix = isCompatible ? "Editor Mode dependency gate passed" : "Editor Mode disabled";
        return new DependencyGateResult(
            isCompatible,
            diagnostics,
            string.Format("{0}: {1}", prefix, string.Join("; ", report.ToArray())));
    }
}
