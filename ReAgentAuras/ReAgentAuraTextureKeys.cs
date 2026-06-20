using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ReAgent.ReAgentAuras;

internal static class ReAgentAuraTextureKeys
{
    public static string Icon(string path)
    {
        return Image("icon", path);
    }

    public static string ManualIcon(string path)
    {
        return Image("manual_icon", path);
    }

    public static string Frame(string frameName)
    {
        return $"reagentauras_frame_{frameName}";
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return path.Trim();
        }
    }

    private static string Image(string kind, string path)
    {
        path = NormalizePath(path);
        return string.IsNullOrWhiteSpace(path)
            ? ""
            : $"reagentauras_{kind}_{HashPath(path)}";
    }

    private static string HashPath(string path)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()));
        return Convert.ToHexString(bytes, 0, 4).ToLowerInvariant();
    }
}
