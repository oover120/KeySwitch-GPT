namespace KeySwitch.Hooks;

// The diagnostic schema deliberately has no field for text, titles, paths or key codes.
public sealed class WordTrace
{
    private int published;
    public string Level { get; init; } = "INFO";
    public string Kind { get; init; } = "word";
    public DateTimeOffset Time { get; } = DateTimeOffset.Now;
    public required string Process { get; init; }
    public required string FocusClass { get; init; }
    public required string PasswordCheck { get; init; }
    public required bool ProgramExcluded { get; init; }
    public required bool GameOrFullscreen { get; init; }
    public required bool ContextReady { get; init; }
    public bool ElevatedWindow { get; init; }
    public string? ContextError { get; init; }
    public int? ContextWin32Error { get; init; }
    public required int WordLength { get; init; }
    public required int BufferedLength { get; init; }
    public required int FocusResets { get; init; }
    public string? ResetReason { get; init; }
    public double? ScoreA { get; set; }
    public double? ScoreB { get; set; }
    public double? Confidence { get; set; }
    public string Decision { get; set; } = "skip";
    public string Reason { get; set; } = "not-evaluated";
    public string? LayoutBefore { get; init; }
    public string? LayoutAfter { get; set; }
    public InputReport? SendInput { get; set; }
    public LayoutReport? LayoutRequest { get; set; }
    internal bool Deferred { get; set; }
    internal bool TryPublish() => Interlocked.Exchange(ref published, 1) == 0;
}

public sealed record LayoutReport(bool Confirmed, int Attempts, bool PostedToFocus, bool PostedToRoot, int Win32Error);

public sealed record InputReport(int Requested, uint Sent, int Win32Error, int Batches = 1, bool Interrupted = false)
{
    public bool Success => !Interrupted && Sent == Requested;
}

internal sealed class InputInjectionException(InputReport report) : InvalidOperationException("SendInput blocked or incomplete")
{
    public InputReport Report { get; } = report;
}
