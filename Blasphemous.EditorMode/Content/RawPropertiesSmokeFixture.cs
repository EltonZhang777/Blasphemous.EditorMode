#if DEBUG
using System;

namespace Blasphemous.EditorMode.Content;

internal static class RawPropertiesSmokeFixture
{
    internal const string ActivationVariable = "BLASPHEMOUS_EDITOR_MODE_SMOKE_FIXTURE";
    internal const string ActivationValue = "raw-properties";

    private static readonly string[] ActivationPropertyKeys =
    [
        "length",
        "color",
        "OneWayDown",
        "mode",
        "left",
        "right",
        "top",
        "bottom",
        "x_boundary",
        "y_boundary",
        "x_adjust",
        "y_adjust",
        "note",
        "x",
        "test-property"
    ];

    internal static string[] ResolveBellIronPropertyKeys(string activationValue)
    {
        return string.Equals(activationValue, ActivationValue, StringComparison.Ordinal)
            ? ActivationPropertyKeys
            : [];
    }
}
#endif