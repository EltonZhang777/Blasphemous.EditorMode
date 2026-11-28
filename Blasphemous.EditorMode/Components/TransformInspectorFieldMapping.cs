using System;

namespace Blasphemous.EditorMode.Components;

internal static class TransformInspectorFieldMapping
{
    internal static TransformInspectorValues ReadValues(
        Func<int, string> readPosition,
        Func<int, string> readRotation,
        Func<int, string> readScale)
    {
        if (readPosition == null)
        {
            throw new ArgumentNullException(nameof(readPosition));
        }
        if (readRotation == null)
        {
            throw new ArgumentNullException(nameof(readRotation));
        }
        if (readScale == null)
        {
            throw new ArgumentNullException(nameof(readScale));
        }

        return new TransformInspectorValues(
            ReadGroup(readPosition),
            ReadGroup(readRotation),
            ReadGroup(readScale));
    }

    internal static void WriteValues(
        TransformInspectorValues values,
        Action<int, string> writePosition,
        Action<int, string> writeRotation,
        Action<int, string> writeScale)
    {
        if (values == null)
        {
            throw new ArgumentNullException(nameof(values));
        }
        if (writePosition == null)
        {
            throw new ArgumentNullException(nameof(writePosition));
        }
        if (writeRotation == null)
        {
            throw new ArgumentNullException(nameof(writeRotation));
        }
        if (writeScale == null)
        {
            throw new ArgumentNullException(nameof(writeScale));
        }

        WriteGroup(values.Position, writePosition);
        WriteGroup(values.Rotation, writeRotation);
        WriteGroup(values.Scale, writeScale);
    }

    private static TransformInspectorGroup ReadGroup(Func<int, string> readField)
    {
        return new TransformInspectorGroup(
            readField(0),
            readField(1),
            readField(2));
    }

    private static void WriteGroup(
        TransformInspectorGroup values,
        Action<int, string> writeField)
    {
        writeField(0, values.X);
        writeField(1, values.Y);
        writeField(2, values.Z);
    }
}