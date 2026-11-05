using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClassScreenGuard.Controls;

/// <summary>仅在管理员按热键后出现的小型密码解锁窗口。</summary>
public sealed class UnlockWindow : Window
{
    private readonly Func<string, bool> _tryUnlock;
    private readonly TextBox _passwordBox;
    private readonly TextBlock _statusText;

    public UnlockWindow(Func<string, bool> tryUnlock)
    {
        _tryUnlock = tryUnlock;

        Width = 430;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        Background = new SolidColorBrush(Color.Parse("#FF172033"));

        _passwordBox = new TextBox
        {
            Watermark = "管理员密码",
            PasswordChar = '•',
            FontSize = 20,
            MinHeight = 46
        };
        _passwordBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                AttemptUnlock();
            }
        };

        var unlockButton = new Button
        {
            Content = "解锁",
            MinWidth = 110,
            MinHeight = 38,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.Parse("#FF2563EB")),
            Foreground = Brushes.White
        };
        unlockButton.Click += (_, _) => AttemptUnlock();

        var cancelButton = new Button
        {
            Content = "取消",
            MinWidth = 90,
            MinHeight = 38,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        cancelButton.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 12,
            Children = { unlockButton, cancelButton }
        };

        _statusText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#FFFCA5A5")),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MinHeight = 22
        };

        Content = new Border
        {
            Padding = new Thickness(26),
            BorderBrush = new SolidColorBrush(Color.Parse("#6688AFFF")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = new StackPanel
            {
                Spacing = 13,
                Children =
                {
                    new TextBlock
                    {
                        Text = "输入密码解除操作锁定",
                        Foreground = Brushes.White,
                        FontSize = 22,
                        FontWeight = FontWeight.SemiBold,
                        TextAlignment = TextAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "可点击「解锁」或按 Enter；密码错误可不限次数重试。",
                        Foreground = Brushes.White,
                        Opacity = 0.65,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center
                    },
                    _passwordBox,
                    buttons,
                    _statusText
                }
            }
        };

        Opened += (_, _) =>
        {
            NativeHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            FocusPassword();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
            else if (e.Key == Key.F4)
            {
                e.Handled = true;
            }
        };
    }

    public IntPtr NativeHandle { get; private set; }

    public void FocusPassword()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Activate();
            _passwordBox.Focus();
        });
    }

    private void AttemptUnlock()
    {
        if (_tryUnlock(_passwordBox.Text ?? string.Empty))
        {
            return;
        }

        _statusText.Text = "密码错误，可继续重试（不限次数）。";
        _passwordBox.Text = string.Empty;
        _passwordBox.Focus();
    }
}
