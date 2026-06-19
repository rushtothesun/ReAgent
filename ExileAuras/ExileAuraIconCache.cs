using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LibBundle3.Records;
using LibBundledGGPK3;
using Pfim;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;

namespace ReAgent.ExileAuras;

public sealed class ExileAuraIconCache
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ExileAuraIconCacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<ExtractRequest> _pending = new();
    private bool _workerRunning;

    public IReadOnlyCollection<ExileAuraIconCacheEntry> Snapshot()
    {
        lock (_sync)
        {
            return _entries.Values
                .OrderBy(x => x.State)
                .ThenBy(x => x.GgpkPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

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

    public static AssetExtractionResult ExtractAssets(
        IReadOnlyCollection<AssetExtractionRequest> requests,
        string contentGgpkPath,
        string outputDirectory)
    {
        if (requests.Count == 0)
        {
            return new AssetExtractionResult(0, 0, 0, "No assets requested.");
        }

        if (string.IsNullOrWhiteSpace(contentGgpkPath))
        {
            return new AssetExtractionResult(requests.Count, 0, 0, "Content.ggpk path is empty.");
        }

        if (!File.Exists(contentGgpkPath))
        {
            return new AssetExtractionResult(requests.Count, 0, 0, $"Content.ggpk was not found: {contentGgpkPath}");
        }

        Directory.CreateDirectory(outputDirectory);

        var pendingRequests = new List<AssetExtractionRequest>();
        var skipped = 0;
        foreach (var request in requests)
        {
            var outputPath = GetSafeAssetOutputPath(outputDirectory, request.OutputPngName);
            if (File.Exists(outputPath))
            {
                skipped++;
                continue;
            }

            pendingRequests.Add(request);
        }

        if (pendingRequests.Count == 0)
        {
            return new AssetExtractionResult(requests.Count, 0, skipped, $"Skipped {skipped}/{requests.Count} assets; all frames already exist.");
        }

        using var ggpk = OpenBundledGgpk(contentGgpkPath);
        var files = new List<FileRecord>();
        var requestByPath = new Dictionary<string, AssetExtractionRequest>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var request in pendingRequests)
        {
            var normalizedPath = NormalizeDdsPath(request.DdsPath);
            if (string.IsNullOrWhiteSpace(normalizedPath) || !ggpk.Index.TryGetFile(normalizedPath, out var file))
            {
                missing.Add(request.DdsPath);
                continue;
            }

            files.Add(file);
            requestByPath[normalizedPath] = request;
        }

        if (files.Count == 0)
        {
            return new AssetExtractionResult(requests.Count, 0, skipped, "No requested assets were found.");
        }

        var written = 0;
        var extractedCount = LibBundle3.Index.Extract(files, (record, content) =>
        {
            if (content == null || !requestByPath.TryGetValue(record.Path, out var request))
            {
                return false;
            }

            var outputPath = GetSafeAssetOutputPath(outputDirectory, request.OutputPngName);
            ConvertDdsToPng(content.Value.ToArray(), outputPath, request.TargetWidth, request.TargetHeight);
            written++;
            return false;
        });

        var message = missing.Count == 0
            ? $"Extracted {written}, skipped {skipped}, requested {requests.Count} assets."
            : $"Extracted {written}, skipped {skipped}, requested {requests.Count} assets. Missing: {string.Join(", ", missing)}";

        if (extractedCount != files.Count)
        {
            message += $" Extract returned {extractedCount}/{files.Count}.";
        }

        return new AssetExtractionResult(requests.Count, written, skipped, message);
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

            using var ggpk = OpenBundledGgpk(request.ContentGgpkPath);
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
                ConvertDdsToPng(bytes, request.PngOutputPath);
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

    private static void ConvertDdsToPng(byte[] ddsBytes, string outputPath, int targetWidth = 0, int targetHeight = 0)
    {
        using var stream = new MemoryStream(ddsBytes);
        using var image = Dds.Create(stream, new PfimConfig());

        switch (image.Format)
        {
            case ImageFormat.Rgba32:
                SavePixelDataAsPng<Bgra32>(image, outputPath, 4, targetWidth, targetHeight);
                break;
            case ImageFormat.Rgb24:
                SavePixelDataAsPng<Bgr24>(image, outputPath, 3, targetWidth, targetHeight);
                break;
            case ImageFormat.Rgb8:
                SavePixelDataAsPng<L8>(image, outputPath, 1, targetWidth, targetHeight);
                break;
            default:
                throw new NotSupportedException($"Unsupported DDS pixel format: {image.Format}");
        }
    }

    private static BundledGGPK OpenBundledGgpk(string ggpkPath)
    {
        var stream = new FileStream(ggpkPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var ggpk = new BundledGGPK(stream, leaveOpen: false, parsePathsInIndex: false);
        ggpk.Index.ParsePaths();
        return ggpk;
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
        var ddsOutputPath = Path.GetFullPath(Path.Combine(root, "dds", outputName + ".dds"));
        var pngOutputPath = Path.GetFullPath(Path.Combine(root, "png", outputName + ".png"));

        if (!ddsOutputPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !pngOutputPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("DDS output path escaped the cache directory.");
        }

        return (ddsOutputPath, pngOutputPath);
    }

    private static string GetSafeAssetOutputPath(string outputDirectory, string outputName)
    {
        var root = Path.GetFullPath(outputDirectory);
        var outputPath = Path.GetFullPath(Path.Combine(root, outputName));
        if (!outputPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Asset output path escaped the output directory.");
        }

        return outputPath;
    }

    private static void SavePixelDataAsPng<TPixel>(IImage image, string outputPath, int bytesPerPixel, int targetWidth = 0, int targetHeight = 0)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var pixelData = GetTightlyPackedPixelData(image, bytesPerPixel);
        using var png = Image.LoadPixelData<TPixel>(pixelData, image.Width, image.Height);
        if (targetWidth > 0 && targetHeight > 0 && (png.Width != targetWidth || png.Height != targetHeight))
        {
            png.Mutate(x => x.Resize(targetWidth, targetHeight));
        }

        using var output = File.Create(outputPath);
        png.Save(output, new PngEncoder());
    }

    private static byte[] GetTightlyPackedPixelData(IImage image, int bytesPerPixel)
    {
        var rowLength = image.Width * bytesPerPixel;
        var outputLength = rowLength * image.Height;
        if (image.Stride == rowLength && image.DataLen >= outputLength)
        {
            return image.Data.Length == outputLength ? image.Data : image.Data.Take(outputLength).ToArray();
        }

        var data = new byte[outputLength];
        for (var y = 0; y < image.Height; y++)
        {
            Buffer.BlockCopy(image.Data, y * image.Stride, data, y * rowLength, rowLength);
        }

        return data;
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

public sealed record AssetExtractionRequest(string DdsPath, string OutputPngName, int TargetWidth = 0, int TargetHeight = 0);

public sealed record AssetExtractionResult(int Requested, int Written, int Skipped, string Message);

public enum ExileAuraIconCacheState
{
    Ready,
    Queued,
    Extracting,
    Failed,
    MissingPath,
    NoIcon
}
