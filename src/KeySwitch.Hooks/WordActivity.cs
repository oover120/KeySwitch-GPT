namespace KeySwitch.Hooks;

// Only counts and reason codes survive a discarded word buffer.
internal sealed class WordActivity
{
    private long? lastIgnoredAt;
    internal int Length { get; private set; }
    internal string? ResetReason { get; private set; }
    internal string? IgnoredReason { get; private set; }

    internal void Add() => Length = Math.Min(Length + 1, 4096);
    internal void Backspace() => Length = Math.Max(0, Length - 1);
    internal void ResetBuffer(string reason) => ResetReason = reason;

    internal bool Ignore(string reason, long now)
    {
        IgnoredReason = reason;
        if (lastIgnoredAt is long previous && now - previous < 1000) return false;
        lastIgnoredAt = now;
        return true;
    }

    internal (int Length, string? Reset, string? Ignored) Complete()
    {
        var result = (Length, ResetReason, IgnoredReason);
        Length = 0;
        ResetReason = null;
        IgnoredReason = null;
        return result;
    }
}
