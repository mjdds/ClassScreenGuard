using System.Runtime.InteropServices;
using Avalonia.Platform;
using Avalonia.Threading;
using ClassScreenGuard.Controls;
using Microsoft.Extensions.Hosting;

namespace ClassScreenGuard.Services;

/// <summary>管理透明输入隔离层、按需密码框和插件生命周期。</summary>
public sealed class ScreenGuardService : IHostedService, IDisposable
{
    private readonly GuardSettingsService _settings;
    private readonly NativeInputGuard _inputGuard = new();
    private readonly List<LockWindow> _windows = new();
    private DispatcherTimer? _foregroundTimer;
    private UnlockWindow? _unlockWindow;
    private bool _disposed;

    public ScreenGuardService(GuardSettingsService settings)
    {
        _settings = settings;
        _inputGuard.LockHotkeyPressed += (_, _) => RequestLock();
    }

    public bool IsLocked { get; private set; }

    public bool CanLock => _settings.HasPassword;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _inputGuard.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        RunOnUiThread(UnlockOnUiThread);
        _inputGuard.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>未锁定时锁定；已锁定时只显示小密码框。</summary>
    public void RequestLock()
    {
        if (_disposed || !_settings.HasPassword)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            if (IsLocked)
            {
                ShowUnlockPromptOnUiThread();
            }
            else
            {
                LockOnUiThread();
            }
        });
    }

    private void LockOnUiThread()
    {
        if (_disposed || IsLocked || !_settings.HasPassword)
        {
            return;
        }

        IsLocked = true;
        _inputGuard.SetPasswordPromptOpen(false);
        _inputGuard.SetLocked(true);

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

            _foregroundTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _foregroundTimer.Tick += (_, _) => KeepWindowsInForeground();
            _foregroundTimer.Start();

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
            _unlockWindow.FocusPassword();
            return;
        }

        _inputGuard.SetPasswordPromptOpen(true);
        var prompt = new UnlockWindow(TryUnlock);
        _unlockWindow = prompt;
        prompt.Closed += (_, _) =>
        {
            if (ReferenceEquals(_unlockWindow, prompt))
            {
                _unlockWindow = null;
            }

            _inputGuard.SetPasswordPromptOpen(false);
            if (IsLocked)
            {
                _windows.FirstOrDefault()?.FocusShield();
            }
        };
        prompt.Show();
        KeepWindowsInForeground();
        prompt.FocusPassword();
    }

    private bool TryUnlock(string password)
    {
        if (!_settings.VerifyPassword(password))
        {
            return false;
        }

        RunOnUiThread(UnlockOnUiThread);
        return true;
    }

    private void UnlockOnUiThread()
    {
        _foregroundTimer?.Stop();
        _foregroundTimer = null;
        IsLocked = false;
        _inputGuard.SetPasswordPromptOpen(false);

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

        if (_unlockWindow != null)
        {
            _unlockWindow.Topmost = true;
            if (_unlockWindow.NativeHandle != IntPtr.Zero)
            {
                NativeWindowMethods.SetTopmost(_unlockWindow.NativeHandle);
                NativeWindowMethods.SetForegroundWindow(_unlockWindow.NativeHandle);
            }
            _unlockWindow.Activate();
            return;
        }

        var primary = _windows.FirstOrDefault();
        if (primary == null)
        {
            return;
        }

        primary.Topmost = true;
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
        RunOnUiThread(UnlockOnUiThread);
        _inputGuard.Dispose();
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
}
