using System;
using System.Collections.Generic;
using System.IO;
using LibBundle3.Records;

namespace ReAgent.ReAgentAuras;

internal static class ReAgentAuraAssetExtractor
{
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

        using var ggpk = ReAgentAuraGgpk.Open(contentGgpkPath);
        var files = new List<FileRecord>();
        var requestByPath = new Dictionary<string, AssetExtractionRequest>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var request in pendingRequests)
        {
            var normalizedPath = ReAgentAuraIconCache.NormalizeDdsPath(request.DdsPath);
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
            ReAgentAuraDdsConverter.ConvertToPng(content.Value.ToArray(), outputPath, request.TargetWidth, request.TargetHeight);
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

    private static string GetSafeAssetOutputPath(string outputDirectory, string outputName)
    {
        var root = Path.GetFullPath(outputDirectory);
        var outputPath = Path.GetFullPath(Path.Combine(root, outputName));
        if (!ReAgentAuraPaths.IsInsideDirectory(root, outputPath))
        {
            throw new InvalidOperationException("Asset output path escaped the output directory.");
        }

        return outputPath;
    }
}

public sealed record AssetExtractionRequest(string DdsPath, string OutputPngName, int TargetWidth = 0, int TargetHeight = 0);

public sealed record AssetExtractionResult(int Requested, int Written, int Skipped, string Message);
