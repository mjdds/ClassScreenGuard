using System.Runtime.InteropServices;
using System.Text;

namespace ClassScreenGuard.Services;

/// <summary>
/// 在鼠标消息到达窗口前按目标窗口隔离输入，并关闭锁定期间出现的 Shell 浮层。
/// </summary>
public sealed class NativePointerGuard : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmQuit = 0x0012;
    private const int ObjIdWindow = 0;
    private const int SwHide = 0;
    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectShow = 0x8002;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint GaRoot = 2;

    private static readonly HashSet<string> BlockedShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ShellExperienceHost.exe",
        "StartMenuExperienceHost.exe",
        "SearchHost.exe",
        "SearchApp.exe"
    };

    private static readonly HashSet<string> VirtualKeyboardProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "TextInputHost.exe",
        "TabTip.exe",
        "osk.exe"
    };

    private readonly HookProc _hookProc;
    private readonly WinEventProc _winEventProc;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly object _allowedWindowsSync = new();
    private readonly HashSet<IntPtr> _allowedWindows = new();
    private readonly Dictionary<uint, string?> _processNames = new();
    private Thread? _thread;
    private IntPtr _mouseHookHandle;
    private IntPtr _showEventHookHandle;
    private IntPtr _foregroundEventHookHandle;
    private uint _threadId;
    private volatile bool _locked;
    private volatile bool _passwordPromptOpen;
    private bool _disposed;

    public NativePointerGuard()
    {
        _hookProc = MouseHookCallback;
        _winEventProc = WinEventCallback;
    }

    public event EventHandler? ForeignWindowActivated;

    public int HookError { get; private set; }

    public void SetLocked(bool locked)
    {
        _locked = locked;
        if (!locked)
        {
            lock (_allowedWindowsSync)
            {
                _processNames.Clear();
            }
        }
    }

    public void SetPasswordPromptOpen(bool open) => _passwordPromptOpen = open;

    public void SetAllowedWindows(IEnumerable<IntPtr> handles)
    {
        lock (_allowedWindowsSync)
        {
            _allowedWindows.Clear();
            foreach (var handle in handles)
            {
                if (handle != IntPtr.Zero)
                {
                    _allowedWindows.Add(handle);
                }
            }
        }
    }

    public void Start()
    {
        if (_thread != null || _disposed)
        {
            return;
        }

        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "ClassScreenGuard.PointerHook"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public void SuppressShellWindows()
    {
        if (!_locked)
        {
            return;
        }

        EnumWindows((handle, _) =>
        {
            if (IsWindowVisible(handle) && !IsAllowedWindow(handle) && IsBlockedShellWindow(handle))
            {
                ShowWindowAsync(handle, SwHide);
            }

            return true;
        }, IntPtr.Zero);
    }

    private void ThreadMain()
    {
        try
        {
            _threadId = GetCurrentThreadId();
            _mouseHookHandle = SetWindowsHookEx(WhMouseLl, _hookProc, GetModuleHandle(null), 0);
            if (_mouseHookHandle == IntPtr.Zero)
            {
                HookError = Marshal.GetLastWin32Error();
            }

            _showEventHookHandle = SetWinEventHook(
                EventObjectShow, EventObjectShow, IntPtr.Zero, _winEventProc, 0, 0, WinEventOutOfContext);
            _foregroundEventHookHandle = SetWinEventHook(
                EventSystemForeground, EventSystemForeground, IntPtr.Zero, _winEventProc, 0, 0, WinEventOutOfContext);
        }
        catch
        {
            HookError = -1;
        }
        finally
        {
            _ready.Set();
        }

        try
        {
            while (true)
            {
                var result = GetMessage(out var message, IntPtr.Zero, 0, 0);
                if (result <= 0)
                {
                    break;
                }

                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        finally
        {
            if (_mouseHookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookHandle);
                _mouseHookHandle = IntPtr.Zero;
            }

            if (_showEventHookHandle != IntPtr.Zero)
            {
                UnhookWinEvent(_showEventHookHandle);
                _showEventHookHandle = IntPtr.Zero;
            }

            if (_foregroundEventHookHandle != IntPtr.Zero)
            {
                UnhookWinEvent(_foregroundEventHookHandle);
                _foregroundEventHookHandle = IntPtr.Zero;
            }
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _locked)
        {
            try
            {
                var data = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
                var target = WindowFromPoint(data.Point);
                if (target == IntPtr.Zero ||
                    (!IsAllowedWindow(target) && !IsVirtualKeyboardWindow(target)))
                {
                    return new IntPtr(1);
                }
            }
            catch
            {
                return new IntPtr(1);
            }
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private void WinEventCallback(
        IntPtr hook,
        uint eventType,
        IntPtr handle,
        int objectId,
        int childId,
        uint eventThreadId,
        uint eventTime)
    {
        if (!_locked || handle == IntPtr.Zero || objectId != ObjIdWindow || IsAllowedWindow(handle))
        {
            return;
        }

        if (IsVirtualKeyboardWindow(handle))
        {
            return;
        }

        try
        {
            if (IsBlockedShellWindow(handle))
            {
                ShowWindowAsync(handle, SwHide);
            }

            if (eventType == EventSystemForeground)
            {
                ForeignWindowActivated?.Invoke(this, EventArgs.Empty);
            }
        }
        catch
        {
            // 单个 Shell 窗口处理失败不能终止钩子线程。
        }
    }

    public bool IsVirtualKeyboardWindow(IntPtr handle)
    {
        if (!_passwordPromptOpen || handle == IntPtr.Zero)
        {
            return false;
        }

        var root = GetAncestor(handle, GaRoot);
        if (root != IntPtr.Zero)
        {
            handle = root;
        }

        var className = GetWindowClassName(handle);
        if (className is not ("Windows.UI.Core.CoreWindow" or "ApplicationFrameWindow" or
            "IPTip_Main_Window" or "OSKMainClass"))
        {
            return false;
        }

        GetWindowThreadProcessId(handle, out var processId);
        var processName = GetProcessName(processId);
        return processName != null && VirtualKeyboardProcesses.Contains(processName);
    }

    private bool IsAllowedWindow(IntPtr handle)
    {
        var root = GetAncestor(handle, GaRoot);
        lock (_allowedWindowsSync)
        {
            return _allowedWindows.Contains(handle) ||
                   (root != IntPtr.Zero && _allowedWindows.Contains(root));
        }
    }

    private bool IsBlockedShellWindow(IntPtr handle)
    {
        var className = GetWindowClassName(handle);
        if (className is not ("Windows.UI.Core.CoreWindow" or "ApplicationFrameWindow"))
        {
            return false;
        }

        GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0 || processId == Environment.ProcessId)
        {
            return false;
        }

        var processName = GetProcessName(processId);
        if (string.IsNullOrEmpty(processName))
        {
            return false;
        }

        return BlockedShellProcesses.Contains(processName) ||
               (processName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) &&
                className == "Windows.UI.Core.CoreWindow");
    }

    private string? GetProcessName(uint processId)
    {
        lock (_allowedWindowsSync)
        {
            if (_processNames.TryGetValue(processId, out var cached))
            {
                return cached;
            }
        }

        string? result = null;
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process != IntPtr.Zero)
        {
            try
            {
                var path = new StringBuilder(1024);
                var length = path.Capacity;
                if (QueryFullProcessImageName(process, 0, path, ref length))
                {
                    result = Path.GetFileName(path.ToString());
                }
            }
            finally
            {
                CloseHandle(process);
            }
        }

        lock (_allowedWindowsSync)
        {
            _processNames[processId] = result;
        }

        return result;
    }

    private static string GetWindowClassName(IntPtr handle)
    {
        var className = new StringBuilder(256);
        return GetClassName(handle, className, className.Capacity) > 0
            ? className.ToString()
            : string.Empty;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _locked = false;
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        if (_thread != null && Thread.CurrentThread != _thread)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private delegate void WinEventProc(
        IntPtr hook,
        uint eventType,
        IntPtr handle,
        int objectId,
        int childId,
        uint eventThreadId,
        uint eventTime);

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        HookProc callback,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr module,
        WinEventProc callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        IntPtr process,
        uint flags,
        StringBuilder path,
        ref int length);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(
        out NativeMessage message,
        IntPtr window,
        uint minFilter,
        uint maxFilter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(
        uint threadId,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
