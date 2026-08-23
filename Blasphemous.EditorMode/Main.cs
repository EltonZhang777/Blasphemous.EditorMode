using BepInEx;
using Blasphemous.EditorMode.Diagnostics;

namespace Blasphemous.EditorMode;

[BepInPlugin(ModInfo.MOD_ID, ModInfo.MOD_NAME, ModInfo.MOD_VERSION)]
[BepInDependency(TargetDependencies.ModdingApiId, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(TargetDependencies.LevelFrameworkId, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(TargetDependencies.UiFrameworkId, BepInDependency.DependencyFlags.SoftDependency)]
internal class Main : BaseUnityPlugin
{
    public static EditorMode EditorMode { get; private set; }

    internal static DependencyGateResult DependencyStatus { get; private set; }

    private void Start()
    {
        DependencyStatus = DependencyGate.Evaluate(
            TargetDependencies.Required,
            RuntimeDependencyProbe.ReadInstalled());

        if (!DependencyStatus.IsCompatible)
        {
            Logger.LogError(DependencyStatus.StatusMessage);
            return;
        }

        Logger.LogInfo(DependencyStatus.StatusMessage);
        EditorMode = new EditorMode();
    }
}
