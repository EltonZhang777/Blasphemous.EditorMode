using System.Collections.Generic;

namespace Blasphemous.EditorMode.Framework;

/// <summary>Canonical normalization rules for opaque Level Framework property strings.</summary>
internal static class LevelFrameworkProperties
{
    internal static List<string> Normalize(IEnumerable<string> properties)
    {
        List<string> normalized = [];
        foreach (string property in properties ?? [])
        {
            if (property == null)
                continue;

            string value = property.Trim();
            if (value.Length > 0)
                normalized.Add(value);
        }

        return normalized;
    }
}