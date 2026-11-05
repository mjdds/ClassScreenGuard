using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClassScreenGuard.Controls;

/// <summary>
/// 单显示器输入隔离层。Alpha=1 使其在视觉上近乎完全透明，同时仍能接收鼠标和触摸。
/// </summary>
public sealed class LockWindow : Window
{
    private readonly Func<bool> _isLocked;

    public LockWindow(Func<bool> isLocked, Action? requestUnlock = null)
    {
        _isLocked = isLocked;

        SystemDecorations = SystemDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowState = WindowState.Normal;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.Manual;
        IsHitTestVisible = true;

        // 不能用完全透明，否则部分 Windows 版本会让鼠标穿透到底层窗口。
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        var shield = new Grid
        {
            Background = Background,
            IsHitTestVisible = true
        };

        if (requestUnlock != null)
        {
            var unlockButton = new Button
            {
                Content = "解锁",
                Width = 88,
                Height = 36,
                Margin = new Thickness(0, 0, 20, 20),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(Color.Parse("#CC2563EB")),
                Foreground = Brushes.White,
                Opacity = 0.8
            };
            unlockButton.Click += (_, _) => requestUnlock();
            shield.Children.Add(unlockButton);
        }

        Content = shield;

        Opened += (_, _) =>
        {
            NativeHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            FocusShield();
        };
        Closing += (_, e) =>
        {
            if (_isLocked())
            {
                e.Cancel = true;
            }
        };
        KeyDown += (_, e) => e.Handled = true;
        KeyUp += (_, e) => e.Handled = true;
        TextInput += (_, e) => e.Handled = true;
    }

    public IntPtr NativeHandle { get; private set; }

    public void FocusShield() => Activate();
}
