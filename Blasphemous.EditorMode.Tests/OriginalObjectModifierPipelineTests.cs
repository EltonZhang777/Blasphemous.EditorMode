using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Editing;
using Blasphemous.EditorMode.Framework;
using Blasphemous.EditorMode.Runtime;
using System;
using System.Collections.Generic;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class OriginalObjectModifierPipelineTests
{
    [Fact]
    public void Modifier_is_verified_before_base_and_runs_after_base()
    {
        FakeModifierTarget target = new();
        FakeModifierSource source = new(target.Steps)
        {
            Modifier = new RecordingModifier("type")
        };
        FrameworkObjectData modification = Modification();

        ApplyReport report = new();
        bool applied = OriginalObjectModifierPipeline.TryApply(
            modification,
            source,
            target,
            report);

        Assert.True(applied, report.ToSummary("apply"));
        Assert.Equal(new[] { "resolve:editor-mode-empty", "capture", "base", "type" }, target.Steps);
        Assert.Empty(target.RestoredStates);
    }

    [Fact]
    public void Missing_modifier_does_not_apply_the_base_transform()
    {
        FakeModifierTarget target = new();
        FakeModifierSource source = new(target.Steps)
        {
            Error = "Creator is not registered."
        };

        ApplyReport report = new();
        bool applied = OriginalObjectModifierPipeline.TryApply(
            Modification(),
            source,
            target,
            report);

        Assert.False(applied);
        Assert.Equal(new[] { "resolve:editor-mode-empty" }, target.Steps);
        Assert.Empty(target.RestoredStates);
        ApplyReportEntry failure = Assert.Single(
            report.Entries,
            entry => entry.Code == ReportCodes.OriginalObjectModifierUnavailable);
        Assert.Equal("decoration:DECORATION/HERO", failure.Location);
        Assert.Contains("editor-mode-empty", failure.Message);
    }

    [Fact]
    public void Throwing_modifier_restores_the_captured_live_state()
    {
        FakeModifierTarget target = new();
        FakeModifierSource source = new(target.Steps)
        {
            Modifier = new ThrowingModifier()
        };

        ApplyReport report = new();
        bool applied = OriginalObjectModifierPipeline.TryApply(
            Modification(),
            source,
            target,
            report);

        Assert.False(applied);
        Assert.Equal(new[] { "resolve:editor-mode-empty", "capture", "base", "type", "restore" }, target.Steps);
        OriginalObjectLiveState restored = Assert.Single(target.RestoredStates);
        Assert.Equal("Hero", restored.Name);
        Assert.Equal(1f, restored.Transform.PositionX);
        Assert.Contains(
            report.Entries,
            entry => entry.Code == ReportCodes.OriginalObjectModifierFailed);
    }

    [Fact]
    public void Empty_transform_modifier_does_not_change_name_or_components()
    {
        FakeModifierTarget target = new();
        FakeModifierSource source = new(target.Steps)
        {
            Modifier = new EditorModeEmptyFrameworkModifier()
        };

        ApplyReport report = new();
        Assert.True(
            OriginalObjectModifierPipeline.TryApply(
                Modification(),
                source,
                target,
                report),
            report.ToSummary("apply empty"));

        Assert.Equal("Hero", target.Name);
        Assert.Equal(new[] { "Collider", "Animator" }, target.Components);
    }

    private static FrameworkObjectData Modification()
    {
        return new FrameworkObjectData(
            "decoration",
            "DECORATION/HERO",
            OriginalObjectEditor.EmptyModificationType,
            "editor-mode-empty-1",
            new FrameworkVector(7f, 8f, 9f),
            new FrameworkVector(10f, 11f, 12f),
            new FrameworkVector(2f, 3f, 4f),
            string.Empty,
            []);
    }

    private sealed class FakeModifierSource : IOriginalObjectModifierSource
    {
        private readonly List<string> _steps;

        internal FakeModifierSource(List<string> steps)
        {
            _steps = steps;
        }

        internal IOriginalObjectModifier Modifier { get; set; }
        internal string Error { get; set; }

        public bool TryCreate(
            string type,
            out IOriginalObjectModifier modifier,
            out string error)
        {
            modifier = Modifier;
            error = Error;
            _steps.Add("resolve:" + type);
            return modifier != null;
        }
    }

    private sealed class RecordingModifier : IOriginalObjectModifier
    {
        private readonly string _name;

        internal RecordingModifier(string name)
        {
            _name = name;
        }

        public void Apply(FrameworkObjectData modification)
        {
        }

        public override string ToString()
        {
            return _name;
        }
    }

    private sealed class ThrowingModifier : IOriginalObjectModifier
    {
        public void Apply(FrameworkObjectData modification)
        {
            throw new InvalidOperationException("Type modifier failed.");
        }
    }

    private sealed class FakeModifierTarget : IOriginalObjectModifierTarget
    {
        private readonly OriginalObjectLiveState _initial = new(
            "42",
            true,
            new PlacementTransform(1f, 2f, 3f, 4f, 5f, 6f, 1f, 1f, 1f),
            "Hero");

        internal List<string> Steps { get; } = [];
        internal List<OriginalObjectLiveState> RestoredStates { get; } = [];
        internal string Name { get; set; } = "Hero";
        internal string[] Components { get; } = ["Collider", "Animator"];

        public OriginalObjectLiveState CaptureState()
        {
            Steps.Add("capture");
            return _initial;
        }

        public void ApplyBase(FrameworkObjectData modification)
        {
            Steps.Add("base");
        }

        public void ApplyType(
            IOriginalObjectModifier modifier,
            FrameworkObjectData modification)
        {
            Steps.Add("type");
            modifier.Apply(modification);
        }

        public void RestoreState(OriginalObjectLiveState state)
        {
            Steps.Add("restore");
            RestoredStates.Add(state);
            Name = state.Name;
        }
    }
}