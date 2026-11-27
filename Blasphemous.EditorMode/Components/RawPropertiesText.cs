using System.Collections.Generic;
using System.Text;

namespace Blasphemous.EditorMode.Components;

/// <summary>One-property-per-line representation of committed raw properties.</summary>
internal sealed class RawPropertiesText
{
    internal string Text { get; }
    internal bool ContainsLineBreak { get; }

    private RawPropertiesText(string text, bool containsLineBreak)
    {
        Text = text;
        ContainsLineBreak = containsLineBreak;
    }

    internal static RawPropertiesText Create(IEnumerable<string> properties)
    {
        StringBuilder text = new();
        bool containsLineBreak = false;
        int index = 0;
        foreach (string property in properties ?? [])
        {
            if (index++ > 0)
            {
                text.Append('\n');
            }

            if (property != null && property.IndexOfAny(['\r', '\n']) >= 0)
            {
                containsLineBreak = true;
            }

            text.Append(property ?? string.Empty);
        }

        return new RawPropertiesText(text.ToString(), containsLineBreak);
    }
}