using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClassScreenGuard.Controls;

/// <summary>Admin 全局解锁后的一秒提示。</summary>
public sealed class AdminUnlockNoticeWindow : Window
{
    private readonly DispatcherTimer _closeTimer;

    public AdminUnlockNoticeWindow()
    {
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        Background = new SolidColorBrush(Color.Parse("#F2172033"));

        Content = new Border
        {
            Padding = new Thickness(24, 20),
            BorderBrush = new SolidColorBrush(Color.Parse("#FF4ADE80")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Admin 解锁",
                        Foreground = Brushes.White,
                        FontSize = 24,
                        FontWeight = FontWeight.SemiBold,
                        TextAlignment = TextAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "已解除屏幕操作锁",
                        Foreground = Brushes.White,
                        Opacity = 0.75,
                        FontSize = 14,
                        TextAlignment = TextAlignment.Center
                    }
                }
            }
        };

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            Close();
        };
        Opened += (_, _) => _closeTimer.Start();
        Closed += (_, _) => _closeTimer.Stop();
    }
}
