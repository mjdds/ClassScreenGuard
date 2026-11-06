using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClassScreenGuard.Services;

namespace ClassScreenGuard.Controls;

/// <summary>普通密码解锁窗口；Admin 使用全局连续输入，不提供隐藏界面。</summary>
public sealed class UnlockWindow : Window
{
    private const string NormalHint = "普通密码最多输错 10 次，之后需等待 5 分钟。";

    private readonly GuardSettingsService _settings;
    private readonly Action _unlock;
    private readonly DispatcherTimer _cooldownTimer;
    private readonly TextBox _passwordBox;
    private readonly TextBlock _titleText;
    private readonly TextBlock _hintText;
    private readonly TextBlock _statusText;
    private readonly Button _unlockButton;
    private bool _isCoolingDown;

    public UnlockWindow(GuardSettingsService settings, Action unlock)
    {
        _settings = settings;
        _unlock = unlock;

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
            Watermark = "请输入普通密码",
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

        _unlockButton = new Button
        {
            Content = "解锁",
            MinWidth = 110,
            MinHeight = 38,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.Parse("#FF2563EB")),
            Foreground = Brushes.White
        };
        _unlockButton.Click += (_, _) => AttemptUnlock();

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
            Children = { _unlockButton, cancelButton }
        };

        _titleText = new TextBlock
        {
            Text = "请输入密码",
            Foreground = Brushes.White,
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center
        };
        _hintText = new TextBlock
        {
            Text = NormalHint,
            Foreground = Brushes.White,
            Opacity = 0.65,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center
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
                    _titleText,
                    _hintText,
                    _passwordBox,
                    buttons,
                    _statusText
                }
            }
        };

        _cooldownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _cooldownTimer.Tick += (_, _) => RefreshCooldownState();

        Opened += (_, _) =>
        {
            NativeHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            RefreshCooldownState();
            FocusPassword();
        };
        Closed += (_, _) => _cooldownTimer.Stop();
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
        if (_isCoolingDown)
        {
            return;
        }

        var result = _settings.TryNormalPassword(_passwordBox.Text);
        switch (result.Status)
        {
            case NormalPasswordAttemptStatus.Success:
                _unlock();
                return;
            case NormalPasswordAttemptStatus.Cooldown:
                EnterCooldown(result.RetryAfter);
                return;
            default:
                var remaining = GuardSettingsService.MaxNormalPasswordAttempts - result.FailedAttempts;
                _statusText.Text = $"密码错误，还可尝试 {remaining} 次。";
                ClearAndFocusPassword();
                return;
        }
    }

    private void RefreshCooldownState()
    {
        var remaining = _settings.GetLockoutRemaining();
        if (remaining > TimeSpan.Zero)
        {
            EnterCooldown(remaining);
            return;
        }

        var wasCoolingDown = _isCoolingDown;
        _cooldownTimer.Stop();
        _isCoolingDown = false;
        _passwordBox.IsEnabled = true;
        _unlockButton.IsEnabled = true;
        _hintText.Text = NormalHint;

        if (wasCoolingDown)
        {
            _statusText.Text = "等待时间已结束，请重新输入密码。";
            ClearAndFocusPassword();
        }
    }

    private void EnterCooldown(TimeSpan remaining)
    {
        if (!_isCoolingDown)
        {
            _isCoolingDown = true;
            _passwordBox.Text = string.Empty;
            _cooldownTimer.Start();
        }

        _passwordBox.IsEnabled = false;
        _unlockButton.IsEnabled = false;
        _statusText.Text = "请等待5分钟后重试";
        _hintText.Text = $"普通密码已连续输错 10 次，剩余 {FormatRemaining(remaining)}。";
    }

    private void ClearAndFocusPassword()
    {
        _passwordBox.Text = string.Empty;
        _passwordBox.Focus();
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        return $"{seconds / 60:D2}:{seconds % 60:D2}";
    }
}
