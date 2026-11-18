using BepInEx.Bootstrap;
using Blasphemous.EditorMode;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Blasphemous.EditorMode.Diagnostics;

internal static class RuntimeDependencyProbe
{
    internal static IEnumerable<InstalledDependency> ReadInstalled()
    {
        return [.. Chainloader.PluginInfos.Values.Select(info => new InstalledDependency(info.Metadata.GUID, info.Metadata.Version))];
    }

    internal static string DescribeTargetProfile()
    {
        Dictionary<string, InstalledDependency> installed = ReadInstalled()
            .ToDictionary(value => value.Id, StringComparer.OrdinalIgnoreCase);
        return string.Format(
            "Editor Mode target profile: Unity {0} (target {1}); GameLibs package {2}; ModdingAPI {3}; Level Framework {4}; UI Framework {5}.",
            Application.unityVersion,
            TargetDependencies.UnityProfile,
            TargetDependencies.GameLibsProfile,
            VersionOf(installed, TargetDependencies.ModdingApiId),
            VersionOf(installed, TargetDependencies.LevelFrameworkId),
            VersionOf(installed, TargetDependencies.UiFrameworkId));
    }

    private static string VersionOf(
        IDictionary<string, InstalledDependency> installed,
        string id)
    {
        return installed.TryGetValue(id, out InstalledDependency dependency)
            ? dependency.Version.ToString()
            : "<missing>";
    }
}
