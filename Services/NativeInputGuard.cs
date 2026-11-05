using System.Runtime.InteropServices;

namespace ClassScreenGuard.Services;

/// <summary>
/// Windows 低级键盘钩子：提供全局锁屏热键，并在锁定时拦截常见退出组合键。
/// </summary>
public sealed class NativeInputGuard : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmQuit = 0x0012;

    private const uint VkTab = 0x09;
    private const uint VkEscape = 0x1B;
    private const uint VkSpace = 0x20;
    private const uint VkDelete = 0x2E;
    private const uint VkL = 0x4C;
    private const uint VkLwin = 0x5B;
    private const uint VkRwin = 0x5C;
    private const uint VkApps = 0x5D;
    private const uint VkF4 = 0x73;
    private const uint VkControl = 0x11;
    private const uint VkMenu = 0x12;
    private const uint VkShift = 0x10;

    private readonly HookProc _hookProc;
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private IntPtr _hookHandle;
    private uint _threadId;
    private volatile bool _locked;
    private volatile bool _passwordPromptOpen;
    private bool _windowsKeyDown;
    private bool _disposed;

    public NativeInputGuard()
    {
        _hookProc = HookCallback;
    }

    public event EventHandler? LockHotkeyPressed;

    /// <summary>钩子安装失败的 Win32 错误码；0 表示正常。</summary>
    public int HookError { get; private set; }

    public void SetLocked(bool locked) => _locked = locked;

    public void SetPasswordPromptOpen(bool open) => _passwordPromptOpen = open;

    public void Start()
    {
        if (_thread != null || _disposed)
        {
            return;
        }

        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "ClassScreenGuard.KeyboardHook"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void ThreadMain()
    {
        try
        {
            _threadId = GetCurrentThreadId();
            _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookProc, GetModuleHandle(null), 0);
            if (_hookHandle == IntPtr.Zero)
            {
                HookError = Marshal.GetLastWin32Error();
            }
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
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        try
        {
            var message = (int)wParam.ToInt64();
            var isDown = message is WmKeyDown or WmSysKeyDown;
            var isUp = message is WmKeyUp or WmSysKeyUp;
            if (!isDown && !isUp)
            {
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var key = data.VkCode;
            var control = IsKeyDown(VkControl);
            var alt = IsKeyDown(VkMenu);
            var shift = IsKeyDown(VkShift);

            // Ctrl+Alt+L 是全局锁屏热键，解锁后也用于重新聚焦密码框。
            if (isDown && key == VkL && control && alt)
            {
                LockHotkeyPressed?.Invoke(this, EventArgs.Empty);
                return new IntPtr(1);
            }

            if (key is VkLwin or VkRwin)
            {
                if (isDown)
                {
                    _windowsKeyDown = true;
                }
                else if (isUp)
                {
                    _windowsKeyDown = false;
                }

                if (_locked)
                {
                    return new IntPtr(1);
                }
            }

            // Ctrl+Alt+Delete 是 Windows 安全注意序列，始终放行。
            if (key == VkDelete && control && alt)
            {
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            if (_locked && ShouldSuppress(key, control, alt, shift))
            {
                return new IntPtr(1);
            }
        }
        catch
        {
            // 钩子异常不能影响 ClassIsland 或系统键盘输入。
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private bool ShouldSuppress(uint key, bool control, bool alt, bool shift)
    {
        // 密码框未打开时冻结全部普通按键，防止 Esc、空格、方向键控制当前应用。
        // 全局解锁热键和 Ctrl+Alt+Delete 已在此前单独处理。
        if (!_passwordPromptOpen)
        {
            return true;
        }

        // 密码框打开时只允许输入所需按键，系统切换和退出组合键仍然拦截。
        if (_windowsKeyDown && key is not (VkLwin or VkRwin))
        {
            return true;
        }

        return key switch
        {
            VkApps => true,
            VkF4 when alt => true,
            VkTab when alt => true,
            VkEscape when control || alt => true,
            VkSpace when alt => true,
            _ => false
        };
    }

    private static bool IsKeyDown(uint key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int nCode,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

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
