using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using ExileCore2;
using ExileCore2.Shared.Helpers;
using ImGuiNET;

namespace ReAgent.ReAgentAuras;

public sealed partial class ReAgentAurasModule
{
    private const long StatusVisibleMs = 7000;

    private readonly ReAgent _plugin;
    private readonly ReAgentAuraIconCache _iconCache = new();
    private readonly ReAgentAuraConditionCompiler _conditionCompiler = new();
    private readonly HashSet<string> _registeredTextureKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReAgentAuraIconStatus> _iconStatuses = new(StringComparer.Ordinal);
    private readonly object _assetExtractionSync = new();

    private long _nextPollMs;
    private bool _assetExtractionRunning;
    private bool _autoDetectAttempted;
    private string _assetExtractionStatus = "";
    private long _assetExtractionStatusExpiresAtMs;

    internal ReAgentAurasModule(ReAgent plugin)
    {
        _plugin = plugin;
    }

    private ReAgentAurasSettings Settings => _plugin.Settings.ReAgentAuras;
    private GameController GameController => _plugin.GameController;

    internal void Initialise()
    {
        EnsureContentGgpkAutoDetected();
    }

    internal void DrawSettings()
    {
        if (!ImGui.TreeNodeEx("ReAgentAuras"))
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
        var extractionWasRunning = IsAssetExtractionRunning();
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

        var assetStatus = GetVisibleAssetExtractionStatus();
        if (!string.IsNullOrWhiteSpace(assetStatus) && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(assetStatus);
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

        assetStatus = GetVisibleAssetExtractionStatus();
        if (!string.IsNullOrWhiteSpace(assetStatus))
        {
            ImGui.TextColored(Color.LightGreen.ToImguiVec4(), assetStatus);
        }
    }

    private void EnsureContentGgpkAutoDetected()
    {
        if (!string.IsNullOrWhiteSpace(Settings.ContentGgpkPath.Value) || _autoDetectAttempted)
        {
            return;
        }

        _autoDetectAttempted = true;
        var detectedPath = TryGetContentGgpkPath(GameController?.Window?.Process);
        if (!string.IsNullOrWhiteSpace(detectedPath))
        {
            Settings.ContentGgpkPath.Value = detectedPath;
        }
    }

    private string ForceAutoDetectContentGgpkPath()
    {
        _autoDetectAttempted = true;
        return TryGetContentGgpkPath(GameController?.Window?.Process) ?? string.Empty;
    }

    private void QueuePluginAssetExtraction()
    {
        lock (_assetExtractionSync)
        {
            if (_assetExtractionRunning)
            {
                return;
            }

            _assetExtractionRunning = true;
        }

        SetAssetExtractionStatus("Extracting frames...");
        var contentGgpkPath = ResolveContentGgpkPath();
        var framesDirectory = ResolveFramesDirectory();

        Task.Run(() =>
        {
            try
            {
                var result = ReAgentAuraAssetExtractor.ExtractAssets(ReAgentAuraFrames.ExtractionRequests, contentGgpkPath, framesDirectory);
                SetAssetExtractionStatus(result.Message);
            }
            catch (Exception ex)
            {
                SetAssetExtractionStatus(ex.Message);
            }
            finally
            {
                lock (_assetExtractionSync)
                {
                    _assetExtractionRunning = false;
                }
            }
        });
    }

    private ReAgentAuraIconCacheEntry ExtractRuleIcon(string ddsFile)
    {
        return _iconCache.Queue(ddsFile, ResolveContentGgpkPath(), ResolveIconCacheDirectory());
    }

    private void ExtractOrRegisterRuleIcon(ReAgentAuraRule rule, string ddsFile)
    {
        var normalizedPath = ReAgentAuraIconCache.NormalizeDdsPath(ddsFile);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            SetIconStatus(rule, ReAgentAuraIconStatusKind.Failed, "No icon DDS path is available.");
            return;
        }

        var textureKey = ReAgentAuraTextureKeys.Icon(rule);
        var (_, pngOutputPath) = ReAgentAuraIconCache.GetSafeOutputPaths(ResolveIconCacheDirectory(), normalizedPath);
        rule.ExtractedPngPath = pngOutputPath;
        rule.IconTextureKey = textureKey;

        if (File.Exists(pngOutputPath))
        {
            if (IsTextureRegistered(textureKey))
            {
                SetIconStatus(rule, ReAgentAuraIconStatusKind.Skipped, "Icon already registered; skipped extraction.");
                return;
            }

            if (TryEnsureImageRegistered(textureKey, pngOutputPath))
            {
                SetIconStatus(rule, ReAgentAuraIconStatusKind.Ready, "Icon already existed; registered it.");
            }
            else
            {
                SetIconStatus(rule, ReAgentAuraIconStatusKind.Failed, "Icon already existed but could not be registered.");
            }

            return;
        }

        var entry = ExtractRuleIcon(ddsFile);
        rule.ExtractedPngPath = entry.PngOutputPath;
        rule.IconTextureKey = textureKey;
        var (kind, message) = entry.State switch
        {
            ReAgentAuraIconCacheState.Queued => (ReAgentAuraIconStatusKind.Queued, "Icon extraction queued."),
            ReAgentAuraIconCacheState.Extracting => (ReAgentAuraIconStatusKind.Extracting, "Icon extraction started."),
            ReAgentAuraIconCacheState.Ready => (ReAgentAuraIconStatusKind.Ready, "Icon extracted."),
            ReAgentAuraIconCacheState.MissingPath => (ReAgentAuraIconStatusKind.Failed, "Icon DDS was not found in Content.ggpk."),
            ReAgentAuraIconCacheState.Failed => (ReAgentAuraIconStatusKind.Failed, $"Icon extraction failed: {entry.Error}"),
            _ => (ReAgentAuraIconStatusKind.Skipped, "Icon extraction skipped.")
        };
        SetIconStatus(rule, kind, message);
    }

    private void RefreshPendingIconStatus(ReAgentAuraRule rule)
    {
        var status = GetIconStatus(rule, includeExpiredPending: true);
        if (string.IsNullOrWhiteSpace(rule.ExtractedPngPath) ||
            !File.Exists(rule.ExtractedPngPath) ||
            status?.IsPending != true)
        {
            return;
        }

        var textureKey = string.IsNullOrWhiteSpace(rule.IconTextureKey) ? ReAgentAuraTextureKeys.Icon(rule) : rule.IconTextureKey;
        rule.IconTextureKey = textureKey;
        if (TryEnsureImageRegistered(textureKey, rule.ExtractedPngPath))
        {
            SetIconStatus(rule, ReAgentAuraIconStatusKind.Ready, "Icon extracted and registered.");
        }
        else
        {
            SetIconStatus(rule, ReAgentAuraIconStatusKind.Failed, "Icon extracted but could not be registered.");
        }
    }

    private string GetVisibleAssetExtractionStatus()
    {
        lock (_assetExtractionSync)
        {
            return !string.IsNullOrWhiteSpace(_assetExtractionStatus) &&
                   (_assetExtractionRunning || Environment.TickCount64 < _assetExtractionStatusExpiresAtMs)
                ? _assetExtractionStatus
                : string.Empty;
        }
    }

    private bool IsAssetExtractionRunning()
    {
        lock (_assetExtractionSync)
        {
            return _assetExtractionRunning;
        }
    }

    private void SetAssetExtractionStatus(string status)
    {
        lock (_assetExtractionSync)
        {
            _assetExtractionStatus = status;
            _assetExtractionStatusExpiresAtMs = Environment.TickCount64 + StatusVisibleMs;
        }
    }

    private void SetIconStatus(ReAgentAuraRule rule, ReAgentAuraIconStatusKind kind, string message)
    {
        _iconStatuses[rule.Id] = new ReAgentAuraIconStatus(kind, message, Environment.TickCount64 + StatusVisibleMs);
    }

    private ReAgentAuraIconStatus GetIconStatus(ReAgentAuraRule rule, bool includeExpiredPending = false)
    {
        if (!_iconStatuses.TryGetValue(rule.Id, out var status))
        {
            return null;
        }

        var expired = Environment.TickCount64 >= status.ExpiresAtMs;
        if (status.Kind == ReAgentAuraIconStatusKind.None || (!status.IsPending && expired))
        {
            _iconStatuses.Remove(rule.Id);
            return null;
        }

        return expired && !includeExpiredPending ? null : status;
    }

    private string ResolveIconCacheDirectory()
    {
        return Path.Combine(_plugin.ConfigDirectory, "ReAgentAuras", "Icons");
    }

    private string ResolveFramesDirectory()
    {
        return Path.Combine(_plugin.ConfigDirectory, "ReAgentAuras", "Frames");
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
        var detectedPath = TryGetContentGgpkPath(GameController?.Window?.Process);
        if (!string.IsNullOrWhiteSpace(detectedPath))
        {
            Settings.ContentGgpkPath.Value = detectedPath;
            return detectedPath;
        }

        return string.Empty;
    }

    private static string TryGetContentGgpkPath(Process process)
    {
        try
        {
            var exePath = process?.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return null;
            }

            var candidate = Path.Combine(Path.GetDirectoryName(exePath)!, "Content.ggpk");
            return File.Exists(candidate) ? candidate : null;
        }
        catch
        {
            return null;
        }
    }
}
