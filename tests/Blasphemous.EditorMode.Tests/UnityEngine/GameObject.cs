namespace UnityEngine;

internal sealed class GameObject : Object
{
    internal bool Active { get; set; } = true;

    internal void SetActive(bool active)
    {
        Active = active;
    }
}
