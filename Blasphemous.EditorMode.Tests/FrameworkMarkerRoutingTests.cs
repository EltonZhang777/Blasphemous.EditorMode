using Blasphemous.EditorMode.Runtime;
using System;
using System.Collections.Generic;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class FrameworkMarkerRoutingTests
{
    [Fact]
    public void Creator_type_buckets_and_matching_preserve_case_variants()
    {
        Dictionary<string, Queue<string>> creatorTypeBuckets =
            FrameworkMarkerRouting.CreateCreatorTypeBuckets<Queue<string>>();
        Queue<string> lower = new();
        Queue<string> upper = new();
        creatorTypeBuckets.Add("bell-iron", lower);
        creatorTypeBuckets.Add("Bell-Iron", upper);
        Dictionary<string, Dictionary<string, Queue<string>>> lookup =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["room"] = creatorTypeBuckets
            };

        Assert.True(FrameworkMarkerRouting.TryGetCreatorTypeBuckets(
            lookup, "room", "bell-iron", out Queue<string> selectedLower));
        Assert.True(FrameworkMarkerRouting.TryGetCreatorTypeBuckets(
            lookup, "room", "Bell-Iron", out Queue<string> selectedUpper));
        Assert.False(FrameworkMarkerRouting.TryGetCreatorTypeBuckets(
            lookup, "room", "BELL-IRON", out _));
        Assert.Same(lower, selectedLower);
        Assert.Same(upper, selectedUpper);
        Assert.True(FrameworkMarkerRouting.AreExactCreatorTypes("bell-iron", "bell-iron"));
        Assert.False(FrameworkMarkerRouting.AreExactCreatorTypes("bell-iron", "Bell-Iron"));
    }
}