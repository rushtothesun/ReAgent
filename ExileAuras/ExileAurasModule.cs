using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using ExileCore2;
using ExileCore2.Shared.Helpers;
using ImGuiNET;

namespace ReAgent.ExileAuras;

public sealed partial class ExileAurasModule
{
    private const long StatusVisibleMs = 7000;

    private static readonly string[] FrameOptions =
    [
        "None",
        "buff",
        "charges",
        "debuff",
        "minionframe",
        "nopausebuffframe"
    ];

    private static readonly AssetExtractionRequest[] PluginAssetRequests =
    [
        new("art/textures/interface/2d/2dart/uiimages/ingame/4k/buff.dds", "buff.png"),
        new("art/textures/interface/2d/2dart/uiimages/ingame/4k/charges.dds", "charges.png"),
        new("art/textures/interface/2d/2dart/uiimages/ingame/4k/debuff.dds", "debuff.png"),
        new("art/textures/interface/2d/2dart/uiimages/ingame/4k/minionframe.dds", "minionframe.png"),
        new("art/textures/interface/2d/2dart/uiimages/ingame/4k/nopausebuffframe.dds", "nopausebuffframe.png")
    ];

    private static readonly Dictionary<string, ExileAuraFrameLayout> FrameLayouts = new(StringComparer.Ordinal)
    {
        ["buff"] = new("buff.png", 132f, 132f, 0.625f, 0f, -4f),
        ["charges"] = new("charges.png", 132f, 132f, 0.625f, 0f, -4f),
        ["debuff"] = new("debuff.png", 132f, 132f, 0.625f, 0f, -4f),
        ["nopausebuffframe"] = new("nopausebuffframe.png", 132f, 132f, 0.625f, 0f, -4f),
        ["minionframe"] = new("minionframe.png", 136f, 132f, 0.625f, 0f, -4f)
    };

    private readonly ReAgent _plugin;
    private readonly ExileAuraIconCache _iconCache = new();
    private readonly ExileAuraConditionCompiler _conditionCompiler = new();
    private readonly HashSet<string> _registeredTextureKeys = new(StringComparer.Ordinal);

    private long _nextPollMs;
    private bool _assetExtractionRunning;
    private bool _autoDetectAttempted;
    private string _assetExtractionStatus = "";
    private long _assetExtractionStatusExpiresAtMs;

    internal ExileAurasModule(ReAgent plugin)
    {
        _plugin = plugin;
    }

    private ExileAurasSettings Settings => _plugin.Settings.ExileAuras;
    private GameController GameController => _plugin.GameController;

    internal void Initialise()
    {
        EnsureContentGgpkAutoDetected();
    }

    internal void DrawSettings()
    {
        if (!ImGui.TreeNodeEx("ExileAuras"))
        {
            return;
        }

        ImGui.TextUnformatted(Settings.Unlocked.Value ? "Unlocked" : "Locked");
        ImGui.SameLine();
        if (ImGui.Button(Settings.Unlocked.Value ? "Lock" : "Unlock"))
        {
            Settings.Unlocked.Value = !Settings.Unlocked.Value;
        }

        DrawPollInterval();
        DrawExtractionSettings();
        ImGui.TreePop();
    }

    private void DrawPollInterval()
    {
        ImGui.PushItemWidth(170);
        var pollInterval = Settings.PollIntervalMs.Value;
        if (ImGui.SliderInt("Poll Interval Ms", ref pollInterval, Settings.PollIntervalMs.Min, Settings.PollIntervalMs.Max))
        {
            Settings.PollIntervalMs.Value = pollInterval;
        }
        ImGui.PopItemWidth();
    }

    private void DrawExtractionSettings()
    {
        var enableExtraction = Settings.EnableExtraction.Value;
        if (ImGui.Checkbox("Enable Extraction", ref enableExtraction))
        {
            Settings.EnableExtraction.Value = enableExtraction;
        }

        if (!Settings.EnableExtraction.Value)
        {
            return;
        }

        ImGui.SameLine(0f, 34f);
        var extractionWasRunning = _assetExtractionRunning;
        if (extractionWasRunning)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Extract Frames"))
        {
            QueuePluginAssetExtraction();
        }

        if (extractionWasRunning)
        {
            ImGui.EndDisabled();
        }

        if (IsAssetExtractionStatusVisible() && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(_assetExtractionStatus);
        }

        ImGui.SameLine(0f, 34f);
        if (ImGui.Button("Auto-Detect GGPK"))
        {
            Settings.ContentGgpkPath.Value = ForceAutoDetectContentGgpkPath();
        }

        ImGui.PushItemWidth(640);
        var contentGgpkPath = Settings.ContentGgpkPath.Value ?? string.Empty;
        if (ImGui.InputText("Content.ggpk Path", ref contentGgpkPath, 512))
        {
            Settings.ContentGgpkPath.Value = contentGgpkPath;
        }
        ImGui.PopItemWidth();

        if (IsAssetExtractionStatusVisible())
        {
            ImGui.TextColored(Color.LightGreen.ToImguiVec4(), _assetExtractionStatus);
        }
    }

    private void EnsureContentGgpkAutoDetected()
    {
        if (!string.IsNullOrWhiteSpace(Settings.ContentGgpkPath.Value) || _autoDetectAttempted)
        {
            return;
        }

        _autoDetectAttempted = true;
        var detectedPath = TryAutoDetectContentGgpkPath();
        if (!string.IsNullOrWhiteSpace(detectedPath))
        {
            Settings.ContentGgpkPath.Value = detectedPath;
        }
    }

    private string ForceAutoDetectContentGgpkPath()
    {
        _autoDetectAttempted = true;
        return TryAutoDetectContentGgpkPath() ?? string.Empty;
    }

    private void QueuePluginAssetExtraction()
    {
        if (_assetExtractionRunning)
        {
            return;
        }

        _assetExtractionRunning = true;
        SetAssetExtractionStatus("Extracting frames...");
        var contentGgpkPath = ResolveContentGgpkPath();
        var framesDirectory = ResolveFramesDirectory();

        Task.Run(() =>
        {
            try
            {
                var result = ExileAuraIconCache.ExtractAssets(PluginAssetRequests, contentGgpkPath, framesDirectory);
                SetAssetExtractionStatus(result.Message);
            }
            catch (Exception ex)
            {
                SetAssetExtractionStatus(ex.Message);
            }
            finally
            {
                _assetExtractionRunning = false;
            }
        });
    }

    private ExileAuraIconCacheEntry ExtractRuleIcon(string ddsFile)
    {
        return _iconCache.Queue(ddsFile, ResolveContentGgpkPath(), ResolveIconCacheDirectory());
    }

    private void ExtractOrRegisterRuleIcon(ExileAuraRule rule, string ddsFile)
    {
        var normalizedPath = ExileAuraIconCache.NormalizeDdsPath(ddsFile);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            SetIconStatus(rule, "No icon DDS path is available.");
            return;
        }

        var textureKey = CreateTextureKey(rule);
        var (_, pngOutputPath) = ExileAuraIconCache.GetSafeOutputPaths(ResolveIconCacheDirectory(), normalizedPath);
        rule.ExtractedPngPath = pngOutputPath;
        rule.IconTextureKey = textureKey;

        if (File.Exists(pngOutputPath))
        {
            if (IsTextureRegistered(textureKey))
            {
                SetIconStatus(rule, "Icon already registered; skipped extraction.");
                return;
            }

            SetIconStatus(rule, TryEnsureImageRegistered(textureKey, pngOutputPath)
                ? "Icon already existed; registered it."
                : "Icon already existed but could not be registered.");
            return;
        }

        var entry = ExtractRuleIcon(ddsFile);
        rule.ExtractedPngPath = entry.PngOutputPath;
        rule.IconTextureKey = textureKey;
        SetIconStatus(rule, entry.State switch
        {
            ExileAuraIconCacheState.Queued => "Icon extraction queued.",
            ExileAuraIconCacheState.Extracting => "Icon extraction started.",
            ExileAuraIconCacheState.Ready => "Icon extracted.",
            ExileAuraIconCacheState.MissingPath => "Icon DDS was not found in Content.ggpk.",
            ExileAuraIconCacheState.Failed => $"Icon extraction failed: {entry.Error}",
            _ => "Icon extraction skipped."
        });
    }

    private void RefreshPendingIconStatus(ExileAuraRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.ExtractedPngPath) ||
            !File.Exists(rule.ExtractedPngPath) ||
            !IsPendingIconStatus(rule.IconStatus))
        {
            return;
        }

        var textureKey = string.IsNullOrWhiteSpace(rule.IconTextureKey) ? CreateTextureKey(rule) : rule.IconTextureKey;
        rule.IconTextureKey = textureKey;
        SetIconStatus(rule, TryEnsureImageRegistered(textureKey, rule.ExtractedPngPath)
            ? "Icon extracted and registered."
            : "Icon extracted but could not be registered.");
    }

    private static bool IsPendingIconStatus(string status)
    {
        return string.Equals(status, "Icon extraction queued.", StringComparison.Ordinal) ||
               string.Equals(status, "Icon extraction started.", StringComparison.Ordinal);
    }

    private bool IsAssetExtractionStatusVisible()
    {
        return !string.IsNullOrWhiteSpace(_assetExtractionStatus) &&
               (_assetExtractionRunning || Environment.TickCount64 < _assetExtractionStatusExpiresAtMs);
    }

    private void SetAssetExtractionStatus(string status)
    {
        _assetExtractionStatus = status;
        _assetExtractionStatusExpiresAtMs = Environment.TickCount64 + StatusVisibleMs;
    }

    private static void SetIconStatus(ExileAuraRule rule, string status)
    {
        rule.IconStatus = status;
        rule.IconStatusExpiresAtMs = Environment.TickCount64 + StatusVisibleMs;
    }

    private string ResolveIconCacheDirectory()
    {
        return Path.Combine(_plugin.ConfigDirectory, "ExileAuras", "Icons");
    }

    private string ResolveFramesDirectory()
    {
        return Path.Combine(_plugin.ConfigDirectory, "ExileAuras", "Frames");
    }

    private string ResolveContentGgpkPath()
    {
        if (!string.IsNullOrWhiteSpace(Settings.ContentGgpkPath.Value))
        {
            return Path.GetFullPath(Settings.ContentGgpkPath.Value);
        }

        if (_autoDetectAttempted)
        {
            return string.Empty;
        }

        _autoDetectAttempted = true;
        var detectedPath = TryAutoDetectContentGgpkPath();
        if (!string.IsNullOrWhiteSpace(detectedPath))
        {
            Settings.ContentGgpkPath.Value = detectedPath;
            return detectedPath;
        }

        return string.Empty;
    }

    private static string TryAutoDetectContentGgpkPath()
    {
        foreach (var processName in new[] { "PathOfExile", "PathOfExile_x64", "PathOfExileSteam", "PathOfExile2" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    var exePath = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(exePath))
                    {
                        continue;
                    }

                    var candidate = Path.Combine(Path.GetDirectoryName(exePath)!, "Content.ggpk");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                }
            }
        }

        return null;
    }
}
