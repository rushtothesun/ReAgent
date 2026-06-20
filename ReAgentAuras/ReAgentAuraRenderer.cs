using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using ExileCore2.Shared;
using ExileCore2.Shared.Helpers;
using ImGuiNET;
using ReAgent.State;

namespace ReAgent.ReAgentAuras;

public sealed partial class ReAgentAurasModule
{
    private static readonly Color PlacementBackgroundColor = Color.FromArgb(120, 0, 0, 0);
    private static readonly Color PlacementFrameColor = Color.FromArgb(210, 235, 190, 90);

    private readonly List<ReAgentAuraDisplayEntry> _displayEntries = new();
    private ReAgentAuraRule _draggingRule;

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

        foreach (var (group, rule) in EnumerateReAgentAuraRules(profile, state))
        {
            var evaluation = _conditionCompiler.Evaluate(rule, state);
            if (evaluation.Active)
            {
                _displayEntries.Add(new ReAgentAuraDisplayEntry(group, rule, true, evaluation.Error, evaluation.Displays));
            }
        }
    }

    private void DrawAuras(Profile profile, RuleState state)
    {
        var entries = Settings.Unlocked.Value
            ? EnumerateReAgentAuraRules(profile, state)
                .Select(item =>
                {
                    var (group, rule) = item;
                    var evaluation = _conditionCompiler.Evaluate(rule, state);
                    return new ReAgentAuraDisplayEntry(group, rule, evaluation.Active, evaluation.Error, evaluation.Displays);
                })
                .ToList()
            : _displayEntries;

        if (entries.Count == 0)
        {
            _draggingRule = null;
            return;
        }

        foreach (var entry in entries)
        {
            DrawAura(entry);
        }
    }

    private IEnumerable<(RuleGroup Group, ReAgentAuraRule Rule)> EnumerateReAgentAuraRules(Profile profile, RuleState state)
    {
        return profile.Groups
            .Where(group => Settings.Unlocked.Value ? group.ShouldEvaluateArea(state) : group.ShouldEvaluate(state))
            .SelectMany(group => group.Rules.Select(rule => (Group: group, Rule: rule)))
            .Where(rule => rule.Rule.Kind == RuleKind.ReAgentAura && rule.Rule.ReAgentAura != null)
            .Select(rule => (rule.Group, rule.Rule.ReAgentAura));
    }

    private void DrawAura(ReAgentAuraDisplayEntry entry)
    {
        var rule = entry.Rule;
        var position = new Vector2(rule.PositionX.Value, rule.PositionY.Value);
        var iconSize = rule.IconSize.Value;
        var boundsSize = new Vector2(iconSize, iconSize);

        HandleDrag(entry, position, boundsSize);

        if (Settings.Unlocked.Value)
        {
            DrawUnlockedBounds(entry, position, boundsSize);
        }

        DrawAuraIcon(entry, position, iconSize);
        if (entry.Active)
        {
            DrawTextDisplays(entry, position, iconSize);
        }
    }

    private void DrawUnlockedBounds(ReAgentAuraDisplayEntry entry, Vector2 position, Vector2 boundsSize)
    {
        var rule = entry.Rule;
        _plugin.Graphics.DrawBox(position - new Vector2(7f, 22f), position + boundsSize + new Vector2(7f, 7f), PlacementBackgroundColor, 4f);
        _plugin.Graphics.DrawFrame(position - new Vector2(7f, 22f), position + boundsSize + new Vector2(7f, 7f), PlacementFrameColor, 2);
        var label = $"{entry.Group.Name}: {rule.Name} {rule.PositionX.Value},{rule.PositionY.Value}";
        if (!entry.Active)
        {
            label += " idle";
        }

        _plugin.Graphics.DrawText(label, position - new Vector2(1f, 20f), PlacementFrameColor);
    }

    private void HandleDrag(ReAgentAuraDisplayEntry entry, Vector2 position, Vector2 size)
    {
        if (!Settings.Unlocked.Value)
        {
            _draggingRule = null;
            return;
        }

        var rule = entry.Rule;
        var runtimeId = RuntimeHelpers.GetHashCode(rule);
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

        if (ImGui.Begin($"##reagentauras_drag_surface_{runtimeId}", flags))
        {
            ImGui.InvisibleButton($"##reagentauras_drag_button_{runtimeId}", dragSize);
            var hovered = ImGui.IsItemHovered();
            var active = ImGui.IsItemActive();

            if (active)
            {
                _draggingRule = rule;
            }
            else if (ReferenceEquals(_draggingRule, rule) && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                _draggingRule = null;
            }

            var draggingThisRule = ReferenceEquals(_draggingRule, rule);
            if (hovered || draggingThisRule)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }

            if (active)
            {
                ApplyDragDelta(entry);
            }
        }

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private static void ApplyDragDelta(ReAgentAuraDisplayEntry entry)
    {
        var delta = ImGui.GetIO().MouseDelta;
        if (delta == Vector2.Zero)
        {
            return;
        }

        if (entry.Group.MoveTogether)
        {
            foreach (var rule in entry.Group.Rules
                         .Where(rule => rule.Kind == RuleKind.ReAgentAura && rule.ReAgentAura != null)
                         .Select(rule => rule.ReAgentAura))
            {
                ApplyDragDelta(rule, delta);
            }

            return;
        }

        ApplyDragDelta(entry.Rule, delta);
    }

    private static void ApplyDragDelta(ReAgentAuraRule rule, Vector2 delta)
    {
        rule.PositionX.Value = Math.Clamp((int)MathF.Round(rule.PositionX.Value + delta.X), rule.PositionX.Min, rule.PositionX.Max);
        rule.PositionY.Value = Math.Clamp((int)MathF.Round(rule.PositionY.Value + delta.Y), rule.PositionY.Min, rule.PositionY.Max);
    }

    private void DrawAuraIcon(ReAgentAuraDisplayEntry entry, Vector2 position, float iconSize)
    {
        var rule = entry.Rule;
        var iconRectSize = new Vector2(iconSize, iconSize);
        var color = rule.Visual == ReAgentAuraVisualSource.Color ? rule.Color : Color.FromArgb(180, 45, 45, 45);
        var iconTextureKey = "";
        var hasIconTexture = rule.Visual is ReAgentAuraVisualSource.Icon or ReAgentAuraVisualSource.ManualIcon &&
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
            DrawCenteredText(GetInitials(rule.Name), position, iconRectSize, Color.White);
        }
    }

    private bool TryDrawRegisteredIcon(string textureKey, Vector2 position, float iconSize)
    {
        _plugin.Graphics.DrawImage(textureKey, new ExileCore2.Shared.RectangleF(position.X, position.Y, iconSize, iconSize));
        return true;
    }

    private bool TryDrawSelectedFrame(ReAgentAuraRule rule, string iconTextureKey, Vector2 position, float iconSize)
    {
        if (!TryEnsureFrameRegistered(rule.Frame, out var frameTextureKey, out var layout))
        {
            return false;
        }

        var scale = iconSize / Math.Max(layout.Width, layout.Height);
        var innerSize = layout.Width * layout.InnerScale * scale;
        var innerX = position.X + ((layout.Width - layout.Width * layout.InnerScale) / 2f + layout.OffsetX) * scale;
        var innerY = position.Y + ((layout.Height - layout.Width * layout.InnerScale) / 2f + layout.OffsetY) * scale;

        var frameRect = new ExileCore2.Shared.RectangleF(position.X, position.Y, layout.Width * scale, layout.Height * scale);
        var iconRect = new ExileCore2.Shared.RectangleF(innerX, innerY, innerSize, innerSize);
        if (DrawsIconAboveFrame(rule.Frame))
        {
            _plugin.Graphics.DrawImage(frameTextureKey, frameRect);
            _plugin.Graphics.DrawImage(iconTextureKey, iconRect);
            return true;
        }

        _plugin.Graphics.DrawImage(iconTextureKey, iconRect);
        _plugin.Graphics.DrawImage(frameTextureKey, frameRect);
        return true;
    }

    private static bool DrawsIconAboveFrame(string frameName)
    {
        return string.Equals(frameName, "minionframe", StringComparison.Ordinal);
    }

    private bool TryEnsureRuleIconRegistered(ReAgentAuraRule rule, out string textureKey)
    {
        var iconPath = rule.Visual == ReAgentAuraVisualSource.ManualIcon ? rule.ManualIconPath : ResolveExtractedPngPath(rule);
        textureKey = rule.Visual == ReAgentAuraVisualSource.ManualIcon
            ? ReAgentAuraTextureKeys.ManualIcon(iconPath)
            : ReAgentAuraTextureKeys.Icon(iconPath);
        return !string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath) && TryEnsureImageRegistered(textureKey, iconPath);
    }

    private bool TryEnsureFrameRegistered(string frameName, out string textureKey, out ReAgentAuraFrameLayout layout)
    {
        textureKey = "";
        layout = default;

        if (string.IsNullOrWhiteSpace(frameName) ||
            string.Equals(frameName, ReAgentAuraFrames.None, StringComparison.Ordinal) ||
            !ReAgentAuraFrames.TryGetLayout(frameName, out layout))
        {
            return false;
        }

        textureKey = ReAgentAuraTextureKeys.Frame(frameName);
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

    private void DrawTextDisplays(ReAgentAuraDisplayEntry entry, Vector2 iconPosition, float iconSize)
    {
        foreach (var runtimeDisplay in entry.Displays.Where(display => display.Enabled))
        {
            var text = ResolveDisplayText(runtimeDisplay);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            using (_plugin.Graphics.SetTextScale(runtimeDisplay.Display.TextScale.Value))
            {
                var textSize = _plugin.Graphics.MeasureText(text);
                var textPosition = ResolveDisplayTextPosition(runtimeDisplay.Display, iconPosition, iconSize, textSize);
                DrawTextWithBackground(text, textPosition, runtimeDisplay.Display.TextColor, Color.FromArgb(190, 0, 0, 0));
            }
        }
    }

    private static string ResolveDisplayText(ReAgentAuraDisplayRuntime display)
    {
        if (display.Display.Effect == ReAgentAuraDisplayEffect.ShowCustomText)
        {
            return display.Text;
        }

        return display.Value;
    }

    private static Vector2 ResolveDisplayTextPosition(ReAgentAuraDisplay display, Vector2 iconPosition, float iconSize, Vector2 textSize)
    {
        var position = display.StartPosition switch
        {
            ReAgentAuraStartPosition.Top => new Vector2(iconPosition.X + (iconSize - textSize.X) / 2f, iconPosition.Y - textSize.Y),
            ReAgentAuraStartPosition.Left => new Vector2(iconPosition.X - textSize.X, iconPosition.Y + (iconSize - textSize.Y) / 2f),
            ReAgentAuraStartPosition.Right => new Vector2(iconPosition.X + iconSize, iconPosition.Y + (iconSize - textSize.Y) / 2f),
            ReAgentAuraStartPosition.Center => new Vector2(iconPosition.X + (iconSize - textSize.X) / 2f, iconPosition.Y + (iconSize - textSize.Y) / 2f),
            _ => new Vector2(iconPosition.X + (iconSize - textSize.X) / 2f, iconPosition.Y + iconSize - textSize.Y)
        };

        return position + new Vector2(display.OffsetX.Value, display.OffsetY.Value);
    }

    private void DrawTextWithBackground(string text, Vector2 position, Color textColor, Color backgroundColor)
    {
        var textSize = _plugin.Graphics.MeasureText(text);
        _plugin.Graphics.DrawBox(position, position + textSize, backgroundColor);
        _plugin.Graphics.DrawText(text, position, textColor);
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

    private static string GetInitials(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "?";
        }

        var initials = string.Concat(text
            .Split([' ', '_', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(x => char.ToUpperInvariant(x[0])));
        return string.IsNullOrWhiteSpace(initials) ? "?" : initials;
    }
}
