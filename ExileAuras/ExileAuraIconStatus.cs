namespace ReAgent.ExileAuras;

internal enum ExileAuraIconStatusKind
{
    None,
    Queued,
    Extracting,
    Ready,
    Skipped,
    Failed
}

internal sealed record ExileAuraIconStatus(ExileAuraIconStatusKind Kind, string Message, long ExpiresAtMs)
{
    public bool IsPending => Kind is ExileAuraIconStatusKind.Queued or ExileAuraIconStatusKind.Extracting;
}
