using System;
using System.Collections.Generic;
using System.Drawing;
using ExileCore2.Shared.Attributes;
using ExileCore2.Shared.Nodes;

namespace ReAgent.ReAgentAuras;

public enum ReAgentAuraVisualSource
{
    Color,
    Icon,
    ManualIcon
}

public enum ReAgentAuraDisplayEffect
{
    ShowTimer,
    ShowCharges,
    ShowInstanceCount,
    ShowStack,
    ShowCustomText
}

public enum ReAgentAuraStartPosition
{
    Bottom,
    Top,
    Left,
    Right,
    Center
}

public sealed class ReAgentAuraRule
{
    public string Name { get; set; } = "New ReAgentAura";
    public string SourceName { get; set; } = "";
    public string Frame { get; set; } = "None";
    public ReAgentAuraVisualSource Visual { get; set; } = ReAgentAuraVisualSource.Color;
    public RangeNode<int> PositionX { get; set; } = new(760, 0, 4000);
    public RangeNode<int> PositionY { get; set; } = new(500, 0, 2500);
    public RangeNode<int> IconSize { get; set; } = new(64, 24, 160);
    public Color Color { get; set; } = Color.FromArgb(230, 90, 110, 150);
    public string ManualIconPath { get; set; } = "";
    public string ExtractedPngPath { get; set; } = "";
    public string ConditionSource { get; set; } = "false";
    public List<ReAgentAuraDisplay> Displays { get; set; } = [];
}

public sealed class ReAgentAuraDisplay
{
    public string Name { get; set; } = "New Display";
    public ReAgentAuraDisplayEffect Effect { get; set; } = ReAgentAuraDisplayEffect.ShowTimer;
    public ReAgentAuraStartPosition StartPosition { get; set; } = ReAgentAuraStartPosition.Bottom;
    [Menu("Offset X")]
    public RangeNode<int> OffsetX { get; set; } = new(0, -2000, 2000);
    [Menu("Offset Y")]
    public RangeNode<int> OffsetY { get; set; } = new(-2, -2000, 2000);
    [Menu("Text Scale")]
    public RangeNode<float> TextScale { get; set; } = new(1.0f, 0.5f, 2.0f);
    public Color TextColor { get; set; } = Color.FromArgb(255, 240, 240, 240);

    public static ReAgentAuraDisplay Create(ReAgentAuraDisplayEffect effect)
    {
        return new ReAgentAuraDisplay
        {
            Effect = effect,
            StartPosition = DefaultStartPosition(effect),
            OffsetY = new RangeNode<int>(DefaultOffsetY(effect), -2000, 2000)
        };
    }

    private static ReAgentAuraStartPosition DefaultStartPosition(ReAgentAuraDisplayEffect effect)
    {
        return effect switch
        {
            ReAgentAuraDisplayEffect.ShowCharges => ReAgentAuraStartPosition.Top,
            ReAgentAuraDisplayEffect.ShowInstanceCount => ReAgentAuraStartPosition.Right,
            ReAgentAuraDisplayEffect.ShowStack => ReAgentAuraStartPosition.Right,
            _ => ReAgentAuraStartPosition.Bottom
        };
    }

    private static int DefaultOffsetY(ReAgentAuraDisplayEffect effect)
    {
        return effect == ReAgentAuraDisplayEffect.ShowTimer ? -2 : 0;
    }
}

[Api]
public sealed class ReAgentAuraDisplayRuntime
{
    private string _text = "";

    internal ReAgentAuraDisplayRuntime(ReAgentAuraDisplay display)
    {
        Display = display;
        Name = display.Name;
        Enabled = false;
    }

    internal ReAgentAuraDisplay Display { get; }
    [Api]
    public string Name { get; }
    [Api]
    public bool Enabled { get; set; }
    [Api]
    public string Value { get; internal set; } = "";
    [Api]
    public string Text
    {
        get => Display.Effect == ReAgentAuraDisplayEffect.ShowCustomText ? _text : "";
        set
        {
            if (Display.Effect != ReAgentAuraDisplayEffect.ShowCustomText)
            {
                throw new InvalidOperationException(
                    $"Display '{Name}' uses effect '{Display.Effect}'. Text can only be set on Show Custom Text displays.");
            }

            _text = value ?? "";
        }
    }
}

public sealed record ReAgentAuraFrameLayout(
    string FileName,
    float Width,
    float Height,
    float InnerScale,
    float OffsetX,
    float OffsetY);

public sealed record ReAgentAuraDisplayEntry(
    RuleGroup Group,
    ReAgentAuraRule Rule,
    bool Active,
    string Error,
    IReadOnlyCollection<ReAgentAuraDisplayRuntime> Displays);

public sealed record ReAgentAuraIconSource(string DisplayName, string DdsFile);
