using BepInEx;

namespace Blasphemous.EditorMode;

[BepInPlugin(ModInfo.MOD_ID, ModInfo.MOD_NAME, ModInfo.MOD_VERSION)]
[BepInDependency("Blasphemous.ModdingAPI", "2.4.1")]
[BepInDependency("Blasphemous.Framework.Levels", "0.1.4")]
internal class Main : BaseUnityPlugin
{
    public static EditorMode EditorMode { get; private set; }

    private void Start()
    {
        EditorMode = new EditorMode();
    }
}
