using System;
using System.IO;
using System.Linq;
using Pfim;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;

namespace ReAgent.ReAgentAuras;

internal static class ReAgentAuraDdsConverter
{
    public static void ConvertToPng(byte[] ddsBytes, string outputPath, int targetWidth = 0, int targetHeight = 0)
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

    private static void SavePixelDataAsPng<TPixel>(IImage image, string outputPath, int bytesPerPixel, int targetWidth, int targetHeight)
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
}
