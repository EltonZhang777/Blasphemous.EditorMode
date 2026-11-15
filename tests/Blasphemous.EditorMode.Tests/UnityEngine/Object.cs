using System.Runtime.CompilerServices;

namespace UnityEngine;

internal class Object
{
    internal bool Destroyed;

    public static bool operator ==(Object left, Object right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (ReferenceEquals(left, null))
            return right.Destroyed;
        if (ReferenceEquals(right, null))
            return left.Destroyed;
        return false;
    }

    public static bool operator !=(Object left, Object right)
    {
        return !(left == right);
    }

    public override bool Equals(object value)
    {
        return ReferenceEquals(this, value);
    }

    public override int GetHashCode()
    {
        return RuntimeHelpers.GetHashCode(this);
    }
}
