using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ReAgent.ExileAuras;

public sealed class ExileAuraIconCache
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ExileAuraIconCacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<ExtractRequest> _pending = new();
    private bool _workerRunning;

    public ExileAuraIconCacheEntry Queue(string ddsFile, string contentGgpkPath, string cacheRoot)
    {
        var normalizedPath = NormalizeDdsPath(ddsFile);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return ExileAuraIconCacheEntry.NoIcon(ddsFile);
        }

        var (ddsOutputPath, pngOutputPath) = GetSafeOutputPaths(cacheRoot, normalizedPath);
        lock (_sync)
        {
            if (_entries.TryGetValue(normalizedPath, out var existing))
            {
                if (existing.State is ExileAuraIconCacheState.Ready or ExileAuraIconCacheState.Queued or ExileAuraIconCacheState.Extracting)
                {
                    return existing;
                }
            }

            if (File.Exists(pngOutputPath))
            {
                var ready = ExileAuraIconCacheEntry.Ready(normalizedPath, ddsOutputPath, pngOutputPath);
                _entries[normalizedPath] = ready;
                return ready;
            }

            var queued = ExileAuraIconCacheEntry.Queued(normalizedPath, ddsOutputPath, pngOutputPath);
            _entries[normalizedPath] = queued;
            _pending.Enqueue(new ExtractRequest(normalizedPath, ddsOutputPath, pngOutputPath, contentGgpkPath));

            if (!_workerRunning)
            {
                _workerRunning = true;
                Task.Run(ProcessQueue);
            }

            return queued;
        }
    }

    private void ProcessQueue()
    {
        while (true)
        {
            ExtractRequest request;
            lock (_sync)
            {
                if (_pending.Count == 0)
                {
                    _workerRunning = false;
                    return;
                }

                request = _pending.Dequeue();
                _entries[request.GgpkPath] = ExileAuraIconCacheEntry.Extracting(request.GgpkPath, request.DdsOutputPath, request.PngOutputPath);
            }

            var result = ExtractOne(request);
            lock (_sync)
            {
                _entries[request.GgpkPath] = result;
            }
        }
    }

    private static ExileAuraIconCacheEntry ExtractOne(ExtractRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ContentGgpkPath))
            {
                return ExileAuraIconCacheEntry.Failed(request.GgpkPath, request.DdsOutputPath, request.PngOutputPath, "Content.ggpk path is empty.");
            }

            if (!File.Exists(request.ContentGgpkPath))
            {
                return ExileAuraIconCacheEntry.Failed(request.GgpkPath, request.DdsOutputPath, request.PngOutputPath, $"Content.ggpk was not found: {request.ContentGgpkPath}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(request.DdsOutputPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(request.PngOutputPath)!);

            using var ggpk = ExileAuraGgpk.Open(request.ContentGgpkPath);
            if (!ggpk.Index.TryGetFile(request.GgpkPath, out var file))
            {
                return ExileAuraIconCacheEntry.MissingPath(request.GgpkPath, request.DdsOutputPath, request.PngOutputPath);
            }

            var wroteFile = false;
            var extractedCount = LibBundle3.Index.Extract(new[] { file }, (record, content) =>
            {
                if (content == null)
                {
                    return false;
                }

                var bytes = content.Value.ToArray();
                File.WriteAllBytes(request.DdsOutputPath, bytes);
                ExileAuraDdsConverter.ConvertToPng(bytes, request.PngOutputPath);
                TryDeleteFile(request.DdsOutputPath);
                wroteFile = true;
                return false;
            });

            if (wroteFile && extractedCount == 1 && File.Exists(request.PngOutputPath))
            {
                return ExileAuraIconCacheEntry.Ready(request.GgpkPath, request.DdsOutputPath, request.PngOutputPath);
            }

            return ExileAuraIconCacheEntry.Failed(request.GgpkPath, request.DdsOutputPath, request.PngOutputPath, $"Extract returned {extractedCount}, wroteFile={wroteFile}.");
        }
        catch (Exception ex)
        {
            return ExileAuraIconCacheEntry.Failed(request.GgpkPath, request.DdsOutputPath, request.PngOutputPath, ex.Message);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    public static string NormalizeDdsPath(string ddsFile)
    {
        if (string.IsNullOrWhiteSpace(ddsFile))
        {
            return string.Empty;
        }

        var path = ddsFile.Replace('\\', '/').Trim().TrimStart('/');
        if (!path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
        {
            path += ".dds";
        }

        const string dbPrefix = "Art/2DArt/";
        if (path.StartsWith(dbPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var relativePath = path[dbPrefix.Length..];
            var prefix = relativePath.StartsWith("UIImages/", StringComparison.OrdinalIgnoreCase)
                ? "art/textures/interface/2d/2dart/"
                : "art/2dart/";

            path = prefix + relativePath;
        }

        path = path.ToLowerInvariant();
        return IsSafeRelativePath(path) ? path : string.Empty;
    }

    public static (string DdsOutputPath, string PngOutputPath) GetSafeOutputPaths(string cacheRoot, string normalizedPath)
    {
        var root = Path.GetFullPath(cacheRoot);
        var outputName = CreateFlatOutputName(normalizedPath);
        var ddsOutputPath = Path.GetFullPath(Path.Combine(root, outputName + ".dds"));
        var pngOutputPath = Path.GetFullPath(Path.Combine(root, outputName + ".png"));

        if (!ExileAuraPaths.IsInsideDirectory(root, ddsOutputPath) ||
            !ExileAuraPaths.IsInsideDirectory(root, pngOutputPath))
        {
            throw new InvalidOperationException("DDS output path escaped the cache directory.");
        }

        return (ddsOutputPath, pngOutputPath);
    }

    private static string CreateFlatOutputName(string normalizedPath)
    {
        var fileName = normalizedPath.Split('/').LastOrDefault() ?? "icon.dds";
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var safeName = SanitizeFileName(baseName);
        return $"{safeName}-{StableHash(normalizedPath):x8}";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name
            .Select(ch => invalid.Contains(ch) ? '_' : char.ToLowerInvariant(ch))
            .ToArray();
        var safeName = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(safeName) ? "icon" : safeName;
    }

    private static uint StableHash(string text)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;

        unchecked
        {
            var hash = offset;
            foreach (var ch in text)
            {
                hash ^= char.ToLowerInvariant(ch);
                hash *= prime;
            }

            return hash;
        }
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return false;
        }

        return path.Split('/').All(part => part.Length > 0 && part != "." && part != "..");
    }

    private sealed record ExtractRequest(string GgpkPath, string DdsOutputPath, string PngOutputPath, string ContentGgpkPath);
}

public sealed record ExileAuraIconCacheEntry(
    string GgpkPath,
    string DdsOutputPath,
    string PngOutputPath,
    ExileAuraIconCacheState State,
    string Error)
{
    public string OutputPath => PngOutputPath;

    public static ExileAuraIconCacheEntry NoIcon(string path) => new(path, string.Empty, string.Empty, ExileAuraIconCacheState.NoIcon, "");
    public static ExileAuraIconCacheEntry Queued(string path, string ddsOutputPath, string pngOutputPath) => new(path, ddsOutputPath, pngOutputPath, ExileAuraIconCacheState.Queued, "");
    public static ExileAuraIconCacheEntry Extracting(string path, string ddsOutputPath, string pngOutputPath) => new(path, ddsOutputPath, pngOutputPath, ExileAuraIconCacheState.Extracting, "");
    public static ExileAuraIconCacheEntry Ready(string path, string ddsOutputPath, string pngOutputPath) => new(path, ddsOutputPath, pngOutputPath, ExileAuraIconCacheState.Ready, "");
    public static ExileAuraIconCacheEntry MissingPath(string path, string ddsOutputPath, string pngOutputPath) => new(path, ddsOutputPath, pngOutputPath, ExileAuraIconCacheState.MissingPath, "");
    public static ExileAuraIconCacheEntry Failed(string path, string ddsOutputPath, string pngOutputPath, string error) => new(path, ddsOutputPath, pngOutputPath, ExileAuraIconCacheState.Failed, error);
}

public enum ExileAuraIconCacheState
{
    Ready,
    Queued,
    Extracting,
    Failed,
    MissingPath,
    NoIcon
}
