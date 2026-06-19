using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using ExileCore2.Shared;
using ImGuiNET;
using ReAgent.State;

namespace ReAgent.ExileAuras;

public sealed partial class ExileAurasModule
{
    private static readonly Color PlacementBackgroundColor = Color.FromArgb(120, 0, 0, 0);
    private static readonly Color PlacementFrameColor = Color.FromArgb(210, 235, 190, 90);

    private readonly List<ExileAuraDisplayEntry> _displayEntries = new();
    private string _draggingRuleId = "";

    internal void Render(Profile profile, RuleState state)
    {
        if (profile == null || state == null)
        {
            return;
        }

        UpdateDisplayEntries(profile, state);
        DrawAuras(profile, state);
    }

    private void UpdateDisplayEntries(Profile profile, RuleState state)
    {
        var now = Environment.TickCount64;
        if (now < _nextPollMs)
        {
            return;
        }

        _nextPollMs = now + Settings.PollIntervalMs.Value;
        _displayEntries.Clear();

        foreach (var rule in EnumerateExileAuraRules(profile, state))
        {
            var (active, error) = _conditionCompiler.Evaluate(rule, state);
            if (active)
            {
                _displayEntries.Add(new ExileAuraDisplayEntry(rule, true, error));
            }
        }
    }

    private void DrawAuras(Profile profile, RuleState state)
    {
        var entries = Settings.Unlocked.Value
            ? EnumerateExileAuraRules(profile, state)
                .Select(rule =>
                {
                    var (active, error) = _conditionCompiler.Evaluate(rule, state);
                    return new ExileAuraDisplayEntry(rule, active, error);
                })
                .ToList()
            : _displayEntries;

        if (entries.Count == 0)
        {
            _draggingRuleId = "";
            return;
        }

        foreach (var entry in entries)
        {
            DrawAura(entry);
        }
    }

    private static IEnumerable<ExileAuraRule> EnumerateExileAuraRules(Profile profile, RuleState state)
    {
        return profile.Groups
            .Where(group => group.ShouldEvaluate(state))
            .SelectMany(group => group.Rules)
            .Where(rule => rule.Kind == RuleKind.ExileAura && rule.ExileAura != null)
            .Select(rule => rule.ExileAura);
    }

    private void DrawAura(ExileAuraDisplayEntry entry)
    {
        var rule = entry.Rule;
        var position = new Vector2(rule.PositionX.Value, rule.PositionY.Value);
        var iconSize = rule.IconSize.Value;
        var boundsSize = new Vector2(iconSize, iconSize);

        HandleDrag(rule, position, boundsSize);

        if (Settings.Unlocked.Value)
        {
            DrawUnlockedBounds(rule, entry, position, boundsSize);
        }

        DrawAuraIcon(entry, position, iconSize);
    }

    private void DrawUnlockedBounds(ExileAuraRule rule, ExileAuraDisplayEntry entry, Vector2 position, Vector2 boundsSize)
    {
        _plugin.Graphics.DrawBox(position - new Vector2(7f, 22f), position + boundsSize + new Vector2(7f, 7f), PlacementBackgroundColor, 4f);
        _plugin.Graphics.DrawFrame(position - new Vector2(7f, 22f), position + boundsSize + new Vector2(7f, 7f), PlacementFrameColor, 2);
        var label = $"{rule.Name} {rule.PositionX.Value},{rule.PositionY.Value}";
        if (!entry.Active)
        {
            label += " idle";
        }

        _plugin.Graphics.DrawText(label, position - new Vector2(1f, 20f), PlacementFrameColor);
    }

    private void HandleDrag(ExileAuraRule rule, Vector2 position, Vector2 size)
    {
        if (!Settings.Unlocked.Value)
        {
            _draggingRuleId = "";
            return;
        }

        var dragRectMin = position - new Vector2(7f, 22f);
        var dragSize = size + new Vector2(14f, 29f);
        var flags = ImGuiWindowFlags.NoTitleBar
                    | ImGuiWindowFlags.NoResize
                    | ImGuiWindowFlags.NoMove
                    | ImGuiWindowFlags.NoScrollbar
                    | ImGuiWindowFlags.NoScrollWithMouse
                    | ImGuiWindowFlags.NoCollapse
                    | ImGuiWindowFlags.NoBackground
                    | ImGuiWindowFlags.NoSavedSettings
                    | ImGuiWindowFlags.NoFocusOnAppearing;

        ImGui.SetNextWindowPos(dragRectMin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(dragSize, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        if (ImGui.Begin($"##exileauras_drag_surface_{rule.Id}", flags))
        {
            ImGui.InvisibleButton($"##exileauras_drag_button_{rule.Id}", dragSize);
            var hovered = ImGui.IsItemHovered();
            var active = ImGui.IsItemActive();

            if (active)
            {
                _draggingRuleId = rule.Id;
            }
            else if (string.Equals(_draggingRuleId, rule.Id, StringComparison.Ordinal) && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                _draggingRuleId = "";
            }

            var draggingThisRule = string.Equals(_draggingRuleId, rule.Id, StringComparison.Ordinal);
            if (hovered || draggingThisRule)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }

            if (active)
            {
                ApplyDragDelta(rule);
            }
        }

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private static void ApplyDragDelta(ExileAuraRule rule)
    {
        var delta = ImGui.GetIO().MouseDelta;
        if (delta == Vector2.Zero)
        {
            return;
        }

        rule.PositionX.Value = Math.Clamp((int)MathF.Round(rule.PositionX.Value + delta.X), rule.PositionX.Min, rule.PositionX.Max);
        rule.PositionY.Value = Math.Clamp((int)MathF.Round(rule.PositionY.Value + delta.Y), rule.PositionY.Min, rule.PositionY.Max);
    }

    private void DrawAuraIcon(ExileAuraDisplayEntry entry, Vector2 position, float iconSize)
    {
        var rule = entry.Rule;
        var iconRectSize = new Vector2(iconSize, iconSize);
        var color = rule.Visual == ExileAuraVisualSource.Color ? rule.Color : Color.FromArgb(180, 45, 45, 45);
        var iconTextureKey = "";
        var hasIconTexture = rule.Visual is ExileAuraVisualSource.Icon or ExileAuraVisualSource.ManualIcon &&
                             TryEnsureRuleIconRegistered(rule, out iconTextureKey);
        var frameWasDrawn = hasIconTexture && TryDrawSelectedFrame(rule, iconTextureKey, position, iconSize);
        var iconWasDrawn = frameWasDrawn || (hasIconTexture && TryDrawRegisteredIcon(iconTextureKey, position, iconSize));

        if (!iconWasDrawn)
        {
            _plugin.Graphics.DrawBox(position, position + iconRectSize, color, 4f);
        }

        _plugin.Graphics.DrawFrame(position, position + iconRectSize, Color.FromArgb(230, 10, 10, 10), 1);

        if (!iconWasDrawn)
        {
            DrawCenteredText(Initials(rule.Name), position, iconRectSize, Color.White);
        }
    }

    private bool TryDrawRegisteredIcon(string textureKey, Vector2 position, float iconSize)
    {
        _plugin.Graphics.DrawImage(textureKey, new ExileCore2.Shared.RectangleF(position.X, position.Y, iconSize, iconSize));
        return true;
    }

    private bool TryDrawSelectedFrame(ExileAuraRule rule, string iconTextureKey, Vector2 position, float iconSize)
    {
        if (!TryEnsureFrameRegistered(rule.Frame, out var frameTextureKey, out var layout))
        {
            return false;
        }

        var scale = iconSize / Math.Max(layout.Width, layout.Height);
        var innerSize = layout.Width * layout.InnerScale * scale;
        var innerX = position.X + ((layout.Width - layout.Width * layout.InnerScale) / 2f + layout.OffsetX) * scale;
        var innerY = position.Y + ((layout.Height - layout.Width * layout.InnerScale) / 2f + layout.OffsetY) * scale;

        _plugin.Graphics.DrawImage(iconTextureKey, new ExileCore2.Shared.RectangleF(innerX, innerY, innerSize, innerSize));
        _plugin.Graphics.DrawImage(frameTextureKey, new ExileCore2.Shared.RectangleF(position.X, position.Y, layout.Width * scale, layout.Height * scale));
        return true;
    }

    private bool TryEnsureRuleIconRegistered(ExileAuraRule rule, out string textureKey)
    {
        textureKey = string.IsNullOrWhiteSpace(rule.IconTextureKey) ? CreateTextureKey(rule) : rule.IconTextureKey;
        rule.IconTextureKey = textureKey;

        var iconPath = rule.Visual == ExileAuraVisualSource.ManualIcon ? rule.ManualIconPath : rule.ExtractedPngPath;
        return !string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath) && TryEnsureImageRegistered(textureKey, iconPath);
    }

    private bool TryEnsureFrameRegistered(string frameName, out string textureKey, out ExileAuraFrameLayout layout)
    {
        textureKey = "";
        layout = default;

        if (string.IsNullOrWhiteSpace(frameName) ||
            string.Equals(frameName, "None", StringComparison.Ordinal) ||
            !FrameLayouts.TryGetValue(frameName, out layout))
        {
            return false;
        }

        textureKey = CreateFrameTextureKey(frameName);
        var framePath = Path.Combine(ResolveFramesDirectory(), layout.FileName);
        return File.Exists(framePath) && TryEnsureImageRegistered(textureKey, framePath);
    }

    private bool TryEnsureImageRegistered(string textureKey, string path)
    {
        if (IsTextureRegistered(textureKey))
        {
            _registeredTextureKeys.Add(textureKey);
            return true;
        }

        try
        {
            if (_plugin.Graphics.InitImage(textureKey, path))
            {
                _registeredTextureKeys.Add(textureKey);
                return true;
            }
        }
        catch
        {
        }

        _registeredTextureKeys.Remove(textureKey);
        return false;
    }

    private bool IsTextureRegistered(string textureKey)
    {
        try
        {
            return _plugin.Graphics.HasImage(textureKey);
        }
        catch
        {
            return false;
        }
    }

    private void DrawCenteredText(string text, Vector2 position, Vector2 size, Color color)
    {
        var textSize = _plugin.Graphics.MeasureText(text);
        var textPosition = new Vector2(
            position.X + (size.X - textSize.X) / 2f,
            position.Y + (size.Y - textSize.Y) / 2f);
        _plugin.Graphics.DrawText(text, textPosition, color);
    }
}
