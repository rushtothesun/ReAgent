namespace ReAgent.ReAgentAuras;

internal enum ReAgentAuraIconStatusKind
{
    None,
    Queued,
    Extracting,
    Ready,
    Skipped,
    Failed
}

internal sealed record ReAgentAuraIconStatus(ReAgentAuraIconStatusKind Kind, string Message, long ExpiresAtMs)
{
    public bool IsPending => Kind is ReAgentAuraIconStatusKind.Queued or ReAgentAuraIconStatusKind.Extracting;
}
