using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassScreenGuard.Services;

namespace ClassScreenGuard.Controls;

/// <summary>锁屏密码设置与手动锁定页面。</summary>
[SettingsPageInfo("classScreenGuard.settings", "屏幕操作锁设置", "\ue72e", "\ue72e")]
public sealed class GuardSettingsPage : SettingsPageBase
{
    private readonly GuardSettingsService _settings;
    private readonly ScreenGuardService _guard;
    private readonly TextBox _passwordBox;
    private readonly TextBox _confirmBox;
    private readonly TextBlock _message;
    private readonly TextBlock _passwordState;
    private readonly TextBox _currentAdminPasswordBox;
    private readonly TextBox _adminPasswordBox;
    private readonly TextBox _adminConfirmBox;
    private readonly TextBlock _adminMessage;
    private readonly TextBlock _adminPasswordState;
    private readonly StackPanel _passwordEditors;
    private readonly TextBlock _versionHint;
    private readonly TextBlock _lockMessage;
    private readonly CheckBox _scheduleEnabled;
    private readonly TextBox _scheduleStart;
    private readonly TextBox _scheduleEnd;
    private readonly TextBlock _scheduleMessage;
    private int _versionClickCount;
    private DateTimeOffset _lastVersionClick;

    public GuardSettingsPage(GuardSettingsService settings, ScreenGuardService guard)
    {
        _settings = settings;
        _guard = guard;

        var root = new StackPanel { Margin = new Thickness(24), Spacing = 14, MaxWidth = 760 };
        root.Children.Add(new TextBlock
        {
            Text = "教室屏幕操作锁",
            FontSize = 28,
            FontWeight = FontWeight.Bold
        });
        root.Children.Add(Hint("设置密码后，按 Ctrl+Alt+L 锁定鼠标、触摸和键盘。当前画面和正在播放的内容保持可见；再按一次热键才会出现小密码框。"));

        var version = typeof(GuardSettingsPage).Assembly.GetName().Version?.ToString() ?? "未知";
        var versionText = new TextBlock
        {
            Text = $"版本 {version}",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold
        };
        versionText.Tapped += (_, _) => RevealPasswordEditors();
        root.Children.Add(versionText);
        _versionHint = Hint("需要设置或修改密码？请阅读 GitHub README 中的「密码设置入口」。");
        root.Children.Add(_versionHint);
        var readmeButton = new Button
        {
            Content = "打开密码设置说明（GitHub README）",
            MinHeight = 38,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        readmeButton.Click += (_, _) => OpenPasswordSetupReadme();
        root.Children.Add(readmeButton);

        _passwordEditors = new StackPanel { Spacing = 14, IsVisible = false };
        root.Children.Add(_passwordEditors);

        _passwordState = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold };
        _passwordEditors.Children.Add(Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Title("1. 设置解锁密码"),
                _passwordState,
                Hint("密码至少 4 个字符，建议 8 位以上。密码只以哈希形式保存，不会保存明文。")
            }
        }));

        _passwordBox = PasswordBox("新密码");
        _confirmBox = PasswordBox("再次输入新密码");

        var saveButton = new Button
        {
            Content = "保存解锁密码",
            MinHeight = 38,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        saveButton.Click += (_, _) => SavePassword();

        _message = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#FFDC2626")),
            TextWrapping = TextWrapping.Wrap
        };

        _passwordEditors.Children.Add(Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                _passwordBox,
                _confirmBox,
                saveButton,
                _message
            }
        }));

        _adminPasswordState = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold };
        _currentAdminPasswordBox = PasswordBox("原管理员密码（首次设置无需填写）");
        _adminPasswordBox = PasswordBox("新的管理员密码");
        _adminConfirmBox = PasswordBox("再次输入管理员密码");
        var saveAdminButton = new Button
        {
            Content = "保存管理员密码",
            MinHeight = 38,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        saveAdminButton.Click += (_, _) => SaveAdminPassword();
        _adminMessage = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#FFDC2626")),
            TextWrapping = TextWrapping.Wrap
        };

        _passwordEditors.Children.Add(Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Title("2. 设置管理员应急密码"),
                _adminPasswordState,
                Hint("锁定后在任意界面连续输入 Admin 密码即可解控，无需点击或先打开密码框。首次设置无需原密码；已设置后必须验证原密码才能修改。"),
                _currentAdminPasswordBox,
                _adminPasswordBox,
                _adminConfirmBox,
                saveAdminButton,
                _adminMessage
            }
        }));

        var lockButton = new Button
        {
            Content = "立即锁定操作（Ctrl+Alt+L）",
            FontSize = 17,
            MinHeight = 44,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.Parse("#FF2563EB")),
            Foreground = Brushes.White
        };
        lockButton.Click += (_, _) => LockNow();
        _lockMessage = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#FFDC2626")),
            TextWrapping = TextWrapping.Wrap
        };

        root.Children.Add(Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Title("3. 锁定操作"),
                Hint("建议先切换到需要保留的画面，再按热键。普通密码连续输错 10 次后需等待 5 分钟；需要解锁时再按 Ctrl+Alt+L 或点击右下角按钮。点「取消」只会关闭密码框，不会解除锁定。"),
                lockButton,
                _lockMessage
            }
        }));

        var schedule = _settings.GetLockSchedule();
        _scheduleEnabled = new CheckBox
        {
            Content = "启用允许锁定时段",
            IsChecked = schedule.Enabled
        };
        _scheduleStart = TimeBox(schedule.Start);
        _scheduleEnd = TimeBox(schedule.End);
        _scheduleEnabled.IsCheckedChanged += (_, _) => UpdateScheduleInputState();
        var saveScheduleButton = new Button
        {
            Content = "保存时段设置",
            MinHeight = 38,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        saveScheduleButton.Click += (_, _) => SaveSchedule();
        _scheduleMessage = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#FFDC2626")),
            TextWrapping = TextWrapping.Wrap
        };
        UpdateScheduleInputState();

        root.Children.Add(Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Title("4. 允许锁定时段（可选）"),
                Hint("不在允许时段内时，热键和手动锁定不会生效；已锁定则会在 15 秒内自动解除。支持跨午夜，起止时间相同表示全天允许。"),
                _scheduleEnabled,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "开始", VerticalAlignment = VerticalAlignment.Center },
                        _scheduleStart,
                        new TextBlock { Text = "结束", VerticalAlignment = VerticalAlignment.Center },
                        _scheduleEnd
                    }
                },
                saveScheduleButton,
                _scheduleMessage,
                Hint("注销后下次启动会按此时段恢复锁定；关机或重启会清除恢复状态，避免次日开机误锁。")
            }
        }));

        root.Children.Add(Card(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Title("锁定期间的保护"),
                Hint("透明输入隔离层覆盖所有显示器，全局指针钩子只放行锁屏层和密码框，并关闭通知、开始菜单等系统浮层，防止通知长按或底部上滑绕过。普通键盘按键及 Alt+F4、Alt+Tab 等也会被拦截。"),
                Hint("Ctrl+Alt+Delete 和注销始终由 Windows 管理，插件无法拦截；注销后仅在下次启动时尽力恢复锁定。插件不会隐藏进程，学校设备还应通过 Windows 策略限制任务管理器、注销及结束 ClassIsland。")
            }
        }));

        Content = new ScrollViewer { Content = root };
        RefreshState();
    }

    private void OpenPasswordSetupReadme()
    {
        const string url = "https://github.com/mjdds/ClassScreenGuard/blob/main/README.md#密码设置入口";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            _versionHint.Text = $"无法自动打开浏览器，请手动访问：{url}";
        }
    }

    private void RevealPasswordEditors()
    {
        if (_passwordEditors.IsVisible)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        _versionClickCount = now - _lastVersionClick <= TimeSpan.FromSeconds(2)
            ? _versionClickCount + 1
            : 1;
        _lastVersionClick = now;

        if (_versionClickCount < 5)
        {
            return;
        }

        _passwordEditors.IsVisible = true;
        _versionHint.Text = "密码设置已显示，本次打开设置页期间保持可见。";
    }

    private void SavePassword()
    {
        var password = _passwordBox.Text ?? string.Empty;
        if (password.Length < 4)
        {
            _message.Text = "密码至少需要 4 个字符。";
            return;
        }

        if (password != (_confirmBox.Text ?? string.Empty))
        {
            _message.Text = "两次输入的密码不一致。";
            return;
        }

        try
        {
            _settings.SetPassword(password);
            _passwordBox.Text = string.Empty;
            _confirmBox.Text = string.Empty;
            _message.Text = string.Empty;
            RefreshState();
        }
        catch (ArgumentException exception)
        {
            _message.Text = exception.Message;
        }
    }

    private void SaveAdminPassword()
    {
        var hasAdminPassword = _settings.HasAdminPassword;
        var currentPassword = _currentAdminPasswordBox.Text ?? string.Empty;
        if (hasAdminPassword && string.IsNullOrEmpty(currentPassword))
        {
            _adminMessage.Text = "修改管理员密码前，请输入原管理员密码。";
            return;
        }

        var password = _adminPasswordBox.Text ?? string.Empty;
        if (password.Length < 4)
        {
            _adminMessage.Text = "管理员密码至少需要 4 个字符。";
            return;
        }

        if (password != (_adminConfirmBox.Text ?? string.Empty))
        {
            _adminMessage.Text = "两次输入的管理员密码不一致。";
            return;
        }

        try
        {
            if (!_settings.ChangeAdminPassword(currentPassword, password))
            {
                _adminMessage.Text = "原管理员密码错误，请重新输入。";
                return;
            }

            _currentAdminPasswordBox.Text = string.Empty;
            _adminPasswordBox.Text = string.Empty;
            _adminConfirmBox.Text = string.Empty;
            _adminMessage.Text = string.Empty;
            RefreshState();
        }
        catch (ArgumentException exception)
        {
            _adminMessage.Text = exception.Message;
        }
    }

    private void SaveSchedule()
    {
        var culture = CultureInfo.CurrentCulture;
        if (!TimeSpan.TryParse((_scheduleStart.Text ?? string.Empty).Trim(), culture, out var start) ||
            !TimeSpan.TryParse((_scheduleEnd.Text ?? string.Empty).Trim(), culture, out var end))
        {
            _scheduleMessage.Text = "请输入有效时间，例如 07:00 和 18:00。";
            return;
        }

        try
        {
            _settings.SetLockSchedule(_scheduleEnabled.IsChecked == true, start, end);
            _scheduleStart.Text = start.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            _scheduleEnd.Text = end.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            _scheduleMessage.Text = _guard.IsLockAllowedNow
                ? "时段设置已保存，当前允许锁定。"
                : "时段设置已保存，当前不允许锁定。";
            _scheduleMessage.Foreground = new SolidColorBrush(Color.Parse("#FF15803D"));
        }
        catch (ArgumentException exception)
        {
            _scheduleMessage.Text = exception.Message;
            _scheduleMessage.Foreground = new SolidColorBrush(Color.Parse("#FFDC2626"));
        }
    }

    private void UpdateScheduleInputState()
    {
        _scheduleStart.IsEnabled = _scheduleEnabled.IsChecked == true;
        _scheduleEnd.IsEnabled = _scheduleEnabled.IsChecked == true;
    }

    private void LockNow()
    {
        if (!_guard.CanLock)
        {
            _lockMessage.Text = "请先设置并保存解锁密码，再锁定电脑。";
            return;
        }

        if (!_guard.IsLockAllowedNow)
        {
            _lockMessage.Text = "当前不在允许锁定时段，无法锁定。";
            return;
        }

        _lockMessage.Text = string.Empty;
        _guard.RequestLock();
    }

    private void RefreshState()
    {
        _passwordState.Text = _settings.HasPassword
            ? "密码已设置，可以锁定电脑。"
            : "尚未设置密码，锁屏热键暂不会生效。";
        _passwordState.Foreground = new SolidColorBrush(
            Color.Parse(_settings.HasPassword ? "#FF15803D" : "#FFB45309"));
        var hasAdminPassword = _settings.HasAdminPassword;
        _adminPasswordState.Text = hasAdminPassword
            ? "管理员应急密码已设置，修改时需要验证原密码。"
            : "尚未设置管理员密码，首次设置无需输入原密码。";
        _adminPasswordState.Foreground = new SolidColorBrush(
            Color.Parse(hasAdminPassword ? "#FF15803D" : "#FFB45309"));
        _currentAdminPasswordBox.IsVisible = hasAdminPassword;
        _currentAdminPasswordBox.Watermark = hasAdminPassword
            ? "原管理员密码（必填）"
            : "原管理员密码（首次设置无需填写）";
    }

    private static TextBox TimeBox(TimeSpan time) => new()
    {
        Text = time.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
        Watermark = "HH:mm",
        Width = 100,
        MinHeight = 38
    };

    private static TextBox PasswordBox(string watermark) => new()
    {
        Watermark = watermark,
        PasswordChar = '•',
        FontSize = 18,
        MinHeight = 42
    };

    private static TextBlock Title(string text) => new()
    {
        Text = text,
        FontSize = 19,
        FontWeight = FontWeight.SemiBold
    };

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 13,
        Opacity = 0.7,
        TextWrapping = TextWrapping.Wrap
    };

    private static Border Card(Control child) => new()
    {
        Background = new SolidColorBrush(Color.Parse("#14808080")),
        BorderBrush = new SolidColorBrush(Color.Parse("#22808080")),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(18),
        Child = child
    };
}
