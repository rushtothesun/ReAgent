using System;
using System.Drawing;
using ExileCore2.Shared.Nodes;

namespace ReAgent.ExileAuras;

public enum ExileAuraVisualSource
{
    Color,
    Icon,
    ManualIcon
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

    public bool ShouldSerializeIconStatus() => false;
    public string IconStatus { get; set; } = "";
    public bool ShouldSerializeIconStatusExpiresAtMs() => false;
    public long IconStatusExpiresAtMs { get; set; }
}

public sealed record ExileAuraFrameLayout(
    string FileName,
    float Width,
    float Height,
    float InnerScale,
    float OffsetX,
    float OffsetY);

public sealed record ExileAuraDisplayEntry(ExileAuraRule Rule, bool Active, string Error);

public sealed record ExileAuraIconSource(string DisplayName, string DdsFile);
