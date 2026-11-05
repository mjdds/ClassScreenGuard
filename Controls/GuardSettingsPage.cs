using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassScreenGuard.Services;

namespace ClassScreenGuard.Controls;

/// <summary>锁屏密码设置与手动锁定页面。</summary>
[SettingsPageInfo("classScreenGuard.settings", "屏幕操作锁", "\ue72e", "\ue72e")]
public sealed class GuardSettingsPage : SettingsPageBase
{
    private readonly GuardSettingsService _settings;
    private readonly ScreenGuardService _guard;
    private readonly TextBox _passwordBox;
    private readonly TextBox _confirmBox;
    private readonly TextBlock _message;
    private readonly TextBlock _passwordState;

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

        _passwordState = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold };
        root.Children.Add(Card(new StackPanel
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

        root.Children.Add(Card(new StackPanel
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

        root.Children.Add(Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Title("2. 锁定操作"),
                Hint("建议先切换到需要保留的画面，再按热键。锁定后不会出现遮挡层；需要解锁时再按 Ctrl+Alt+L，输入密码并按 Enter。点「取消」只会关闭密码框，不会解除锁定。"),
                lockButton
            }
        }));

        root.Children.Add(Card(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Title("锁定期间的保护"),
                Hint("透明输入隔离层覆盖所有显示器，拦截鼠标、触摸、长按和全部普通键盘按键，因此 Esc、空格、方向键、Windows 键、Alt+F4、Alt+Tab 等都不能控制或退出当前应用。"),
                Hint("Ctrl+Alt+Delete 始终保留给 Windows，可用于注销或重启。任何用户态插件都无法阻止断电、强制重启或管理员结束进程。")
            }
        }));

        Content = new ScrollViewer { Content = root };
        RefreshState();
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

    private void LockNow()
    {
        if (!_guard.CanLock)
        {
            _message.Text = "请先设置并保存解锁密码，再锁定电脑。";
            return;
        }

        _guard.RequestLock();
    }

    private void RefreshState()
    {
        _passwordState.Text = _settings.HasPassword
            ? "密码已设置，可以锁定电脑。"
            : "尚未设置密码，锁屏热键暂不会生效。";
        _passwordState.Foreground = new SolidColorBrush(
            Color.Parse(_settings.HasPassword ? "#FF15803D" : "#FFB45309"));
    }

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
