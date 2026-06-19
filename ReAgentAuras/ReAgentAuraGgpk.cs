using System.IO;
using LibBundledGGPK3;

namespace ReAgent.ReAgentAuras;

internal static class ReAgentAuraGgpk
{
    public static BundledGGPK Open(string ggpkPath)
    {
        var stream = new FileStream(ggpkPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var ggpk = new BundledGGPK(stream, leaveOpen: false, parsePathsInIndex: false);
        ggpk.Index.ParsePaths();
        return ggpk;
    }
}
