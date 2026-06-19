using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using ExileCore2;
using ExileCore2.Shared.Helpers;
using ImGuiNET;
using Newtonsoft.Json;
using ReAgent.ExileAuras;
using ReAgent.SideEffects;
using ReAgent.State;

namespace ReAgent;

public class RuleGroup
{
    private bool _expand;
    private int _deleteIndex = -1;
    private readonly GroupConditionCompiler _groupConditionCompiler = new();

    public List<Rule> Rules = new();
    public RuleKind Kind = RuleKind.ReAgent;
    public bool Enabled;
    public bool EnabledInTown;
    public bool EnabledInHideout;
    public bool EnabledInPeacefulAreas;
    public bool EnabledInMaps = true;
    public bool MoveTogether;
    public bool UseGroupCondition;
    public string GroupConditionName = "Group Condition";
    public string GroupConditionSource = "return true;";
    public string Name;

    public RuleGroup(string name, RuleKind kind = RuleKind.ReAgent)
    {
        Name = name;
        Kind = kind;
    }

    [JsonIgnore]
    public RuleKind EffectiveKind => Kind == RuleKind.ExileAura || Rules.Any(rule => rule.Kind == RuleKind.ExileAura)
        ? RuleKind.ExileAura
        : RuleKind.ReAgent;

    public void DrawSettings(RuleState state, ReAgentSettings settings, ExileAurasModule exileAuras)
    {
        using (settings.PluginSettings.ColorEnableToggles ? ImGuiHelpers.UseStyleColor(ImGuiCol.Text, Color.Lime.ToImguiVec4()) : null)
            ImGui.Checkbox("Enable", ref Enabled);
        if (settings.PluginSettings.KeepEnableTogglesOnASingleLine)
        {
            ImGui.SameLine();
        }

        using (settings.PluginSettings.ColorEnableToggles ? ImGuiHelpers.UseStyleColor(ImGuiCol.Text, Color.LightBlue.ToImguiVec4()) : null)
            ImGui.Checkbox("Enable in town", ref EnabledInTown);
        if (settings.PluginSettings.KeepEnableTogglesOnASingleLine)
        {
            ImGui.SameLine();
        }

        using (settings.PluginSettings.ColorEnableToggles ? ImGuiHelpers.UseStyleColor(ImGuiCol.Text, Color.Salmon.ToImguiVec4()) : null)
            ImGui.Checkbox("Enable in hideout", ref EnabledInHideout);
        if (settings.PluginSettings.KeepEnableTogglesOnASingleLine)
        {
            ImGui.SameLine();
        }

        using (settings.PluginSettings.ColorEnableToggles ? ImGuiHelpers.UseStyleColor(ImGuiCol.Text, Color.Orange.ToImguiVec4()) : null)
            ImGui.Checkbox("Enable in maps", ref EnabledInMaps);
        if (settings.PluginSettings.KeepEnableTogglesOnASingleLine)
        {
            ImGui.SameLine();
        }

        using (settings.PluginSettings.ColorEnableToggles ? ImGuiHelpers.UseStyleColor(ImGuiCol.Text, Color.LightGoldenrodYellow.ToImguiVec4()) : null)
            ImGui.Checkbox("Enable in other peaceful areas", ref EnabledInPeacefulAreas);
        ImGui.InputText("Name", ref Name, 20);

        if (Rules.Any())
        {
            using (_expand ? null : ImGuiHelpers.UseStyleColor(ImGuiCol.Button, Color.Green.ToImgui()))
                if (ImGui.Button($"{(_expand ? "Collapse" : "Expand")}###ExpandHideButton"))
                {
                    _expand = !_expand;
                }

            ImGui.SameLine();
        }

        if (ImGui.Button("Export group"))
        {
            ImGui.SetClipboardText(DataExporter.ExportDataBase64(this, "reagent_group_v1", new JsonSerializerSettings()));
        }

        if (EffectiveKind == RuleKind.ExileAura)
        {
            ImGui.SameLine();
            ImGui.Checkbox("Move Together", ref MoveTogether);
        }

        DrawGroupCondition(state);

        using var groupReg = state?.InternalState.SetCurrentGroup(this);
        for (var i = 0; i < Rules.Count; i++)
        {
            ImGui.PushID($"Rule{i}");
            if (i != 0)
            {
                ImGui.Separator();
            }

            var dropTargetStart = ImGui.GetCursorPos();
            ImGui.PushStyleColor(ImGuiCol.Button, 0);
            ImGui.Button("=");
            ImGui.PopStyleColor();
            ImGui.SameLine();

            if (ImGui.BeginDragDropSource())
            {
                ImguiExt.SetDragDropPayload("RuleIndex", i);
                Rules[i].Display(state, false, exileAuras);
                ImGui.EndDragDropSource();
            }
            else if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Drag me");
            }

            if (ImGui.Button("Delete"))
            {
                if (ImGui.IsKeyDown(ImGuiKey.ModShift))
                {
                    RemoveAt(i);
                    ImGui.PopID();
                    break;
                }

                _deleteIndex = i;
            }
            else if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Hold Shift to delete without confirmation");
            }

            ImGui.SameLine();
            Rules[i].Display(state, _expand, exileAuras);
            ImguiExt.DrawLargeTransparentSelectable("##DragTarget", dropTargetStart);
            if (ImGui.BeginDragDropTarget())
            {
                var sourceId = ImguiExt.AcceptDragDropPayload<int>("RuleIndex");
                if (sourceId != null)
                {
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        MoveRule(sourceId.Value, i);
                    }
                }

                ImGui.EndDragDropTarget();
            }

            ImGui.PopID();
        }

        if (ImGui.Button("Add New Rule"))
        {
            Rules.Add(CreateRule());
        }

        if (_deleteIndex != -1)
        {
            ImGui.OpenPopup("RuleDeleteConfirmation");
        }

        var deleteResult = ImguiExt.DrawDeleteConfirmationPopup("RuleDeleteConfirmation", _deleteIndex == -1 ? null : $"rule with index {_deleteIndex}");
        if (deleteResult == true)
        {
            RemoveAt(_deleteIndex);
        }

        if (deleteResult != null)
        {
            _deleteIndex = -1;
        }
    }

    public bool ShouldEvaluate(RuleState state)
    {
        return ShouldEvaluateArea(state) && ShouldEvaluateGroupCondition(state).Active;
    }

    public bool ShouldEvaluateArea(RuleState state)
    {
        return Enabled &&
               (state.IsInHideout, state.IsInTown, state.IsInPeacefulArea) switch
               {
                   (true, _, _) => EnabledInHideout,
                   (_, true, _) => EnabledInTown,
                   (_, _, true) => EnabledInPeacefulAreas,
                   (false, false, false) => EnabledInMaps,
               };
    }

    public IEnumerable<SideEffectContainer> Evaluate(RuleState state)
    {
        if (ShouldEvaluate(state))
        {
            using var groupReg = state.InternalState.SetCurrentGroup(this);
            foreach (var rule in Rules)
            {
                using var ruleReg = state.InternalState.CurrentGroupState.SetCurrentRule(rule);
                foreach (var effect in rule.Evaluate(state))
                {
                    yield return new SideEffectContainer(effect, this, rule);
                }
            }
        }
    }

    private void RemoveAt(int index)
    {
        Rules.RemoveAt(index);
    }

    private void MoveRule(int sourceIndex, int targetIndex)
    {
        var movedItem = Rules[sourceIndex];
        Rules.RemoveAt(sourceIndex);
        Rules.Insert(targetIndex, movedItem);
    }

    private void DrawGroupCondition(RuleState state)
    {
        if (ImGui.Checkbox("Use Group Condition", ref UseGroupCondition) &&
            string.IsNullOrWhiteSpace(GroupConditionSource))
        {
            GroupConditionSource = "return true;";
        }

        if (!UseGroupCondition)
        {
            return;
        }

        ImGui.PushItemWidth(260);
        ImGui.InputText("Condition Name", ref GroupConditionName, 120);
        ImGui.PopItemWidth();

        var source = GroupConditionSource ?? string.Empty;
        if (ImGui.InputTextMultiline(
                "##groupConditionSource",
                ref source,
                10000,
                new Vector2(ImGui.GetContentRegionAvail().X, ImGui.CalcTextSize($"^{source}_").Y + ImGui.GetTextLineHeight())))
        {
            GroupConditionSource = source;
        }

        var result = ShouldEvaluateGroupCondition(state);
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            ImGui.TextColored(Color.Red.ToImguiVec4(), result.Error);
            return;
        }

        ImGui.TextColored((result.Active ? Color.Lime : Color.Yellow).ToImguiVec4(), result.Active ? "Group condition is true." : "Group condition is false.");
    }

    private GroupConditionEvaluation ShouldEvaluateGroupCondition(RuleState state)
    {
        if (!UseGroupCondition)
        {
            return new GroupConditionEvaluation(true, "");
        }

        return _groupConditionCompiler.Evaluate(GroupConditionSource, state);
    }

    private Rule CreateRule()
    {
        return EffectiveKind switch
        {
            RuleKind.ExileAura => Rule.CreateExileAura(),
            _ => new Rule("false", 1)
        };
    }
}
