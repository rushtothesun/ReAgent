using System;
using System.Collections.Generic;
using System.Drawing;
using ExileCore2.Shared.Attributes;
using ExileCore2.Shared.Nodes;

namespace ReAgent.ExileAuras;

public enum ExileAuraVisualSource
{
    Color,
    Icon,
    ManualIcon
}

public enum ExileAuraDisplayEffect
{
    ShowTimer,
    ShowCharges,
    ShowInstanceCount,
    ShowStack
}

public enum ExileAuraStartPosition
{
    Bottom,
    Top,
    Left,
    Right,
    Center
}

public sealed class ExileAuraRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New ExileAura";
    public string SourceName { get; set; } = "";
    public string Frame { get; set; } = "None";
    public ExileAuraVisualSource Visual { get; set; } = ExileAuraVisualSource.Color;
    public RangeNode<int> PositionX { get; set; } = new(760, 0, 4000);
    public RangeNode<int> PositionY { get; set; } = new(500, 0, 2500);
    public RangeNode<int> IconSize { get; set; } = new(64, 24, 160);
    public Color Color { get; set; } = Color.FromArgb(230, 90, 110, 150);
    public string ManualIconPath { get; set; } = "";
    public string ExtractedPngPath { get; set; } = "";
    public string IconTextureKey { get; set; } = "";
    public string ConditionSource { get; set; } = "false";
    public List<ExileAuraDisplay> Displays { get; set; } = [];
}

public sealed class ExileAuraDisplay
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Display";
    public ExileAuraDisplayEffect Effect { get; set; } = ExileAuraDisplayEffect.ShowTimer;
    public ExileAuraStartPosition StartPosition { get; set; } = ExileAuraStartPosition.Bottom;
    [Menu("Offset X")]
    public RangeNode<int> OffsetX { get; set; } = new(0, -2000, 2000);
    [Menu("Offset Y")]
    public RangeNode<int> OffsetY { get; set; } = new(-2, -2000, 2000);
    [Menu("Text Scale")]
    public RangeNode<float> TextScale { get; set; } = new(1.0f, 0.5f, 2.0f);
    public Color TextColor { get; set; } = Color.FromArgb(255, 240, 240, 240);

    public static ExileAuraDisplay Create(ExileAuraDisplayEffect effect)
    {
        return new ExileAuraDisplay
        {
            Effect = effect,
            StartPosition = DefaultStartPosition(effect),
            OffsetY = new RangeNode<int>(DefaultOffsetY(effect), -2000, 2000)
        };
    }

    private static ExileAuraStartPosition DefaultStartPosition(ExileAuraDisplayEffect effect)
    {
        return effect switch
        {
            ExileAuraDisplayEffect.ShowCharges => ExileAuraStartPosition.Top,
            ExileAuraDisplayEffect.ShowInstanceCount => ExileAuraStartPosition.Right,
            ExileAuraDisplayEffect.ShowStack => ExileAuraStartPosition.Right,
            _ => ExileAuraStartPosition.Bottom
        };
    }

    private static int DefaultOffsetY(ExileAuraDisplayEffect effect)
    {
        return effect == ExileAuraDisplayEffect.ShowTimer ? -2 : 0;
    }
}

[Api]
public sealed class ExileAuraDisplayRuntime
{
    internal ExileAuraDisplayRuntime(ExileAuraDisplay display)
    {
        Display = display;
        Name = display.Name;
        Enabled = false;
    }

    internal ExileAuraDisplay Display { get; }
    [Api]
    public string Name { get; }
    [Api]
    public bool Enabled { get; set; }
    [Api]
    public string Value { get; internal set; } = "";
    [Api]
    public string Text { get; set; } = "";
    [Api]
    public string TextOverride { get; set; } = "";
}

public sealed record ExileAuraFrameLayout(
    string FileName,
    float Width,
    float Height,
    float InnerScale,
    float OffsetX,
    float OffsetY);

public sealed record ExileAuraDisplayEntry(
    RuleGroup Group,
    ExileAuraRule Rule,
    bool Active,
    string Error,
    IReadOnlyCollection<ExileAuraDisplayRuntime> Displays);

public sealed record ExileAuraIconSource(string DisplayName, string DdsFile);
