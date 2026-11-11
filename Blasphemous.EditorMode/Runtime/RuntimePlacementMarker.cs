using UnityEngine;

namespace Blasphemous.EditorMode.Runtime;

/// <summary>Runtime marker used to distinguish overlay additions from original objects.</summary>
internal sealed class RuntimePlacementMarker : MonoBehaviour
{
    internal string PlacementId { get; set; }
    internal string Type { get; set; }
    internal string Source { get; set; }
}
