using System.Runtime.InteropServices;
using Avalonia.Platform;
using Avalonia.Threading;
using ClassScreenGuard.Controls;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;

namespace ClassScreenGuard.Services;

/// <summary>管理透明输入隔离层、按需密码框和插件生命周期。</summary>
public sealed class ScreenGuardService : IHostedService, IDisposable
{
    private readonly GuardSettingsService _settings;
    private readonly NativeInputGuard _inputGuard = new();
    private readonly NativePointerGuard _pointerGuard = new();
    private readonly AdminPasswordSequenceDetector _adminSequence;
    private readonly List<LockWindow> _windows = new();
    private DispatcherTimer? _foregroundTimer;
    private DispatcherTimer? _scheduleTimer;
    private UnlockWindow? _unlockWindow;
    private AdminUnlockNoticeWindow? _adminNoticeWindow;
    private bool _systemShutdown;
    private bool _disposed;

    public ScreenGuardService(GuardSettingsService settings)
    {
        _settings = settings;
        _adminSequence = new AdminPasswordSequenceDetector(settings);
        _adminSequence.UnlockRequested += () => RunOnUiThread(AdminUnlockOnUiThread);
        _inputGuard.LockHotkeyPressed += (_, _) => RequestLock();
        _inputGuard.TextTyped += _adminSequence.Append;
        _inputGuard.TextSequenceInterrupted += _adminSequence.Interrupt;
        _pointerGuard.ForeignWindowActivated += (_, _) => RunOnUiThread(KeepWindowsInForeground);
    }

    public bool IsLocked { get; private set; }

    public bool CanLock => _settings.HasPassword;

    public bool IsLockAllowedNow => _settings.IsLockAllowedNow();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _inputGuard.Start();
        _pointerGuard.Start();
        SystemEvents.SessionEnding += OnSessionEnding;
        RunOnUiThread(() =>
        {
            _scheduleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _scheduleTimer.Tick += (_, _) => EnforceLockSchedule();
            _scheduleTimer.Start();

            if (_settings.ConsumeResumeLockAfterLogoff() && _settings.HasPassword &&
                _settings.IsLockAllowedNow())
            {
                LockOnUiThread();
            }
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _settings.SetResumeLockAfterLogoff(IsLocked && !_systemShutdown);
        SystemEvents.SessionEnding -= OnSessionEnding;
        RunOnUiThread(() =>
        {
            _scheduleTimer?.Stop();
            _scheduleTimer = null;
            UnlockOnUiThread();
        });
        _adminSequence.Dispose();
        _inputGuard.Dispose();
        _pointerGuard.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>未锁定时锁定；已锁定时只显示小密码框。</summary>
    public void RequestLock()
    {
        if (_disposed)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (IsLocked)
            {
                ShowUnlockPromptOnUiThread();
            }
            else if (_settings.HasPassword)
            {
                LockOnUiThread();
            }
        });
    }

    private void LockOnUiThread()
    {
        if (_disposed || IsLocked || !_settings.HasPassword || !_settings.IsLockAllowedNow())
        {
            return;
        }

        IsLocked = true;
        _adminSequence.SetActive(true);
        _adminNoticeWindow?.Close();
        _adminNoticeWindow = null;
        _inputGuard.SetPasswordPromptOpen(false);
        _pointerGuard.SetPasswordPromptOpen(false);
        _inputGuard.SetLocked(true);
        _pointerGuard.SetAllowedWindows(Array.Empty<IntPtr>());
        _pointerGuard.SetLocked(true);

        try
        {
            var primaryWindow = CreateShieldWindow(true);
            var screens = primaryWindow.Screens.All.ToList();
            var primaryScreen = primaryWindow.Screens.Primary ?? screens.FirstOrDefault();

            if (primaryScreen != null)
            {
                PositionWindow(primaryWindow, primaryScreen);
            }

            foreach (var screen in screens)
            {
                if (primaryScreen != null && screen.Bounds == primaryScreen.Bounds)
                {
                    continue;
                }

                PositionWindow(CreateShieldWindow(false), screen);
            }

            UpdateAllowedWindows();
            _foregroundTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _foregroundTimer.Tick += (_, _) =>
            {
                _pointerGuard.SuppressShellWindows();
                KeepWindowsInForeground();
            };
            _foregroundTimer.Start();

            _pointerGuard.SuppressShellWindows();
            KeepWindowsInForeground();
            primaryWindow.FocusShield();
        }
        catch
        {
            // 隔离层创建失败时必须恢复输入，避免无窗口但键盘仍被锁住。
            UnlockOnUiThread();
        }
    }

    private LockWindow CreateShieldWindow(bool showUnlockButton)
    {
        var window = new LockWindow(
            () => IsLocked,
            showUnlockButton ? ShowUnlockPromptOnUiThread : null);
        window.Show();
        _windows.Add(window);
        return window;
    }

    private void ShowUnlockPromptOnUiThread()
    {
        if (!IsLocked)
        {
            return;
        }

        if (_unlockWindow != null)
        {
            var foreground = NativeWindowMethods.GetForegroundWindow();
            if (foreground != _unlockWindow.NativeHandle &&
                !_pointerGuard.IsVirtualKeyboardWindow(foreground))
            {
                _unlockWindow.FocusPassword();
            }
            return;
        }

        _inputGuard.SetPasswordPromptOpen(true);
        _pointerGuard.SetPasswordPromptOpen(true);
        var prompt = new UnlockWindow(_settings, UnlockOnUiThread);
        _unlockWindow = prompt;
        prompt.Closed += (_, _) =>
        {
            if (ReferenceEquals(_unlockWindow, prompt))
            {
                _unlockWindow = null;
            }

            _inputGuard.SetPasswordPromptOpen(false);
            _pointerGuard.SetPasswordPromptOpen(false);
            UpdateAllowedWindows();
            if (IsLocked)
            {
                _windows.FirstOrDefault()?.FocusShield();
            }
        };
        prompt.Show();
        UpdateAllowedWindows();
        KeepWindowsInForeground();
    }

    private void UnlockOnUiThread()
    {
        _foregroundTimer?.Stop();
        _foregroundTimer = null;
        _adminSequence.SetActive(false);
        IsLocked = false;
        _inputGuard.SetPasswordPromptOpen(false);
        _pointerGuard.SetPasswordPromptOpen(false);

        try
        {
            _unlockWindow?.Close();
        }
        catch
        {
            // 继续关闭其余隔离层。
        }
        _unlockWindow = null;

        foreach (var window in _windows.ToList())
        {
            try
            {
                window.Close();
            }
            catch
            {
                // 继续关闭其余窗口。
            }
        }

        _windows.Clear();
        _inputGuard.SetLocked(false);
        _pointerGuard.SetAllowedWindows(Array.Empty<IntPtr>());
        _pointerGuard.SetLocked(false);
    }

    private void AdminUnlockOnUiThread()
    {
        if (!IsLocked)
        {
            return;
        }

        UnlockOnUiThread();
        _adminNoticeWindow?.Close();
        var notice = new AdminUnlockNoticeWindow();
        _adminNoticeWindow = notice;
        notice.Closed += (_, _) =>
        {
            if (ReferenceEquals(_adminNoticeWindow, notice))
            {
                _adminNoticeWindow = null;
            }
        };
        notice.Show();
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs args)
    {
        if (args.Reason == SessionEndReasons.Logoff)
        {
            _settings.SetResumeLockAfterLogoff(IsLocked);
            return;
        }

        _systemShutdown = true;
        _settings.SetResumeLockAfterLogoff(false);
    }

    private void EnforceLockSchedule()
    {
        if (IsLocked && !_settings.IsLockAllowedNow())
        {
            UnlockOnUiThread();
        }
    }

    private void UpdateAllowedWindows()
    {
        var handles = _windows
            .Select(window => window.NativeHandle)
            .Where(handle => handle != IntPtr.Zero)
            .ToList();
        if (_unlockWindow != null && _unlockWindow.NativeHandle != IntPtr.Zero)
        {
            handles.Add(_unlockWindow.NativeHandle);
        }

        _pointerGuard.SetAllowedWindows(handles);
    }

    private void KeepWindowsInForeground()
    {
        if (!IsLocked)
        {
            return;
        }

        foreach (var window in _windows)
        {
            if (window.NativeHandle != IntPtr.Zero)
            {
                NativeWindowMethods.SetTopmost(window.NativeHandle);
            }
        }

        var foreground = NativeWindowMethods.GetForegroundWindow();
        var virtualKeyboardFocused = _pointerGuard.IsVirtualKeyboardWindow(foreground);

        if (_unlockWindow != null)
        {
            _unlockWindow.Topmost = true;
            if (_unlockWindow.NativeHandle != IntPtr.Zero)
            {
                NativeWindowMethods.SetTopmost(_unlockWindow.NativeHandle);
                if (foreground == _unlockWindow.NativeHandle || virtualKeyboardFocused)
                {
                    return;
                }

                NativeWindowMethods.SetForegroundWindow(_unlockWindow.NativeHandle);
            }

            if (!virtualKeyboardFocused)
            {
                _unlockWindow.Activate();
            }
            return;
        }

        var primary = _windows.FirstOrDefault();
        if (primary == null || virtualKeyboardFocused)
        {
            return;
        }

        primary.Topmost = true;
        if (_windows.Any(window => window.NativeHandle == foreground))
        {
            return;
        }

        primary.Activate();
        if (primary.NativeHandle != IntPtr.Zero)
        {
            NativeWindowMethods.SetForegroundWindow(primary.NativeHandle);
        }
    }

    private static void PositionWindow(LockWindow window, Screen screen)
    {
        var bounds = screen.Bounds;
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
        window.Position = bounds.TopLeft;
        window.Width = bounds.Width / scaling;
        window.Height = bounds.Height / scaling;

        if (window.NativeHandle != IntPtr.Zero)
        {
            NativeWindowMethods.SetBounds(
                window.NativeHandle,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height);
        }
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.SessionEnding -= OnSessionEnding;
        RunOnUiThread(() =>
        {
            _scheduleTimer?.Stop();
            _scheduleTimer = null;
            UnlockOnUiThread();
        });
        _adminSequence.Dispose();
        _inputGuard.Dispose();
        _pointerGuard.Dispose();
    }
}

internal static class NativeWindowMethods
{
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    public static void SetTopmost(IntPtr handle) =>
        SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0,
            SwpNoActivate | SwpShowWindow | 0x0001 | 0x0002);

    public static void SetBounds(IntPtr handle, int x, int y, int width, int height) =>
        SetWindowPos(handle, HwndTopmost, x, y, width, height, SwpNoActivate | SwpShowWindow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
}
