using Blasphemous.EditorMode.Framework;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blasphemous.EditorMode.Editing;

/// <summary>Ownership boundary enforced by editor operations.</summary>
internal enum PlacementOwnership
{
    ArchitectOwned,
    Original
}

/// <summary>Serializable transform state for one Placement.</summary>
internal sealed class PlacementTransform : IEquatable<PlacementTransform>
{
    internal PlacementTransform(
        float positionX,
        float positionY,
        float positionZ,
        float rotationX,
        float rotationY,
        float rotationZ,
        float scaleX,
        float scaleY,
        float scaleZ)
    {
        PositionX = positionX;
        PositionY = positionY;
        PositionZ = positionZ;
        RotationX = rotationX;
        RotationY = rotationY;
        RotationZ = rotationZ;
        ScaleX = scaleX;
        ScaleY = scaleY;
        ScaleZ = scaleZ;
    }

    internal float PositionX { get; }
    internal float PositionY { get; }
    internal float PositionZ { get; }
    internal float RotationX { get; }
    internal float RotationY { get; }
    internal float RotationZ { get; }
    internal float ScaleX { get; }
    internal float ScaleY { get; }
    internal float ScaleZ { get; }

    internal static PlacementTransform Default
    {
        get { return new PlacementTransform(0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f); }
    }

    internal static PlacementTransform FromFramework(FrameworkObjectData data)
    {
        return new PlacementTransform(
            data.Position.X,
            data.Position.Y,
            data.Position.Z,
            data.Rotation.X,
            data.Rotation.Y,
            data.Rotation.Z,
            data.Scale.X,
            data.Scale.Y,
            data.Scale.Z);
    }

    internal FrameworkVector Position
    {
        get { return new FrameworkVector(PositionX, PositionY, PositionZ); }
    }

    internal FrameworkVector Rotation
    {
        get { return new FrameworkVector(RotationX, RotationY, RotationZ); }
    }

    internal FrameworkVector Scale
    {
        get { return new FrameworkVector(ScaleX, ScaleY, ScaleZ); }
    }

    public bool Equals(PlacementTransform other)
    {
        return other != null
            && PositionX == other.PositionX
            && PositionY == other.PositionY
            && PositionZ == other.PositionZ
            && RotationX == other.RotationX
            && RotationY == other.RotationY
            && RotationZ == other.RotationZ
            && ScaleX == other.ScaleX
            && ScaleY == other.ScaleY
            && ScaleZ == other.ScaleZ;
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as PlacementTransform);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = PositionX.GetHashCode();
            hash = (hash * 397) ^ PositionY.GetHashCode();
            hash = (hash * 397) ^ PositionZ.GetHashCode();
            hash = (hash * 397) ^ RotationX.GetHashCode();
            hash = (hash * 397) ^ RotationY.GetHashCode();
            hash = (hash * 397) ^ RotationZ.GetHashCode();
            hash = (hash * 397) ^ ScaleX.GetHashCode();
            hash = (hash * 397) ^ ScaleY.GetHashCode();
            return (hash * 397) ^ ScaleZ.GetHashCode();
        }
    }
}

/// <summary>Domain record for an editable Placement.</summary>
internal sealed class PlacementRecord
{
    internal PlacementRecord(
        string id,
        string type,
        PlacementTransform transform,
        IEnumerable<string> properties,
        PlacementOwnership ownership,
        bool locked = false,
        string condition = "")
    {
        if (string.IsNullOrEmpty(id) || id.Trim().Length == 0)
            throw new ArgumentException("Placement id is required.", nameof(id));
        if (string.IsNullOrEmpty(type) || type.Trim().Length == 0)
            throw new ArgumentException("Placement type is required.", nameof(type));

        Id = id;
        Type = type;
        Transform = transform ?? PlacementTransform.Default;
        Properties = new ReadOnlyCollection<string>(
            [.. properties ?? []]);
        Ownership = ownership;
        IsLocked = locked;
        Condition = condition ?? string.Empty;
    }

    internal string Id { get; }
    internal string Type { get; }
    internal PlacementTransform Transform { get; }
    internal ReadOnlyCollection<string> Properties { get; }
    internal PlacementOwnership Ownership { get; }
    internal bool IsLocked { get; }
    internal string Condition { get; }

    internal static PlacementRecord FromFramework(FrameworkObjectData data)
    {
        return new PlacementRecord(
            data.Id,
            data.Type,
            PlacementTransform.FromFramework(data),
            data.Properties,
            PlacementOwnership.ArchitectOwned,
            false,
            data.Condition);
    }

    internal PlacementRecord WithTransform(PlacementTransform transform)
    {
        return new PlacementRecord(Id, Type, transform, Properties, Ownership, IsLocked, Condition);
    }

    internal PlacementRecord WithProperties(IEnumerable<string> properties)
    {
        return new PlacementRecord(Id, Type, Transform, properties, Ownership, IsLocked, Condition);
    }

    internal PlacementRecord WithLocked(bool locked)
    {
        return new PlacementRecord(Id, Type, Transform, Properties, Ownership, locked, Condition);
    }

    internal PlacementRecord CopyWithId(string id)
    {
        return new PlacementRecord(id, Type, Transform, Properties, Ownership, false, Condition);
    }

    internal FrameworkObjectData ToFrameworkData()
    {
        return new FrameworkObjectData(
            null,
            null,
            Type,
            Id,
            Transform.Position,
            Transform.Rotation,
            Transform.Scale,
            Condition,
            Properties);
    }
}