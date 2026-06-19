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

    private readonly ReAgent _plugin;
    private readonly ExileAuraIconCache _iconCache = new();
    private readonly ExileAuraConditionCompiler _conditionCompiler = new();
    private readonly HashSet<string> _registeredTextureKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExileAuraIconStatus> _iconStatuses = new(StringComparer.Ordinal);
    private readonly object _assetExtractionSync = new();

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
                var result = ExileAuraAssetExtractor.ExtractAssets(ExileAuraFrames.ExtractionRequests, contentGgpkPath, framesDirectory);
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

    private ExileAuraIconCacheEntry ExtractRuleIcon(string ddsFile)
    {
        return _iconCache.Queue(ddsFile, ResolveContentGgpkPath(), ResolveIconCacheDirectory());
    }

    private void ExtractOrRegisterRuleIcon(ExileAuraRule rule, string ddsFile)
    {
        var normalizedPath = ExileAuraIconCache.NormalizeDdsPath(ddsFile);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            SetIconStatus(rule, ExileAuraIconStatusKind.Failed, "No icon DDS path is available.");
            return;
        }

        var textureKey = ExileAuraTextureKeys.Icon(rule);
        var (_, pngOutputPath) = ExileAuraIconCache.GetSafeOutputPaths(ResolveIconCacheDirectory(), normalizedPath);
        rule.ExtractedPngPath = pngOutputPath;
        rule.IconTextureKey = textureKey;

        if (File.Exists(pngOutputPath))
        {
            if (IsTextureRegistered(textureKey))
            {
                SetIconStatus(rule, ExileAuraIconStatusKind.Skipped, "Icon already registered; skipped extraction.");
                return;
            }

            if (TryEnsureImageRegistered(textureKey, pngOutputPath))
            {
                SetIconStatus(rule, ExileAuraIconStatusKind.Ready, "Icon already existed; registered it.");
            }
            else
            {
                SetIconStatus(rule, ExileAuraIconStatusKind.Failed, "Icon already existed but could not be registered.");
            }

            return;
        }

        var entry = ExtractRuleIcon(ddsFile);
        rule.ExtractedPngPath = entry.PngOutputPath;
        rule.IconTextureKey = textureKey;
        var (kind, message) = entry.State switch
        {
            ExileAuraIconCacheState.Queued => (ExileAuraIconStatusKind.Queued, "Icon extraction queued."),
            ExileAuraIconCacheState.Extracting => (ExileAuraIconStatusKind.Extracting, "Icon extraction started."),
            ExileAuraIconCacheState.Ready => (ExileAuraIconStatusKind.Ready, "Icon extracted."),
            ExileAuraIconCacheState.MissingPath => (ExileAuraIconStatusKind.Failed, "Icon DDS was not found in Content.ggpk."),
            ExileAuraIconCacheState.Failed => (ExileAuraIconStatusKind.Failed, $"Icon extraction failed: {entry.Error}"),
            _ => (ExileAuraIconStatusKind.Skipped, "Icon extraction skipped.")
        };
        SetIconStatus(rule, kind, message);
    }

    private void RefreshPendingIconStatus(ExileAuraRule rule)
    {
        var status = GetIconStatus(rule, includeExpiredPending: true);
        if (string.IsNullOrWhiteSpace(rule.ExtractedPngPath) ||
            !File.Exists(rule.ExtractedPngPath) ||
            status?.IsPending != true)
        {
            return;
        }

        var textureKey = string.IsNullOrWhiteSpace(rule.IconTextureKey) ? ExileAuraTextureKeys.Icon(rule) : rule.IconTextureKey;
        rule.IconTextureKey = textureKey;
        if (TryEnsureImageRegistered(textureKey, rule.ExtractedPngPath))
        {
            SetIconStatus(rule, ExileAuraIconStatusKind.Ready, "Icon extracted and registered.");
        }
        else
        {
            SetIconStatus(rule, ExileAuraIconStatusKind.Failed, "Icon extracted but could not be registered.");
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

    private void SetIconStatus(ExileAuraRule rule, ExileAuraIconStatusKind kind, string message)
    {
        _iconStatuses[rule.Id] = new ExileAuraIconStatus(kind, message, Environment.TickCount64 + StatusVisibleMs);
    }

    private ExileAuraIconStatus GetIconStatus(ExileAuraRule rule, bool includeExpiredPending = false)
    {
        if (!_iconStatuses.TryGetValue(rule.Id, out var status))
        {
            return null;
        }

        var expired = Environment.TickCount64 >= status.ExpiresAtMs;
        if (status.Kind == ExileAuraIconStatusKind.None || (!status.IsPending && expired))
        {
            _iconStatuses.Remove(rule.Id);
            return null;
        }

        return expired && !includeExpiredPending ? null : status;
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
