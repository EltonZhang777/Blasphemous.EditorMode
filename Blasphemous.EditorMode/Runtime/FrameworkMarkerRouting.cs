using System;
using System.Collections.Generic;

namespace Blasphemous.EditorMode.Runtime;

internal static class FrameworkMarkerRouting
{
    internal static Dictionary<string, TValue> CreateCreatorTypeBuckets<TValue>()
    {
        return new Dictionary<string, TValue>(StringComparer.Ordinal);
    }

    internal static bool TryGetCreatorTypeBuckets<TValue>(
        Dictionary<string, Dictionary<string, Queue<TValue>>> lookup,
        string placementId,
        string creatorType,
        out Queue<TValue> candidates)
    {
        candidates = null;
        return lookup != null
            && lookup.TryGetValue(placementId, out Dictionary<string, Queue<TValue>> byType)
            && byType.TryGetValue(creatorType, out candidates);
    }

    internal static bool AreExactCreatorTypes(string left, string right)
    {
        return string.Equals(left, right, StringComparison.Ordinal);
    }
}