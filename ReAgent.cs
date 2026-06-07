using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore2;
using ExileCore2.PoEMemory.Components;
using ExileCore2.Shared.Helpers;
using ExileCore2.Shared.Nodes;
using ImGuiNET;
using Newtonsoft.Json;
using ReAgent.SideEffects;
using ReAgent.State;
using RectangleF = ExileCore2.Shared.RectangleF;

namespace ReAgent;

public sealed class ReAgent : BaseSettingsPlugin<ReAgentSettings>
{
    private readonly Queue<(DateTime Date, string Description)> _actionInfo = new();
    private readonly Stopwatch _sinceLastKeyPress = Stopwatch.StartNew();
    private readonly RuleInternalState _internalState = new RuleInternalState();
    private readonly ConditionalWeakTable<Profile, string> _pendingNames = new ConditionalWeakTable<Profile, string>();
    private readonly HashSet<string> _loadedTextures = new();
    private RuleState _state;
    private List<SideEffectContainer> _pendingSideEffects = new List<SideEffectContainer>();
    private string _profileToDelete = null;
    public Dictionary<string, List<string>> CustomAilments { get; set; } = new Dictionary<string, List<string>>();
    public static int ProcessID { get; private set; }

    public override bool Initialise()
    {
        ProcessID = GameController.Window.Process.Id;

        var stringData = File.ReadAllText(Path.Join(DirectoryFullName, "CustomAilments.json"));
        CustomAilments = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(stringData);
        Settings.DumpState.OnPressed = () => { ImGui.SetClipboardText(JsonConvert.SerializeObject(new RuleState(this, _internalState), new JsonSerializerSettings
        {
            Error = (sender, args) =>
            {
                DebugWindow.LogError($"Error during state dump {args.ErrorContext.Error}");
                args.ErrorContext.Handled = true;
            }
        })); };
        Settings.ImageDirectory.OnValueChanged = () =>
        {
            foreach (var loadedTexture in _loadedTextures)
            {
                Graphics.DisposeTexture(loadedTexture);
            }

            _loadedTextures.Clear();
        };
        return base.Initialise();
    }

    private string _profileImportInput = null;
    private Task<(string text, bool edited)> _profileImportObject = null;

    private void DrawProfileImport()
    {
        var windowVisible = _profileImportInput != null;
        if (windowVisible)
        {
            if (ImGui.Begin("Import reagent profile", ref windowVisible))
            {
                if (_profileImportObject is { IsCompleted: false })
                {
                    ImGui.Text("Checking...");
                }

                if (_profileImportObject is { IsFaulted: true })
                {
                    ImGui.Text($"Check failed: {string.Join("\n", _profileImportObject.Exception.InnerExceptions)}");
                }

                if (ImGui.InputText("Exported code", ref _profileImportInput, 20000))
                {
                    _profileImportObject = Task.Run(() =>
                    {
                        var data = DataExporter.ImportDataBase64(_profileImportInput, "reagent_profile_v1");
                        data.ToObject<Profile>();
                        return (data.ToString(), false);
                    });
                }

                if (_profileImportObject is { IsCompletedSuccessfully: true })
                {
                    if (_profileImportObject.Result.edited)
                    {
                        ImGui.TextColored(Color.Green.ToImguiVec4(), "Editing manually");
                    }

                    var text = _profileImportObject.Result.text;
                    if (ImGui.InputTextMultiline("Json", ref text, 20000,
                            new Vector2(ImGui.GetContentRegionAvail().X, Math.Max(ImGui.GetContentRegionAvail().Y - 50, 50)), ImGuiInputTextFlags.ReadOnly))
                    {
                        _profileImportObject = Task.FromResult((text, true));
                    }
                }

                ImGui.BeginDisabled(_profileImportObject is not { IsCompletedSuccessfully: true });
                if (ImGui.Button("Import"))
                {
                    var profileName = GetNewProfileName("Imported profile ");
                    var profile = JsonConvert.DeserializeObject<Profile>(_profileImportObject.Result.text);
                    if (profile == null)
                    {
                        throw new Exception($"Profile deserialized to a null object, was '{_profileImportObject.Result.text}'");
                    }
                    Settings.Profiles.Add(profileName, profile);
                    windowVisible = false;
                }

                ImGui.EndDisabled();
                ImGui.End();
            }

            if (!windowVisible)
            {
                _profileImportInput = null;
                _profileImportObject = null;
            }
        }
    }

    public override void DrawSettings()
    {
        base.DrawSettings();
        DrawProfileImport();

        try
        {
            _state = new RuleState(this, _internalState);
        }
        catch (Exception ex)
        {
            LogError(ex.ToString());
        }

        if (!ShouldExecute(out var state))
        {
            ImGui.TextColored(Color.Red.ToImguiVec4(), $"Actions paused: {state}");
        }
        else
        {
            ImGui.Text("");
        }

        if (ImGui.BeginTabBar("Profiles", ImGuiTabBarFlags.AutoSelectNewTabs | ImGuiTabBarFlags.FittingPolicyScroll | ImGuiTabBarFlags.Reorderable))
        {
            if (ImGui.TabItemButton("+##addProfile", ImGuiTabItemFlags.Trailing))
            {
                var profileName = GetNewProfileName("New profile ");
                Settings.Profiles.Add(profileName, Profile.CreateWithDefaultGroup());
            }

            if (ImGui.TabItemButton("Import profile##import", ImGuiTabItemFlags.Trailing))
            {
                _profileImportInput = "";
                _profileImportObject = null;
            }

            foreach (var (profileName, profile) in Settings.Profiles.OrderByDescending(x => x.Key == Settings.CurrentProfile).ThenBy(x => x.Key).ToList())
            {
                if (profile == null)
                {
                    DebugWindow.LogError($"Profile {profileName} is null, creating default");
                    Settings.Profiles[profileName] = Profile.CreateWithDefaultGroup();
                    continue;
                }

                var preserveItem = true;
                var isCurrentProfile = profileName == Settings.CurrentProfile;
                if (isCurrentProfile)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, Color.LightGreen.ToImgui());
                }

                var tabSelected = ImGui.BeginTabItem($"{profileName}###{profile.TemporaryId}", ref preserveItem, ImGuiTabItemFlags.UnsavedDocument);
                if (isCurrentProfile)
                {
                    ImGui.PopStyleColor();
                }

                if (tabSelected)
                {
                    try
                    {
                        var availableSpace = ImGui.GetContentRegionAvail();
                        if (availableSpace.X < 100 || availableSpace.Y < 50)
                        {
                            ImGui.TextColored(Color.Yellow.ToImguiVec4(), "Window too small - expand to view settings");
                            ImGui.EndTabItem();
                        }
                        else
                        {
                            _pendingNames.TryGetValue(profile, out var newProfileName);
                            newProfileName ??= profileName;
                            ImGui.InputText("Name", ref newProfileName, 40);
                            if (!isCurrentProfile)
                            {
                                using (ImGuiHelpers.UseStyleColor(ImGuiCol.Button, Color.Green.ToImgui()))
                                    if (ImGui.Button("Activate"))
                                    {
                                        Settings.CurrentProfile = profileName;
                                    }

                                ImGui.SameLine();
                            }

                            if (ImGui.Button("Export profile"))
                            {
                                ImGui.SetClipboardText(DataExporter.ExportDataBase64(profile, "reagent_profile_v1", new JsonSerializerSettings()));
                            }

                            if (profileName != newProfileName)
                            {
                                if (Settings.Profiles.ContainsKey(newProfileName))
                                {
                                    ImGui.SameLine();
                                    ImGui.TextColored(Color.Red.ToImguiVec4(), "This profile name is already used");
                                    _pendingNames.AddOrUpdate(profile, newProfileName);
                                }
                                else
                                {
                                    Settings.Profiles.Remove(profileName);
                                    Settings.Profiles.Add(newProfileName, profile);
                                    if (isCurrentProfile)
                                    {
                                        Settings.CurrentProfile = newProfileName;
                                    }

                                    _pendingNames.Clear();
                                }
                            }

                            profile.DrawSettings(_state, Settings);
                            ImGui.EndTabItem();
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Error rendering profile settings: {ex.Message}");
                        ImGui.TextColored(Color.Red.ToImguiVec4(), "Error rendering - check logs");
                        ImGui.EndTabItem();
                    }
                }
                else
                {
                    profile.FocusLost();
                }

                if (!preserveItem)
                {
                    if (ImGui.IsKeyDown(ImGuiKey.ModShift))
                    {
                        Settings.Profiles.Remove(profileName);
                    }
                    else
                    {
                        _profileToDelete = profileName;
                        ImGui.OpenPopup("ProfileDeleteConfirmation");
                    }
                }
            }

            var deleteResult = ImguiExt.DrawDeleteConfirmationPopup("ProfileDeleteConfirmation", $"profile {_profileToDelete}");
            if (deleteResult == true)
            {
                Settings.Profiles.Remove(_profileToDelete);
            }

            if (deleteResult != null)
            {
                _profileToDelete = null;
            }

            ImGui.EndTabBar();
        }
    }

    private string GetNewProfileName(string prefix)
    {
        return Enumerable.Range(1, 10000).Select(i => $"{prefix}{i}").Except(Settings.Profiles.Keys).First();
    }

    public override void Render()
    {
        if (Settings.Profiles.Count == 0)
        {
            Settings.Profiles.Add(GetNewProfileName("New profile "), Profile.CreateWithDefaultGroup());
            Settings.CurrentProfile = Settings.Profiles.Keys.Single();
        }

        if (string.IsNullOrEmpty(Settings.CurrentProfile) || !Settings.Profiles.TryGetValue(Settings.CurrentProfile, out var profile))
        {
            Settings.CurrentProfile = Settings.Profiles.Keys.First();
            profile = Settings.Profiles[Settings.CurrentProfile];
        }

        var shouldExecute = ShouldExecute(out var state);
        while (_actionInfo.TryPeek(out var entry) && (DateTime.Now - entry.Date).TotalSeconds > Settings.HistorySecondsToKeep)
        {
            _actionInfo.Dequeue();
        }

        if (Settings.ShowDebugWindow)
        {
            var show = Settings.ShowDebugWindow.Value;
            ImGui.Begin("Debug Mode Window", ref show);
            Settings.ShowDebugWindow.Value = show;
            ImGui.TextWrapped($"State: {state}");
            if (ImGui.Button("Clear History"))
            {
                _actionInfo.Clear();
            }

            ImGui.BeginChild("KeyPressesInfo");
            foreach (var (dateTime, @event) in _actionInfo.Reverse())
            {
                ImGui.TextUnformatted($"{dateTime:HH:mm:ss.fff}: {@event}");
            }

            ImGui.EndChild();
            ImGui.End();
        }

        if (!shouldExecute && !Settings.InspectState && !Settings.ShowControllerState)
        {
            return;
        }

        _internalState.KeyToPress = null;
        _internalState.KeysToHoldDown.Clear();
        _internalState.KeysToRelease.Clear();
        _internalState.TextToDisplay.Clear();
        _internalState.GraphicToDisplay.Clear();
        _internalState.PluginBridgeMethodsToCall.Clear();
        _internalState.ProgressBarsToDisplay.Clear();
        _internalState.ChatTitlePanelVisible = GameController.IngameState.IngameUi.ChatTitlePanel.IsVisible;
        _internalState.CanPressKey = _sinceLastKeyPress.ElapsedMilliseconds >= Settings.GlobalKeyPressCooldown && !_internalState.ChatTitlePanelVisible;
        _internalState.LeftPanelVisible = GameController.IngameState.IngameUi.OpenLeftPanel.IsVisible;
        _internalState.RightPanelVisible = GameController.IngameState.IngameUi.OpenRightPanel.IsVisible;
        _internalState.LargePanelVisible = GameController.IngameState.IngameUi.LargePanels.Any(p => p.IsVisible);
        _internalState.FullscreenPanelVisible = GameController.IngameState.IngameUi.FullscreenPanels.Any(p => p.IsVisible);
        _state = new RuleState(this, _internalState);

        if (Settings.InspectState)
        {
            GameController.InspectObject(_state, "ReAgent state");
        }

        if (Settings.ShowControllerState)
        {
            DrawControllerStateWindow(_state);
        }

        if (!shouldExecute)
        {
            return;
        }

        ApplyPendingSideEffects();

        foreach (var group in profile.Groups)
        {
            var newSideEffects = group.Evaluate(_state).ToList();
            foreach (var sideEffect in newSideEffects)
            {
                sideEffect.SetPending();
                _pendingSideEffects.Add(sideEffect);
            }
        }

        ApplyPendingSideEffects();

        foreach (var (methodName, invoker) in _internalState.PluginBridgeMethodsToCall)
        {
            try
            {
                if (GameController.PluginBridge.GetMethod<Delegate>(methodName) is { } method)
                {
                    invoker(method);
                }
                else
                {
                    LogError($"Plugin bridge method {methodName} was not found");
                }
            }
            catch (Exception ex)
            {
                LogError($"Plugin bridge {methodName} call error: {ex}");
            }
        }

        if (_internalState.KeyToPress is { } key)
        {
            _internalState.KeyToPress = null;
            InputHelper.SendInputPress(key);
            _sinceLastKeyPress.Restart();
        }

        foreach (var heldKey in _internalState.KeysToHoldDown)
        {
            if (heldKey?.Key == Keys.LButton)
            {
                Input.LeftDown();
            }
            else
            {
                InputHelper.SendInputDown(heldKey);
            }
        }


        foreach (var heldKey in _internalState.KeysToRelease)
        {
            if (heldKey?.Key == Keys.LButton)
            {
                Input.LeftUp();
            }
            else
            {
                InputHelper.SendInputUp(heldKey);
            }
        }

        foreach (var (text, position, size, fraction, color, backgroundColor, textColor) in _internalState.ProgressBarsToDisplay)
        {
            var textSize = Graphics.MeasureText(text);
            Graphics.DrawBox(position, position + size, ColorFromName(backgroundColor));
            Graphics.DrawBox(position, position + size with { X = size.X * fraction }, ColorFromName(color));
            Graphics.DrawText(text, position + size / 2 - textSize / 2, ColorFromName(textColor));
        }

        foreach (var (graphicFilePath, position, size, tintColor) in _internalState.GraphicToDisplay)
        {
            if (!_loadedTextures.Contains(graphicFilePath))
            {
                var graphicFileFullPath = Path.Combine(Path.GetDirectoryName(typeof(Core).Assembly.Location)!, Settings.ImageDirectory, graphicFilePath);
                if (File.Exists(graphicFileFullPath))
                {
                    if (Graphics.InitImage(graphicFilePath, graphicFileFullPath))
                    {
                        _loadedTextures.Add(graphicFilePath);
                    }
                }
            }

            if (_loadedTextures.Contains(graphicFilePath))
            {
                Graphics.DrawImage(graphicFilePath, new RectangleF(position.X, position.Y, size.X, size.Y), ColorFromName(tintColor));
            }
        }

        foreach (var (text, position, color) in _internalState.TextToDisplay)
        {
            var textSize = Graphics.MeasureText(text);
            Graphics.DrawBox(position, position + textSize, Color.Black);
            Graphics.DrawText(text, position, ColorFromName(color));
        }
    }

    private void DrawControllerStateWindow(RuleState state)
    {
        var show = Settings.ShowControllerState.Value;
        if (!ImGui.Begin("Controller State", ref show))
        {
            Settings.ShowControllerState.Value = show;
            ImGui.End();
            return;
        }

        Settings.ShowControllerState.Value = show;
        var controller = state.Controller;
        ImGui.TextUnformatted($"Connected: {controller.IsConnected}");

        ImGui.Separator();
        DrawControllerButtonRows(controller);

        ImGui.Separator();
        DrawControllerBindingRows("Primary Skill Bar", controller.Skills.PrimaryCells, "Primary");
        ImGui.Separator();
        DrawControllerBindingRows("Secondary Skill Bar", controller.Skills.SecondaryCells, "Secondary");

        ImGui.End();
    }

    private static void DrawControllerButtonRows(ControllerState controller)
    {
        if (ImGui.TreeNodeEx("Buttons", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var start = ImGui.GetCursorScreenPos();
            var lineHeight = ImGui.GetTextLineHeightWithSpacing();

            DrawControllerButtonAt(controller, HotkeyNodeV2.ControllerKey.LTrigger, $"LT {controller.LeftTriggerPressure}", start, 20, 0);
            DrawControllerButtonAt(controller, HotkeyNodeV2.ControllerKey.RTrigger, $"RT {controller.RightTriggerPressure}", start, 270, 0);

            DrawControllerButtonAt(controller, HotkeyNodeV2.ControllerKey.Lb, "LB", start, 20, lineHeight);
            DrawControllerButtonAt(controller, HotkeyNodeV2.ControllerKey.Rb, "RB", start, 270, lineHeight);

            DrawControllerButtonAt(controller, HotkeyNodeV2.ControllerKey.Back, "Back", start, 115, lineHeight * 2);
            DrawControllerButtonAt(controller, HotkeyNodeV2.ControllerKey.Start, "Start", start, 180, lineHeight * 2);

            DrawControllerButtonAt(controller, HotkeyNodeV2.ControllerKey.Up, "Up", start, 70, lineHeight * 3);
            DrawControllerButtonAt(controller, HotkeyNodeV2.ControllerKey.Left, "Left", start, 20, lineHeight * 4);
            DrawControllerButtonAt(controller, HotkeyNodeV2.ControllerKey.Right, "Right", start, 105, lineHeight * 4);
            DrawControllerButtonAt(controller, HotkeyNodeV2.ControllerKey.Down, "Down", start, 60, lineHeight * 5);

            DrawControllerButtonAt(controller, HotkeyNodeV2.ControllerKey.Y, "Y", start, 270, lineHeight * 3);
            DrawControllerButtonAt(controller, HotkeyNodeV2.ControllerKey.X, "X", start, 230, lineHeight * 4);
            DrawControllerButtonAt(controller, HotkeyNodeV2.ControllerKey.B, "B", start, 310, lineHeight * 4);
            DrawControllerButtonAt(controller, HotkeyNodeV2.ControllerKey.A, "A", start, 270, lineHeight * 5);

            DrawControllerButtonAt(controller, HotkeyNodeV2.ControllerKey.Ls, "LS", start, 60, lineHeight * 6);
            DrawControllerButtonAt(controller, HotkeyNodeV2.ControllerKey.Rs, "RS", start, 270, lineHeight * 6);

            ImGui.SetCursorScreenPos(start + new Vector2(0, lineHeight * 7));

            ImGui.TreePop();
        }
    }

    private static void DrawControllerButtonAt(ControllerState controller, HotkeyNodeV2.ControllerKey key, string label, Vector2 start, float x, float y)
    {
        var pressed = controller.IsPressed(key);
        ImGui.SetCursorScreenPos(start + new Vector2(x, y));
        ImGui.TextColored((pressed ? Color.LightGreen : Color.Gray).ToImguiVec4(), label);
    }

    private static void DrawControllerBindingRows(string label, IReadOnlyList<ControllerBindingCell> cells, string id)
    {
        if (ImGui.TreeNodeEx(label, ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.BeginTable($"ControllerState{id}Table", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Cell");
                ImGui.TableSetupColumn("Key");
                ImGui.TableSetupColumn("Texture");
                ImGui.TableSetupColumn("Copy");
                ImGui.TableHeadersRow();

                foreach (var cell in cells)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted(cell.Cell.ToString());
                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(cell.Key.ToString());
                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(string.IsNullOrWhiteSpace(cell.TextureFileName) ? "-" : cell.TextureFileName);
                    if (!string.IsNullOrWhiteSpace(cell.Texture) && ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(cell.Texture);
                    }

                    ImGui.TableSetColumnIndex(3);
                    var hasTexture = !string.IsNullOrWhiteSpace(cell.TextureFileName);
                    ImGui.BeginDisabled(!hasTexture);
                    if (ImGui.SmallButton($"Copy##ControllerState{id}{cell.Cell}{cell.TextureFileName}"))
                    {
                        ImGui.SetClipboardText(cell.TextureFileName);
                    }
                    ImGui.EndDisabled();
                }

                ImGui.EndTable();
            }

            ImGui.TreePop();
        }
    }

    private static Color ColorFromName(string color)
    {
        return Color.FromName(color);
    }

    private void ApplyPendingSideEffects()
    {
        var applicationResults = _pendingSideEffects.Select(x => (x, ApplicationResult: x.Apply(_state))).ToList();
        foreach (var successfulApplication in applicationResults.Where(x =>
                     x.ApplicationResult is SideEffectApplicationResult.AppliedUnique or SideEffectApplicationResult.AppliedDuplicate))
        {
            successfulApplication.x.SetExecuted(_state);
            if (successfulApplication.ApplicationResult == SideEffectApplicationResult.AppliedUnique)
            {
                _actionInfo.Enqueue((DateTime.Now, successfulApplication.x.SideEffect.ToString()));
            }
        }

        _pendingSideEffects = applicationResults.Where(x => x.ApplicationResult == SideEffectApplicationResult.UnableToApply).Select(x => x.x).ToList();
    }


    private bool ShouldExecute(out string state)
    {
        if (!GameController.Window.IsForeground())
        {
            state = "Game window is not focused";
            return false;
        }

        if (!Settings.PluginSettings.EnableInEscapeState && 
            GameController.Game.IsEscapeState)
        {
            state = "Escape state is active";
            return false;
        }

        if (GameController.Player.TryGetComponent<Life>(out var lifeComp))
        {
            if (lifeComp.CurHP <= 0)
            {
                state = "Player is dead";
                return false;
            }
        }
        else
        {
            state = "Cannot find player Life component";
            return false;
        }

        if (GameController.Player.TryGetComponent<Buffs>(out var buffComp))
        {
            if (!Settings.PluginSettings.IgnoreGracePeriod &&
                buffComp.HasBuff("grace_period"))
            {
                state = "Grace period is active";
                return false;
            }
        }
        else
        {
            state = "Cannot find player Buffs component";
            return false;
        }

        if (!GameController.Player.HasComponent<Actor>())
        {
            state = "Cannot find player Actor component";
            return false;
        }

        state = "Ready";
        return true;
    }
}
