namespace Blasphemous.EditorMode;

internal sealed class EditorModeConfig
{
    public EditorModeConfig()
    {
        VerboseDiagnostics = false;
    }

    public bool VerboseDiagnostics { get; set; }
}