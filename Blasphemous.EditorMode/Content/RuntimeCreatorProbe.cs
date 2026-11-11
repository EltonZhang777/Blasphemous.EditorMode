using Blasphemous.Framework.Levels;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Blasphemous.EditorMode.Content;

/// <summary>Reads the base Framework's internal registry without using its internal API.</summary>
internal static class RuntimeCreatorProbe
{
    private const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;

    internal static CreatorProbeResult Inspect(IEnumerable<CreatorMetadata> catalog)
    {
        try
        {
            PropertyInfo creatorsProperty = typeof(LevelRegister).GetProperty("Creators", StaticNonPublic);
            if (creatorsProperty == null)
            {
                return CreatorProbeResult.RegistryUnavailable(
                    "Level Framework creator registry is unavailable; catalog entries were not activated.");
            }

            object creators = creatorsProperty.GetValue(null, null);
            List<string> registeredTypes = [];
            foreach (object entry in (IEnumerable)creators)
            {
                PropertyInfo keyProperty = entry.GetType().GetProperty("Key");
                string type = keyProperty == null ? null : keyProperty.GetValue(entry, null) as string;
                if (!string.IsNullOrEmpty(type) && type.Trim().Length > 0)
                    registeredTypes.Add(type);
            }

            return CreatorProbe.Evaluate(catalog, registeredTypes);
        }
        catch (Exception exception)
        {
            return CreatorProbeResult.RegistryUnavailable(
                string.Format(
                    "Level Framework creator probe failed safely: {0}",
                    exception.Message));
        }
    }
}