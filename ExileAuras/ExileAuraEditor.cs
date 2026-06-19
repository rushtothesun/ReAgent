using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using ExileCore2.Shared.Helpers;
using ExileCore2.Shared.Nodes;
using ImGuiNET;
using ReAgent.State;

namespace ReAgent.ExileAuras;

public sealed partial class ExileAurasModule
{
    private static readonly string[] VisualOptions = ["Color", "Icon", "Manual Icon"];
    private static readonly string[] DisplayEffectOptions = ["Show Timer", "Show Charges", "Show Instance Count", "Show Stack"];
    private static readonly string[] StartPositionOptions = Enum.GetNames<ExileAuraStartPosition>();

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
        DrawDisplays(rule);
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
        var frameIndex = Array.FindIndex(ExileAuraFrames.Options, x => string.Equals(x, rule.Frame, StringComparison.Ordinal));
        if (frameIndex < 0)
        {
            frameIndex = 0;
        }

        if (ImGui.Combo("Frame", ref frameIndex, ExileAuraFrames.Options, ExileAuraFrames.Options.Length))
        {
            rule.Frame = ExileAuraFrames.Options[frameIndex];
        }

        if (!string.Equals(rule.Frame, ExileAuraFrames.None, StringComparison.Ordinal) &&
            ExileAuraFrames.TryGetLayout(rule.Frame, out var layout))
        {
            var framePath = Path.Combine(ResolveFramesDirectory(), layout.FileName);
            if (!File.Exists(framePath))
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
        DrawColorEdit("Color", rule.Color, color => rule.Color = color);
    }

    private static void DrawManualIconPath(ExileAuraRule rule)
    {
        ImGui.PushItemWidth(520);
        var manualIconPath = rule.ManualIconPath ?? string.Empty;
        if (ImGui.InputText("Manual Icon PNG Path", ref manualIconPath, 512))
        {
            rule.ManualIconPath = manualIconPath;
            rule.IconTextureKey = ExileAuraTextureKeys.Icon(rule);
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

        var status = GetIconStatus(rule);
        if (status != null)
        {
            ImGui.SameLine();
            ImGui.TextColored(Color.LightGreen.ToImguiVec4(), status.Message);
        }
    }

    private void DrawDisplays(ExileAuraRule rule)
    {
        if (!ImGui.TreeNodeEx("Displays###exileAuraDisplays", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (ImGui.Button("Add Display"))
        {
            var display = ExileAuraDisplay.Create(ExileAuraDisplayEffect.ShowTimer);
            display.Name = GetNewDisplayName(rule);
            rule.Displays.Add(display);
        }

        DrawDisplayNameWarnings(rule);

        for (var i = 0; i < rule.Displays.Count; i++)
        {
            DrawDisplayRow(rule, rule.Displays[i], i);
        }

        ImGui.TreePop();
    }

    private void DrawDisplayRow(ExileAuraRule rule, ExileAuraDisplay display, int index)
    {
        if (string.IsNullOrWhiteSpace(display.Id))
        {
            display.Id = Guid.NewGuid().ToString("N");
        }

        ImGui.PushID(display.Id);

        var header = string.IsNullOrWhiteSpace(display.Name) ? GetDisplayEffectLabel(display.Effect) : display.Name;
        if (ImGui.TreeNodeEx($"{header}###display_header"))
        {
            DrawDisplayEditor(display);
            if (ImGui.Button("Remove Display"))
            {
                rule.Displays.RemoveAt(index);
                ImGui.TreePop();
                ImGui.PopID();
                return;
            }

            ImGui.TreePop();
        }

        ImGui.PopID();
    }

    private static void DrawDisplayEditor(ExileAuraDisplay display)
    {
        ImGui.PushItemWidth(260);

        var name = display.Name ?? string.Empty;
        if (ImGui.InputText("Name", ref name, 120))
        {
            display.Name = name;
        }

        var effect = (int)display.Effect;
        if (ImGui.Combo("Effect", ref effect, DisplayEffectOptions, DisplayEffectOptions.Length))
        {
            display.Effect = (ExileAuraDisplayEffect)effect;
        }

        var startPosition = (int)display.StartPosition;
        if (ImGui.Combo("Start Position", ref startPosition, StartPositionOptions, StartPositionOptions.Length))
        {
            display.StartPosition = (ExileAuraStartPosition)startPosition;
        }

        DrawRangeNode("Offset X", display.OffsetX);
        DrawRangeNode("Offset Y", display.OffsetY);
        DrawRangeNode("Text Scale", display.TextScale);
        ImGui.PopItemWidth();

        DrawColorEdit("Text Color", display.TextColor, color => display.TextColor = color);
    }

    private static void DrawDisplayNameWarnings(ExileAuraRule rule)
    {
        var validation = ExileAuraDisplayValidator.Validate(rule);
        if (!validation.Success)
        {
            ImGui.TextColored(Color.Yellow.ToImguiVec4(), validation.Error);
        }
    }

    private static string GetNewDisplayName(ExileAuraRule rule)
    {
        var existing = rule.Displays
            .Select(display => display.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < 10000; i++)
        {
            var name = $"Display {i}";
            if (!existing.Contains(name))
            {
                return name;
            }
        }

        return Guid.NewGuid().ToString("N");
    }

    private static string GetDisplayEffectLabel(ExileAuraDisplayEffect effect)
    {
        var index = (int)effect;
        return index >= 0 && index < DisplayEffectOptions.Length ? DisplayEffectOptions[index] : effect.ToString();
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
        var evaluation = _conditionCompiler.Evaluate(rule, state);
        if (!string.IsNullOrWhiteSpace(evaluation.Error))
        {
            ImGui.TextColored(Color.Red.ToImguiVec4(), evaluation.Error);
            return;
        }

        ImGui.TextColored((evaluation.Active ? Color.Lime : Color.Yellow).ToImguiVec4(), evaluation.Active ? "Aura is active." : "Aura is idle.");
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

    private static void DrawRangeNode(string label, RangeNode<float> node)
    {
        var value = node.Value;
        if (ImGui.SliderFloat($"##{label}", ref value, node.Min, node.Max, "%.3f"))
        {
            node.Value = value;
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(label);
    }

    private static void DrawColorEdit(string label, Color current, Action<Color> setColor)
    {
        var color = current.ToImguiVec4();
        if (ImGui.ColorEdit4(label, ref color, ImGuiColorEditFlags.NoInputs))
        {
            setColor(Color.FromArgb(
                ClampByte(color.W * 255f),
                ClampByte(color.X * 255f),
                ClampByte(color.Y * 255f),
                ClampByte(color.Z * 255f)));
        }
    }

    private static int ClampByte(float value)
    {
        return Math.Clamp((int)MathF.Round(value), 0, 255);
    }
}
