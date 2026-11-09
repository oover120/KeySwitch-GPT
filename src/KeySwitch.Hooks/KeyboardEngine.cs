using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using KeySwitch.Core;
namespace KeySwitch.Hooks;

public sealed record EngineOptions(bool Enabled, double Threshold, bool Early, bool SingleShift, bool Typos, bool Capitals, bool Yo,
    int SelectionKey, int SelectionModifiers, int PauseKey, int PauseModifiers, IReadOnlyList<ApplicationRule> Applications,
    IReadOnlyList<WordException> Words, IReadOnlyDictionary<string, string> Replacements, IReadOnlyDictionary<string, string> YoWords, IReadOnlyDictionary<string, string> Learned, bool Debug = false,
    IReadOnlyList<string>? GameExecutables = null, bool PreferRussian = true);

public sealed class KeyboardEngine : IDisposable
{
    private readonly LanguageDetector detector;
    private readonly Thread thread;
    private readonly ConcurrentQueue<Action> actions = new();
    private readonly ManualResetEventSlim started = new();
    private readonly Native.HookProc keyboardCallback, mouseCallback;
    private readonly WordBuffer buffer = new();
    private readonly UndoState undo = new();
    private readonly ShiftGesture shift = new();
    private readonly HashSet<uint> pressed = [];
    private readonly HashSet<uint> suppressed = [];
    private readonly List<Native.KeyboardEvent> deferred = [];
    private volatile EngineOptions options;
    private WindowMonitor? windows;
    private nint keyboardHook, mouseHook;
    private uint threadId;
    private Exception? startupError;
    private volatile bool busy;
    private bool disposed;
    private Action? cancelReserved;
    private long shiftToken;
    private readonly WordActivity activity = new();
    private WindowContext? bufferOwner;
    private int focusResets;
    private (string Word, string Language, string Delimiter, WindowContext Context)? lastWord;
    public event Action<string>? Status;
    public event Action<string, string, string, bool>? Learning;
    public event Action<bool>? Corrected;
    public event Action? TogglePause;
    public event Action<WindowContext>? ConvertSelection;
    public event Action<WordTrace>? Trace;
    public WindowContext? CurrentWindow => windows?.Current;
    public bool Matches(WindowContext context) => busy && windows?.CanEdit(context) == true;
    public void RefreshFocus() => Post(() => windows?.RefreshFocus(force: true));
    public void CompleteSelection() => Post(() => { Reset("selection-conversion"); Finish(); });
    public KeyboardEngine(LanguageDetector detector, EngineOptions options)
    {
        this.detector = detector; this.options = options;
        keyboardCallback = OnKeyboard; mouseCallback = OnMouse;
        thread = new Thread(Run) { IsBackground = true, Name = "KeySwitch input" }; thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!started.Wait(TimeSpan.FromSeconds(10))) throw new InvalidOperationException("Input thread did not start");
        if (startupError is not null) throw new InvalidOperationException("Input startup failed", startupError);
    }
    public void Configure(EngineOptions value, bool reset = true) { options = value; if (reset) Post(() => Reset("settings-changed")); }
    private void Run()
    {
        try
        {
            threadId = Native.GetCurrentThreadId(); windows = new WindowMonitor();
            windows.Changed += reason => { focusResets++; Reset(reason); };
            windows.Observed += ObserveFocus;
            keyboardHook = Native.SetWindowsHookEx(13, keyboardCallback, Native.GetModuleHandle(null), 0);
            mouseHook = Native.SetWindowsHookEx(14, mouseCallback, Native.GetModuleHandle(null), 0);
            if (keyboardHook == 0 || mouseHook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            started.Set();
            while (Native.GetMessage(out var message, 0, 0, 0) > 0)
            {
                if (message.Id == 0x8001) { while (actions.TryDequeue(out var action)) { try { action(); } catch { Reset("input-operation-error"); Status?.Invoke("Операция ввода отменена"); } } }
                else { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
            }
        }
        catch (Exception ex) { startupError = ex; started.Set(); }
        finally { if (keyboardHook != 0) Native.UnhookWindowsHookEx(keyboardHook); if (mouseHook != 0) Native.UnhookWindowsHookEx(mouseHook); windows?.Dispose(); }
    }
    private void Post(Action action)
    { if (disposed) return; actions.Enqueue(action); Native.PostThreadMessage(threadId, 0x8001, 0, 0); }
    private void Reset(string reason)
    {
        if (!buffer.IsEmpty) ReportReset(reason);
        ClearBuffer();
        undo.Invalidate();
        lastWord = null;
        shift.OtherKey();
        Interlocked.Increment(ref shiftToken);
    }

    private void ClearBuffer()
    {
        buffer.Reset();
        bufferOwner = null;
    }

    private void ReportReset(string reason)
    {
        activity.ResetBuffer(reason);
        var context = bufferOwner ?? windows!.Current;
        var trace = CreateTrace(context, "buffer-reset", Math.Max(activity.Length, buffer.Keys.Count), reason);
        trace.Decision = "reset";
        trace.Reason = reason;
        PublishTrace(trace);
    }
    private nint OnMouse(int code, nint message, nint data)
    {
        if (code >= 0 && message is 0x201 or 0x204 or 0x207 or 0x20B)
        {
            windows?.RefreshFocus(afterClick: true);
        }
        return Native.CallNextHookEx(mouseHook, code, message, data);
    }
    private nint OnKeyboard(int code, nint message, nint data)
    {
        if (code < 0) return Native.CallNextHookEx(keyboardHook, code, message, data);
        var key = Marshal.PtrToStructure<Native.KeyboardEvent>(data);
        if ((key.Flags & 0x10) != 0 && key.Extra == Native.Marker)
            return Native.CallNextHookEx(keyboardHook, code, message, data);
        try
        {
            if (busy)
            {
                deferred.Add(key);
                // The queued mutation runs before the next message; bound unexpected reentrancy.
                if (deferred.Count > 512) { busy = false; cancelReserved?.Invoke(); Reset("queue-overflow"); Finish(); }
                return 1;
            }
            if (Process(key, true)) return 1;
        }
        catch { Reset("key-processing-error"); Status?.Invoke("Обработка клавиши пропущена"); }
        return Native.CallNextHookEx(keyboardHook, code, message, data);
    }
    private bool Process(Native.KeyboardEvent key, bool allowActions)
    {
        var vk = (int)key.Vk;
        var up = (key.Flags & 0x80) != 0;
        var lowerIntegrity = (key.Flags & 2) != 0;
        if (!lowerIntegrity)
        {
            if (up) pressed.Remove(key.Vk); else pressed.Add(key.Vk);
            if (up && suppressed.Remove(key.Vk)) return true;
        }
        var context = windows!.RefreshFocus();
        var settings = options;
        var rule = ExclusionPolicy.RuleFor(context.ProcessPath, settings.Applications);
        var game = ApplicationProfiles.IsGame(context.ProcessPath, context.IsFullScreen, settings.GameExecutables);
        var mode = rule?.Mode ?? (game ? ApplicationMode.Disabled : ApplicationMode.Full);
        var modifiers = (pressed.Any(k => k is 0x11 or 0xA2 or 0xA3) ? 2 : 0)
            | (pressed.Any(k => k is 0x10 or 0xA0 or 0xA1) ? 4 : 0)
            | (pressed.Any(k => k is 0x12 or 0xA4 or 0xA5) ? 1 : 0)
            | (pressed.Any(k => k is 0x5B or 0x5C) ? 8 : 0);
        var plainInput = !up && (modifiers & 11) == 0;
        var textKey = plainInput && IsTextCandidate(vk);
        if (textKey) activity.Add();
        if (plainInput && vk == 8) activity.Backspace();

        WordTrace? trace = null;
        string? earlierIgnore = null;
        if (plainInput && WordBuffer.IsBoundary(vk) && !(vk == settings.SelectionKey && modifiers == settings.SelectionModifiers))
        {
            var (length, reset, ignored) = activity.Complete();
            earlierIgnore = ignored;
            if (length > 0 || !buffer.IsEmpty)
                trace = CreateTrace(context, "word", Math.Max(length, buffer.Keys.Count), reset, rule, game);
            focusResets = 0;
        }

        try
        {
            return ProcessKey(key, context, mode, rule, modifiers, up, textKey, lowerIntegrity, allowActions, trace, earlierIgnore);
        }
        catch
        {
            if (trace is not null) trace.Reason = "key-processing-error";
            throw;
        }
        finally
        {
            if (trace is { Deferred: false }) PublishTrace(trace);
        }
    }

    private bool ProcessKey(Native.KeyboardEvent key, WindowContext context, ApplicationMode mode, ApplicationRule? rule,
        int modifiers, bool up, bool textKey, bool lowerIntegrity, bool allowActions, WordTrace? trace, string? earlierIgnore)
    {
        bool Reject(string reason, bool reset = false)
        {
            if (reset) Reset(reason);
            if (trace is not null) trace.Reason = reason;
            if (textKey && activity.Ignore(reason, Environment.TickCount64))
            {
                var ignored = CreateTrace(context, "ignored", activity.Length, activity.ResetReason);
                ignored.Reason = reason;
                PublishTrace(ignored);
            }
            return false;
        }

        var vk = (int)key.Vk;
        var isShift = vk is 0x10 or 0xA0 or 0xA1;
        if (lowerIntegrity) return Reject("injected-lower-integrity", reset: !up);
        if (!windows!.Matches(context)) return Reject(context.Ready ? "focus-mismatch" : "context-not-ready", reset: true);
        var accessible = context.Ready && !context.IsPassword && (!context.IsElevated || InputSender.IsCurrentProcessElevated);
        if (!accessible || mode == ApplicationMode.Disabled)
            return Reject(!accessible ? context.IsPassword ? "password" : "elevated-window" : rule is null ? "game-or-fullscreen" : "program-excluded", reset: true);
        if (isShift)
        {
            if (!up) { shift.Down(Environment.TickCount64); if (pressed.Any(k => k is not (0x10 or 0xA0 or 0xA1))) shift.OtherKey(); }
            else if (shift.Up(Environment.TickCount64))
            {
                Interlocked.Increment(ref shiftToken);
                if (allowActions && undo.Last is not null) ScheduleUndo(context);
            }
            else if (options.SingleShift && allowActions && shift.WasTap)
            {
                var token = Interlocked.Increment(ref shiftToken);
                _ = Task.Delay(400).ContinueWith(_ => Post(() =>
                {
                    if (token != shiftToken || !windows.CanEdit(context) || pressed.Count > 0) return;
                    var language = InputSender.CurrentLanguage();
                    var target = language == "en" ? Layouts.Russian : Layouts.English;
                    Reset("shift-layout-switch"); InputSender.SwitchLayout(context, target);
                }), TaskScheduler.Default);
            }
            return false;
        }
        if (up) return false;
        shift.OtherKey(); Interlocked.Increment(ref shiftToken);
        if (allowActions && vk == options.PauseKey && modifiers == options.PauseModifiers)
        { Reset("pause-hotkey"); TogglePause?.Invoke(); suppressed.Add(key.Vk); return true; }
        if (allowActions && vk == options.SelectionKey && modifiers == options.SelectionModifiers)
        {
            if (!windows.CanEdit(context)) return Reject("uia-pending");
            Reset("selection-hotkey"); busy = true; ConvertSelection?.Invoke(context); suppressed.Add(key.Vk); return true;
        }
        if (vk == 0x13 && allowActions && modifiers == 0)
        {
            if (undo.Last is not null) ScheduleUndo(context);
            else Manual(context);
            suppressed.Add(key.Vk); return true;
        }
        if ((modifiers & 11) != 0) return Reject("shortcut", reset: true);
        if (WordBuffer.Invalidates(vk)) return Reject(vk is >= 0x25 and <= 0x28 ? "arrow-key"
            : vk is 0x23 or 0x24 ? "home-end" : "editing-key", reset: true);
        if (vk is 0x14) return false;
        if (vk == 8) { undo.Invalidate(); lastWord = null; buffer.Backspace(); return false; }
        var languageId = InputSender.CurrentLanguage();
        var layout = detector.Profiles.FirstOrDefault(p => p.Layout.Id == languageId)?.Layout;
        if (layout is null) return Reject("unsupported-layout", reset: true);
        if (WordBuffer.IsBoundary(vk))
        {
            if ((modifiers & 4) != 0 && vk is 13 or 9) return Reject("shift-delimiter", reset: true);
            var word = buffer.Text; ClearBuffer();
            var delimiter = vk == 13 ? "\n" : vk == 9 ? "\t" : " ";
            undo.Invalidate();
            if (word.Length == 0)
            {
                lastWord = null;
                return Reject(trace?.ResetReason is not null ? "buffer-reset" : earlierIgnore ?? "empty-buffer");
            }
            lastWord = (word, layout.Id, delimiter, context);
            if (!allowActions || mode != ApplicationMode.Full || !options.Enabled)
                return Reject(!allowActions ? "deferred-input" : !options.Enabled ? "autocorrection-disabled" : "manual-only");
            if (context.Password == PasswordStatus.Pending) return Reject("uia-pending");
            if (PasswordHeuristic.IsSensitive(word))
            {
                lastWord = null;
                if (layout.Id == "en") return Reject("password-heuristic");
                if (trace is not null) { trace.Reason = "password-heuristic"; trace.Deferred = true; }
                Post(() =>
                {
                    try
                    {
                        if (windows.CanEdit(context))
                        {
                            var request = InputSender.RequestLayout(context, Layouts.English);
                            if (trace is not null)
                            {
                                trace.LayoutRequest = request;
                                if (request.Confirmed) trace.Decision = "layout-only";
                            }
                        }
                    }
                    finally { if (trace is not null) PublishTrace(trace); }
                });
                return false;
            }
            var evidence = trace is null ? null : new DetectionEvidence();
            var detected = detector.Detect(word, layout.Id, options.Threshold, learned: options.Learned, evidence: evidence, preferRussian: options.PreferRussian);
            if (trace is not null && evidence is not null)
            {
                trace.ScoreA = evidence.ScoreA;
                trace.ScoreB = evidence.ScoreB;
                trace.Confidence = evidence.Confidence;
            }
            var target = detected?.TargetLanguage ?? layout.Id;
            var replacement = detected?.Replacement ?? word;
            replacement = TextCorrections.Apply(replacement, options.Typos, options.Capitals, options.Yo, options.Replacements, options.YoWords);
            if (replacement == word) return Reject(evidence?.Reason ?? "detector-kept-source");
            if (ExclusionPolicy.IsExcepted(word, layout.Id, target, options.Words)) return Reject("word-excluded");
            Reserve(context, word, replacement, layout.Id, target, delimiter, word.Length, delimiter, false, trace);
            suppressed.Add(key.Vk); return true;
        }
        var physical = new PhysicalKey(vk, (int)key.Scan, (modifiers & 4) != 0, (Native.GetKeyState(0x14) & 1) != 0);
        var character = layout.Translate(physical);
        if (character is null) return Reject(vk == 0xE7 ? "unicode-packet" : vk == 0xE5 ? "ime-composition" : "unmapped-key", reset: true);
        if (buffer.Language is not null && buffer.Language != layout.Id) Reset("layout-changed");
        if (buffer.Keys.Count == WordBuffer.Capacity) ReportReset("buffer-overflow");
        bufferOwner = context;
        undo.Invalidate(); lastWord = null; buffer.Add(character.Value, physical, layout.Id);
        if (allowActions && options.Enabled && options.Early && mode == ApplicationMode.Full && context.Password != PasswordStatus.Pending && !PasswordHeuristic.IsSensitive(buffer.Text))
        {
            var text = buffer.Text;
            var detected = detector.Detect(text, layout.Id, options.Threshold, true);
            if (detected is not null && !ExclusionPolicy.IsExcepted(text, layout.Id, detected.TargetLanguage, options.Words))
            {
                // The current letter is suppressed and therefore must not be backspaced.
                Reserve(context, text, detected.Replacement, layout.Id, detected.TargetLanguage, "", text.Length - 1, text[^1].ToString(), false);
                suppressed.Add(key.Vk); return true;
            }
        }
        return false;
    }
    private static bool IsTextCandidate(int vk) => vk is 0xE5 or 0xE7 or >= 0x60 and <= 0x6F
        || Layouts.English.Translate(new PhysicalKey(vk, 0, false, false)) is not null;

    private WordTrace CreateTrace(WindowContext context, string kind, int length, string? reset,
        ApplicationRule? rule = null, bool? game = null)
    {
        var path = context.ProcessPath;
        if (path.Length == 0 && context.Identity.ProcessId != 0)
            path = Native.ProcessImage(context.Identity.ProcessId, out _);
        rule ??= ExclusionPolicy.RuleFor(path, options.Applications);
        return new WordTrace
        {
            Kind = kind, Process = path.Length > 0 ? ApplicationProfiles.ExecutableName(path) : "unknown",
            FocusClass = context.FocusClass.Length > 0 ? context.FocusClass : Native.ClassName(context.Focus == 0 ? context.Window : context.Focus),
            PasswordCheck = context.PasswordCheck, ProgramExcluded = rule is { Mode: not ApplicationMode.Full },
            GameOrFullscreen = game ?? ApplicationProfiles.IsGame(path, context.IsFullScreen, options.GameExecutables),
            ContextReady = context.Ready, ElevatedWindow = context.IsElevated,
            ContextError = context.ContextError, ContextWin32Error = context.ContextWin32Error, WordLength = length,
            BufferedLength = buffer.Keys.Count, FocusResets = focusResets, ResetReason = reset,
            LayoutBefore = kind == "buffer-reset" ? buffer.Language : InputSender.CurrentLanguage()
        };
    }
    private void ObserveFocus(WindowContext context)
    {
        if (!options.Debug) return;
        Trace?.Invoke(new WordTrace
        {
            Level = "DEBUG", Kind = "focus", Process = ApplicationProfiles.ExecutableName(context.ProcessPath), FocusClass = context.FocusClass,
            PasswordCheck = context.PasswordCheck, ProgramExcluded = ExclusionPolicy.RuleFor(context.ProcessPath, options.Applications) is { Mode: not ApplicationMode.Full },
            GameOrFullscreen = ApplicationProfiles.IsGame(context.ProcessPath, context.IsFullScreen, options.GameExecutables),
            ContextReady = context.Ready, ElevatedWindow = context.IsElevated, ContextError = context.ContextError, ContextWin32Error = context.ContextWin32Error, WordLength = 0,
            BufferedLength = 0, FocusResets = 0, LayoutBefore = InputSender.CurrentLanguage(),
            Decision = "focus-check", Reason = context.Ready ? context.IsPassword ? "password" : "context-ready" : "context-not-ready"
        });
    }
    private void PublishTrace(WordTrace trace)
    {
        if (!trace.TryPublish()) return;
        trace.LayoutAfter = InputSender.CurrentLanguage();
        Trace?.Invoke(trace);
    }
    private void Manual(WindowContext context)
    {
        if (!windows!.CanEdit(context)) return;
        var word = buffer.Text; var language = buffer.Language; var delimiter = "";
        if (word.Length == 0 && lastWord is { } last && windows!.Matches(last.Context))
        { word = last.Word; language = last.Language; delimiter = last.Delimiter; }
        if (word.Length == 0 || language is null || PasswordHeuristic.IsSensitive(word)) return;
        // Enter/Tab may already have submitted a message or moved focus: never erase across them.
        if (delimiter is "\n" or "\t") return;
        var source = detector.Profiles.First(p => p.Layout.Id == language).Layout;
        var target = detector.Profiles.FirstOrDefault(p => p.Layout.Id != language)?.Layout;
        if (target is null) return;
        Reserve(context, word, source.ConvertTo(word, target), language, target.Id, delimiter, word.Length + delimiter.Length, "", true);
    }
    private void ScheduleUndo(WindowContext context)
    {
        var last = undo.Take(context.Window, context.Generation); if (last is null) return;
        if (last.Delimiter is "\n" or "\t") return;
        busy = true;
        Post(() =>
        {
            try
            {
                if (windows!.CanEdit(context) && InputSender.SwitchLayout(context, detector.Profiles.First(p => p.Layout.Id == last.SourceLanguage).Layout) && windows.CanEdit(context))
                {
                    var result = ReplaceForContext(context, last.Replacement.Length + last.Delimiter.Length, last.Original, last.Delimiter);
                    if (result.Success && last.SourceLanguage != last.TargetLanguage) Learning?.Invoke(last.Original, last.SourceLanguage, last.TargetLanguage, true);
                }
            }
            finally { Reset("undo"); Finish(); }
        });
    }
    private void Reserve(WindowContext context, string original, string replacement, string source, string target, string delimiter, int erase, string fallback, bool manual, WordTrace? trace = null)
    {
        ClearBuffer(); lastWord = null; busy = true;
        if (trace is not null) trace.Deferred = true;
        cancelReserved = () =>
        {
            if (!windows!.Matches(context) || fallback.Length == 0) return;
            var fallbackResult = InputSender.Replace(0, "", fallback);
            if (trace is not null) trace.SendInput = fallbackResult;
        };
        Post(() =>
        {
            try
            {
                if (!busy) { if (trace is not null) trace.Reason = "queue-overflow"; return; }
                if (!windows!.CanEdit(context))
                {
                    if (trace is not null) trace.Reason = "target-unavailable-before-replacement";
                    cancelReserved?.Invoke();
                    return;
                }
                var targetLayout = detector.Profiles.First(p => p.Layout.Id == target).Layout;
                var layout = InputSender.RequestLayout(context, targetLayout);
                if (trace is not null) trace.LayoutRequest = layout;
                if (!layout.Confirmed || !windows.CanEdit(context))
                {
                    if (trace is not null) trace.Reason = layout.Confirmed ? "target-unavailable-after-layout" : "layout-not-confirmed";
                    cancelReserved?.Invoke();
                    Status?.Invoke("Раскладка окна недоступна"); return;
                }
                var input = ReplaceForContext(context, erase, replacement, delimiter);
                if (trace is not null)
                {
                    trace.SendInput = input;
                    trace.Decision = !input.Success ? "interrupted" : source == target ? "text-corrected" : "switched";
                    trace.Reason = input.Success ? "accepted" : "focus-changed-after-erase";
                }
                if (!input.Success) return;
                if(delimiter.Length==0)
                {
                    foreach(var character in replacement)
                    { var physical=targetLayout.PhysicalKeyFor(character); if(physical is null) { ClearBuffer(); break; } buffer.Add(character,physical,target); }
                    if (!buffer.IsEmpty) bufferOwner = context;
                }
                if (delimiter is not ("\n" or "\t")) undo.Set(new(original, replacement, source, target, delimiter, context.Window, context.Generation));
                if (manual) Learning?.Invoke(original, source, target, false);
                Corrected?.Invoke(source == target);
            }
            catch (InputInjectionException ex)
            {
                if (trace is not null) { trace.Reason = "sendinput-incomplete"; trace.SendInput = ex.Report; }
                throw;
            }
            catch
            {
                if (trace is not null) trace.Reason = "input-operation-error";
                throw;
            }
            finally
            {
                if (trace is not null) PublishTrace(trace);
                Finish();
            }
        });
    }
    private InputReport ReplaceForContext(WindowContext context, int erase, string text, string delimiter) =>
        erase > 0 && ApplicationProfiles.NeedsInputPause(context.ProcessPath, context.RootClass)
            ? InputSender.ReplaceWithPause(erase, text, delimiter, () => busy && windows!.CanEdit(context))
            : InputSender.Replace(erase, text, delimiter);
    private void Finish()
    {
        busy = false;
        cancelReserved=null;
        if (deferred.Count == 0) return;
        var events = deferred.ToArray(); deferred.Clear();
        foreach (var key in events) Process(key, false);
        InputSender.Replay(events);
    }
    public void Dispose()
    { if (disposed) return; disposed = true; Native.PostThreadMessage(threadId, 0x12, 0, 0); thread.Join(TimeSpan.FromSeconds(2)); started.Dispose(); }
}
