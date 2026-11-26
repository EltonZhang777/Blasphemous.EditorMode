using System;

namespace Blasphemous.EditorMode.Components;

internal static class PropertiesHorizontalScroll
{
    internal const float EdgePadding = 2f;

    internal static float FollowCaret(
        float caretX,
        float currentOffset,
        float viewportWidth,
        float contentWidth)
    {
        if (viewportWidth <= 0f)
            return currentOffset;

        float offset = currentOffset;
        float caretPositionInView = caretX + offset;
        float rightEdge = viewportWidth - EdgePadding;
        if (caretPositionInView > rightEdge)
            offset -= caretPositionInView - rightEdge;
        else if (caretPositionInView < EdgePadding)
            offset += EdgePadding - caretPositionInView;

        float minimumOffset = Math.Min(0f, viewportWidth - contentWidth);
        return Math.Min(0f, Math.Max(minimumOffset, offset));
    }
}