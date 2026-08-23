using BepInEx.Bootstrap;
using System.Collections.Generic;
using System.Linq;

namespace Blasphemous.EditorMode.Diagnostics;

internal static class RuntimeDependencyProbe
{
    internal static IEnumerable<InstalledDependency> ReadInstalled()
    {
        return Chainloader.PluginInfos.Values
            .Select(info => new InstalledDependency(info.Metadata.GUID, info.Metadata.Version))
            .ToList();
    }
}
