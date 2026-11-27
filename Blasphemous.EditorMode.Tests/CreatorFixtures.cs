using Blasphemous.EditorMode.Content;

namespace Blasphemous.EditorMode.Tests;

internal static class CreatorFixtures
{
    internal const string BellIronType = "bell-iron";

    internal static readonly CreatorMetadata BellIron = new(
        BellIronType,
        "Iron Bell",
        "Traps",
        CreatorOrigin.BaseFramework,
        "D03Z02S06_LOGIC",
        "TRAPS/TRAP_SHOCK_CHAIN_REACTION",
        TransformCapabilities.Position
            | TransformCapabilities.Rotation
            | TransformCapabilities.Scale,
        new string[0]);
}