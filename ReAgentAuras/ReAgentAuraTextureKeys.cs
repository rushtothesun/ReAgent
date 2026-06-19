using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ReAgent.ReAgentAuras;

internal static class ReAgentAuraTextureKeys
{
    public static string Icon(ReAgentAuraRule rule)
    {
        return $"reagentauras_icon_{rule.Id}";
    }

    public static string ManualIcon(ReAgentAuraRule rule)
    {
        var path = NormalizePath(rule.ManualIconPath);
        return string.IsNullOrWhiteSpace(path)
            ? Icon(rule)
            : $"reagentauras_manual_icon_{rule.Id}_{HashPath(path)}";
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

    private static string HashPath(string path)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()));
        return Convert.ToHexString(bytes, 0, 4).ToLowerInvariant();
    }
}
