using System;

namespace Blasphemous.EditorMode.Components;

/// <summary>Tracks the multiline raw-properties draft independently of Unity UI state.</summary>
internal sealed class RawPropertiesDraftState
{
    private string _selectedId;
    private string _committedText = string.Empty;
    private string _text = string.Empty;
    private bool _loaded;

    internal string Text { get { return _text; } }

    internal bool Reload(
        string selectedId,
        string committedText,
        bool preserveDraft)
    {
        bool selectionChanged = !_loaded
            || !string.Equals(_selectedId, selectedId, StringComparison.OrdinalIgnoreCase);
        string normalizedCommittedText = committedText ?? string.Empty;
        bool committedTextChanged = !string.Equals(
            _committedText,
            normalizedCommittedText,
            StringComparison.Ordinal);
        _selectedId = selectedId;
        _committedText = normalizedCommittedText;
        _loaded = true;

        if (!selectionChanged && !committedTextChanged && preserveDraft)
        {
            return false;
        }

        SetText(_committedText);
        return true;
    }

    internal bool Edit(string text)
    {
        return SetText(text ?? string.Empty);
    }

    private bool SetText(string text)
    {
        if (string.Equals(_text, text, StringComparison.Ordinal))
        {
            return false;
        }

        _text = text;
        return true;
    }
}