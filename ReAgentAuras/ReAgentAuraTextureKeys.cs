namespace ReAgent.ReAgentAuras;

internal static class ReAgentAuraTextureKeys
{
    public static string Icon(ReAgentAuraRule rule)
    {
        return $"exileauras_icon_{rule.Id}";
    }

    public static string Frame(string frameName)
    {
        return $"exileauras_frame_{frameName}";
    }
}
