namespace ReAgent.ExileAuras;

internal static class ExileAuraTextureKeys
{
    public static string Icon(ExileAuraRule rule)
    {
        return $"exileauras_icon_{rule.Id}";
    }

    public static string Frame(string frameName)
    {
        return $"exileauras_frame_{frameName}";
    }
}
