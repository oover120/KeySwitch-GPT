using System.Runtime.InteropServices;
using System.Text;
namespace KeySwitch.Hooks;

internal static class Native
{
    internal static readonly nuint Marker = 0x4B535754;
    internal delegate nint HookProc(int code, nint message, nint data);
    internal delegate void WinEventProc(nint hook, uint eventId, nint hwnd, int objectId, int child, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardEvent { public uint Vk, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseEvent { public Point Position; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct Message { public nint Hwnd; public uint Id; public nuint WParam; public nint LParam; public uint Time; public Point Point; public uint Private; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion { [FieldOffset(0)] public KeyboardInput Key; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiThreadInfo { public uint Size, Flags; public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret; public Rect CaretRect; }
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint threadId);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] internal static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint LoadKeyboardLayout(string klid, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool PostMessage(nint hwnd, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(nint hwnd);
    [DllImport("shell32.dll")] internal static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] internal static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern int GetMessage(out Message message, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] internal static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll")] internal static extern bool PostThreadMessage(uint thread, uint message, nuint wparam, nint lparam);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern nint OpenProcess(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref int length);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool GetTokenInformation(nint token, int kind, out int value, int length, out int returned);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    internal static nint Focus(nint hwnd)
    {
        if (hwnd == 0) return 0;
        var thread = GetWindowThreadProcessId(hwnd, out _);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return thread != 0 && GetGUIThreadInfo(thread, ref info) ? info.Focus : 0;
    }
    internal static string ClassName(nint hwnd)
    {
        var name = new StringBuilder(256);
        GetClassName(hwnd, name, name.Capacity);
        return name.ToString();
    }
    internal static string ProcessImage(uint pid, out int error)
    {
        var process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == 0) { error = Marshal.GetLastPInvokeError(); return ""; }
        try
        {
            var path = new StringBuilder(32768);
            var length = path.Capacity;
            if (!QueryFullProcessImageName(process, 0, path, ref length))
            { error = Marshal.GetLastPInvokeError(); return ""; }
            error = 0;
            return path.ToString();
        }
        finally { CloseHandle(process); }
    }
    internal static Input Key(ushort vk, ushort scan = 0, uint flags = 0) => new() { Type = 1, Data = new() { Key = new() { Vk = vk, Scan = scan, Flags = flags, Extra = Marker } } };
    internal static InputReport Send(IReadOnlyCollection<Input> inputs)
    {
        var array = inputs.ToArray();
        var sent = array.Length == 0 ? 0 : SendInput((uint)array.Length, array, Marshal.SizeOf<Input>());
        var report = new InputReport(array.Length, sent, sent == array.Length ? 0 : Marshal.GetLastPInvokeError());
        if (!report.Success) throw new InputInjectionException(report);
        return report;
    }
    internal static void Tap(List<Input> inputs, ushort key) { inputs.Add(Key(key)); inputs.Add(Key(key, flags: 2)); }
    internal static bool IsElevated(uint id)
    {
        var process = OpenProcess(0x1000, false, id); if (process == 0) return true;
        try { if (!OpenProcessToken(process, 8, out var token)) return true; try { return !GetTokenInformation(token, 20, out var elevated, 4, out _) || elevated != 0; } finally { CloseHandle(token); } }
        finally { CloseHandle(process); }
    }
}
