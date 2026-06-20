using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using ExileCore2.Shared.Helpers;
using ExileCore2.Shared.Nodes;
using ImGuiNET;
using ReAgent.State;

namespace ReAgent.ReAgentAuras;

public sealed partial class ReAgentAurasModule
{
    private static readonly string[] VisualOptions = ["Color", "Icon", "Manual Icon"];
    private static readonly string[] DisplayEffectOptions = ["Show Timer", "Show Charges", "Show Instance Count", "Show Stack", "Show Custom Text"];
    private static readonly string[] StartPositionOptions = Enum.GetNames<ReAgentAuraStartPosition>();

    internal void DrawRuleEditor(ReAgentAuraRule rule, RuleState state, bool expand)
    {
        if (!expand)
        {
            ImGui.TextUnformatted($"ReAgentAura: {rule.Name}");
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

    private static void DrawIdentity(ReAgentAuraRule rule)
    {
        var name = rule.Name ?? string.Empty;
        if (ImGui.InputText("Rule Name", ref name, 120))
        {
            rule.Name = name;
        }

        var sourceName = rule.SourceName ?? string.Empty;
        if (ImGui.InputText("Source Name", ref sourceName, 160))
        {
            if (!string.Equals(rule.SourceName, sourceName, StringComparison.Ordinal))
            {
                rule.ExtractedPngPath = "";
            }

            rule.SourceName = sourceName;
        }
    }

    private void DrawFrame(ReAgentAuraRule rule)
    {
        var frameIndex = Array.FindIndex(ReAgentAuraFrames.Options, x => string.Equals(x, rule.Frame, StringComparison.Ordinal));
        if (frameIndex < 0)
        {
            frameIndex = 0;
        }

        if (ImGui.Combo("Frame", ref frameIndex, ReAgentAuraFrames.Options, ReAgentAuraFrames.Options.Length))
        {
            rule.Frame = ReAgentAuraFrames.Options[frameIndex];
        }

        if (!string.Equals(rule.Frame, ReAgentAuraFrames.None, StringComparison.Ordinal) &&
            ReAgentAuraFrames.TryGetLayout(rule.Frame, out var layout))
        {
            var framePath = Path.Combine(ResolveFramesDirectory(), layout.FileName);
            if (!File.Exists(framePath))
            {
                ImGui.SameLine();
                ImGui.TextColored(Color.Gray.ToImguiVec4(), "Frame not extracted.");
            }
        }
    }

    private static void DrawVisual(ReAgentAuraRule rule)
    {
        var visual = (int)rule.Visual;
        if (ImGui.Combo("Visual", ref visual, VisualOptions, VisualOptions.Length))
        {
            rule.Visual = (ReAgentAuraVisualSource)visual;
        }
    }

    private void DrawVisualOptions(ReAgentAuraRule rule)
    {
        switch (rule.Visual)
        {
            case ReAgentAuraVisualSource.Color:
                DrawColorPicker(rule);
                break;
            case ReAgentAuraVisualSource.Icon:
                DrawIconExtraction(rule);
                break;
            case ReAgentAuraVisualSource.ManualIcon:
                DrawManualIconPath(rule);
                break;
        }
    }

    private static void DrawColorPicker(ReAgentAuraRule rule)
    {
        DrawColorEdit("Color", rule.Color, color => rule.Color = color);
    }

    private void DrawManualIconPath(ReAgentAuraRule rule)
    {
        ImGui.PushItemWidth(520);
        var manualIconPath = rule.ManualIconPath ?? string.Empty;
        if (ImGui.InputText("Manual Icon PNG Path", ref manualIconPath, 512))
        {
            rule.ManualIconPath = manualIconPath;
        }

        ImGui.PopItemWidth();

        var canRegister = !string.IsNullOrWhiteSpace(rule.ManualIconPath);
        if (!canRegister)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Register Icon"))
        {
            RegisterManualIcon(rule);
        }

        if (!canRegister)
        {
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextColored(Color.Gray.ToImguiVec4(), "Needs a PNG path.");
        }

        var status = GetIconStatus(rule);
        if (status != null)
        {
            ImGui.SameLine();
            var color = status.Kind == ReAgentAuraIconStatusKind.Failed ? Color.Salmon : Color.LightGreen;
            ImGui.TextColored(color.ToImguiVec4(), status.Message);
        }
    }

    private void RegisterManualIcon(ReAgentAuraRule rule)
    {
        var path = rule.ManualIconPath?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            SetIconStatus(rule, ReAgentAuraIconStatusKind.Failed, "Manual icon path is empty.");
            return;
        }

        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            SetIconStatus(rule, ReAgentAuraIconStatusKind.Failed, $"Manual icon path is invalid: {ex.Message}");
            return;
        }

        if (!File.Exists(path))
        {
            SetIconStatus(rule, ReAgentAuraIconStatusKind.Failed, "Manual icon file was not found.");
            return;
        }

        rule.ManualIconPath = path;
        var textureKey = ReAgentAuraTextureKeys.ManualIcon(path);
        if (TryEnsureImageRegistered(textureKey, path))
        {
            SetIconStatus(rule, ReAgentAuraIconStatusKind.Ready, "Manual icon registered.");
            return;
        }

        SetIconStatus(rule, ReAgentAuraIconStatusKind.Failed, "Manual icon could not be registered.");
    }

    private void DrawIconExtraction(ReAgentAuraRule rule)
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

    private void DrawDisplays(ReAgentAuraRule rule)
    {
        if (!ImGui.TreeNodeEx("Displays###reAgentAuraDisplays", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (ImGui.Button("Add Display"))
        {
            var display = ReAgentAuraDisplay.Create(ReAgentAuraDisplayEffect.ShowTimer);
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

    private void DrawDisplayRow(ReAgentAuraRule rule, ReAgentAuraDisplay display, int index)
    {
        ImGui.PushID(index);

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

    private static void DrawDisplayEditor(ReAgentAuraDisplay display)
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
            display.Effect = (ReAgentAuraDisplayEffect)effect;
        }

        var startPosition = (int)display.StartPosition;
        if (ImGui.Combo("Start Position", ref startPosition, StartPositionOptions, StartPositionOptions.Length))
        {
            display.StartPosition = (ReAgentAuraStartPosition)startPosition;
        }

        DrawRangeNode("Offset X", display.OffsetX);
        DrawRangeNode("Offset Y", display.OffsetY);
        DrawRangeNode("Text Scale", display.TextScale);
        ImGui.PopItemWidth();

        DrawColorEdit("Text Color", display.TextColor, color => display.TextColor = color);
    }

    private static void DrawDisplayNameWarnings(ReAgentAuraRule rule)
    {
        var validation = ReAgentAuraDisplayValidator.Validate(rule);
        if (!validation.Success)
        {
            ImGui.TextColored(Color.Yellow.ToImguiVec4(), validation.Error);
        }
    }

    private static string GetNewDisplayName(ReAgentAuraRule rule)
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

    private static string GetDisplayEffectLabel(ReAgentAuraDisplayEffect effect)
    {
        var index = (int)effect;
        return index >= 0 && index < DisplayEffectOptions.Length ? DisplayEffectOptions[index] : effect.ToString();
    }

    private void DrawCondition(ReAgentAuraRule rule, RuleState state)
    {
        ImGui.TextUnformatted("Condition");
        var source = rule.ConditionSource ?? string.Empty;
        if (ImGui.InputTextMultiline(
                "##reAgentAuraCondition",
                ref source,
                10000,
                new Vector2(ImGui.GetContentRegionAvail().X, Math.Max(ImGui.GetTextLineHeight() * 3f, ImGui.CalcTextSize($"^{source}_").Y + ImGui.GetTextLineHeight()))))
        {
            rule.ConditionSource = source;
        }

        DrawConditionStatus(rule, state);
    }

    private void DrawConditionStatus(ReAgentAuraRule rule, RuleState state)
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
