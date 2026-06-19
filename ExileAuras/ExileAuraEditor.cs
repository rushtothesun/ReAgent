using System;
using System.Drawing;
using System.Numerics;
using ExileCore2.Shared.Helpers;
using ExileCore2.Shared.Nodes;
using ImGuiNET;
using ReAgent.State;

namespace ReAgent.ExileAuras;

public sealed partial class ExileAurasModule
{
    private static readonly string[] VisualOptions = ["Color", "Icon", "Manual Icon"];

    internal void DrawRuleEditor(ExileAuraRule rule, RuleState state, bool expand)
    {
        if (!expand)
        {
            ImGui.TextUnformatted($"ExileAura: {rule.Name}");
            ImGui.SameLine();
            DrawConditionStatus(rule, state);
            return;
        }

        ImGui.PushItemWidth(260);
        DrawIdentity(rule);
        DrawFrame(rule);
        DrawRangeNode("Position X", rule.PositionX);
        DrawRangeNode("Position Y", rule.PositionY);
        DrawRangeNode("Icon Size", rule.IconSize);
        DrawVisual(rule);
        ImGui.PopItemWidth();

        DrawVisualOptions(rule);
        DrawCondition(rule, state);
    }

    private static void DrawIdentity(ExileAuraRule rule)
    {
        var name = rule.Name ?? string.Empty;
        if (ImGui.InputText("Rule Name", ref name, 120))
        {
            rule.Name = name;
        }

        var sourceName = rule.SourceName ?? string.Empty;
        if (ImGui.InputText("Source Name", ref sourceName, 160))
        {
            rule.SourceName = sourceName;
        }
    }

    private void DrawFrame(ExileAuraRule rule)
    {
        var frameIndex = Array.FindIndex(FrameOptions, x => string.Equals(x, rule.Frame, StringComparison.Ordinal));
        if (frameIndex < 0)
        {
            frameIndex = 0;
        }

        if (ImGui.Combo("Frame", ref frameIndex, FrameOptions, FrameOptions.Length))
        {
            rule.Frame = FrameOptions[frameIndex];
        }

        if (!string.Equals(rule.Frame, "None", StringComparison.Ordinal) &&
            FrameLayouts.TryGetValue(rule.Frame, out var layout))
        {
            var framePath = System.IO.Path.Combine(ResolveFramesDirectory(), layout.FileName);
            if (!System.IO.File.Exists(framePath))
            {
                ImGui.SameLine();
                ImGui.TextColored(Color.Gray.ToImguiVec4(), "Frame not extracted.");
            }
        }
    }

    private static void DrawVisual(ExileAuraRule rule)
    {
        var visual = (int)rule.Visual;
        if (ImGui.Combo("Visual", ref visual, VisualOptions, VisualOptions.Length))
        {
            rule.Visual = (ExileAuraVisualSource)visual;
        }
    }

    private void DrawVisualOptions(ExileAuraRule rule)
    {
        switch (rule.Visual)
        {
            case ExileAuraVisualSource.Color:
                DrawColorPicker(rule);
                break;
            case ExileAuraVisualSource.Icon:
                DrawIconExtraction(rule);
                break;
            case ExileAuraVisualSource.ManualIcon:
                DrawManualIconPath(rule);
                break;
        }
    }

    private static void DrawColorPicker(ExileAuraRule rule)
    {
        var color = rule.Color.ToImguiVec4();
        if (ImGui.ColorEdit4("Color", ref color, ImGuiColorEditFlags.NoInputs))
        {
            rule.Color = Color.FromArgb(
                ClampByte(color.W * 255f),
                ClampByte(color.X * 255f),
                ClampByte(color.Y * 255f),
                ClampByte(color.Z * 255f));
        }
    }

    private static void DrawManualIconPath(ExileAuraRule rule)
    {
        ImGui.PushItemWidth(520);
        var manualIconPath = rule.ManualIconPath ?? string.Empty;
        if (ImGui.InputText("Manual Icon PNG Path", ref manualIconPath, 512))
        {
            rule.ManualIconPath = manualIconPath;
            rule.IconTextureKey = CreateTextureKey(rule);
        }

        ImGui.PopItemWidth();
    }

    private void DrawIconExtraction(ExileAuraRule rule)
    {
        RefreshPendingIconStatus(rule);
        var source = ResolveIconSource(rule.SourceName);
        var canExtract = Settings.EnableExtraction.Value && !string.IsNullOrWhiteSpace(source.DdsFile);
        if (!canExtract)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Extract Icon"))
        {
            ExtractOrRegisterRuleIcon(rule, source.DdsFile);
        }

        if (!canExtract)
        {
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextColored(Color.Gray.ToImguiVec4(), Settings.EnableExtraction.Value ? "Needs active source icon." : "Extraction is disabled.");
        }

        if (!string.IsNullOrWhiteSpace(rule.IconStatus) && Environment.TickCount64 < rule.IconStatusExpiresAtMs)
        {
            ImGui.SameLine();
            ImGui.TextColored(Color.LightGreen.ToImguiVec4(), rule.IconStatus);
        }
    }

    private void DrawCondition(ExileAuraRule rule, RuleState state)
    {
        ImGui.TextUnformatted("Condition");
        var source = rule.ConditionSource ?? string.Empty;
        if (ImGui.InputTextMultiline(
                "##exileAuraCondition",
                ref source,
                10000,
                new Vector2(ImGui.GetContentRegionAvail().X, Math.Max(ImGui.GetTextLineHeight() * 3f, ImGui.CalcTextSize($"^{source}_").Y + ImGui.GetTextLineHeight()))))
        {
            rule.ConditionSource = source;
        }

        DrawConditionStatus(rule, state);
    }

    private void DrawConditionStatus(ExileAuraRule rule, RuleState state)
    {
        var (active, error) = _conditionCompiler.Evaluate(rule, state);
        if (!string.IsNullOrWhiteSpace(error))
        {
            ImGui.TextColored(Color.Red.ToImguiVec4(), error);
            return;
        }

        ImGui.TextColored((active ? Color.Lime : Color.Yellow).ToImguiVec4(), active ? "Aura is active." : "Aura is idle.");
    }

    private static void DrawRangeNode(string label, RangeNode<int> node)
    {
        var value = node.Value;
        if (ImGui.SliderInt($"##{label}", ref value, node.Min, node.Max))
        {
            node.Value = value;
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(label);
    }

    private static int ClampByte(float value)
    {
        return Math.Clamp((int)MathF.Round(value), 0, 255);
    }
}
