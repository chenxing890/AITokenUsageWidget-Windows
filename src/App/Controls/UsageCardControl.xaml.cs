using AITokenUsageWidget.Shared.Models;
using AITokenUsageWidget.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AITokenUsageWidget.App.Controls;

/// <summary>
/// 供应商用量卡片（FR-2 / §3.4，对齐 macOS ProviderCardView）：App 内嵌预览使用，与小组件卡片内容一致。
/// </summary>
public sealed partial class UsageCardControl : UserControl
{
    public static readonly DependencyProperty UsageProperty = DependencyProperty.Register(
        nameof(Usage), typeof(ProviderUsage), typeof(UsageCardControl),
        new PropertyMetadata(null, OnUsageChanged));

    public ProviderUsage? Usage
    {
        get => (ProviderUsage?)GetValue(UsageProperty);
        set => SetValue(UsageProperty, value);
    }

    public UsageCardControl()
    {
        InitializeComponent();
        // 主题切换后按新主题重取画刷重绘（代码生成的画刷是快照，不随主题自动更新）
        ActualThemeChanged += (_, _) => Render();
    }

    private static void OnUsageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((UsageCardControl)d).Render();
    }

    private void Render()
    {
        BodyPanel.Children.Clear();
        var usage = Usage;
        if (usage == null)
        {
            NameText.Text = "";
            Visibility = Visibility.Collapsed;
            return;
        }
        Visibility = Visibility.Visible;

        var brand = (SolidColorBrush)Application.Current.Resources[$"Brand{usage.Kind}"];
        Chip.Kind = usage.Kind;
        BrandBar.Background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = brand.Color, Offset = 0 },
                // macOS：accentColor → accentColor.opacity(0.45)
                new GradientStop { Color = Windows.UI.Color.FromArgb(115, brand.Color.R, brand.Color.G, brand.Color.B), Offset = 1 },
            },
        };
        NameText.Text = usage.DisplayName;
        StatusDot.Fill = StatusColor(usage);

        switch (usage.State)
        {
            case UsageState.MissingKey:
                AddHint("未配置 API Key", secondary: true);
                break;
            case UsageState.Error:
                AddHint(usage.ErrorMessage ?? "请求失败", secondary: false, error: true);
                break;
            default:
                if (usage.Kind.ShowsBalance()) RenderBalance(usage);
                else RenderWindows(usage);
                break;
        }
    }

    private void RenderBalance(ProviderUsage usage)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        row.Children.Add(new TextBlock
        {
            Text = usage.CurrencySymbol,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 2, 4),
            Foreground = SecondaryBrush(),
        });
        row.Children.Add(new TextBlock
        {
            Text = usage.TotalBalance is { } total ? Format.Amount(total) : "--",
            FontSize = 26,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        });
        BodyPanel.Children.Add(row);

        if (usage.GrantedBalance is { } granted && usage.ToppedUpBalance is { } toppedUp)
        {
            BodyPanel.Children.Add(new TextBlock
            {
                Text = $"赠送 {usage.CurrencySymbol}{Format.Amount(granted)} · 充值 {usage.CurrencySymbol}{Format.Amount(toppedUp)}",
                FontSize = 11,
                Foreground = TertiaryBrush(),
            });
        }
    }

    private void RenderWindows(ProviderUsage usage)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var window in usage.Windows)
        {
            if (window.UsedPercent is not { } percent)
            {
                // 无百分比窗口（如「30 天累计」）：单行「标题 + 数值」
                var line = new Grid();
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
                var title = SmallText(window.Title, SecondaryBrush());
                line.Children.Add(title);
                var value = SmallText(window.UsedText ?? "--", TertiaryBrush());
                Grid.SetColumn(value, 1);
                line.Children.Add(value);
                BodyPanel.Children.Add(line);
                continue;
            }

            // 标题行：窗口名 + 用量文本 + 百分比 + 倒计时
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

            head.Children.Add(SmallText(window.Title, SecondaryBrush()));

            if (!string.IsNullOrEmpty(window.UsedText))
            {
                var used = SmallText(window.UsedText, TertiaryBrush());
                used.Margin = new Thickness(0, 0, 6, 0);
                Grid.SetColumn(used, 2);
                head.Children.Add(used);
            }

            var percentText = new TextBlock
            {
                Text = $"{Format.Percent(percent)}%",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            };
            var countdown = Format.ResetCountdown(window.ResetTime, now);
            if (countdown.Length > 0)
            {
                percentText.Margin = new Thickness(0, 0, 6, 0);
                Grid.SetColumn(percentText, 3);
                head.Children.Add(percentText);

                // 倒计时：小历史图标 + 文本（对齐 macOS clock.arrow.circlepath）
                var timer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
                timer.Children.Add(new FontIcon
                {
                    Glyph = "\uE81C", // History
                    FontSize = 9,
                    Foreground = TertiaryBrush(),
                });
                timer.Children.Add(SmallText(countdown, TertiaryBrush()));
                Grid.SetColumn(timer, 4);
                head.Children.Add(timer);
            }
            else
            {
                Grid.SetColumn(percentText, 4);
                head.Children.Add(percentText);
            }

            BodyPanel.Children.Add(head);

            // 进度条：4px 圆角轨道 + 用量级别色填充（≥80 红 / ≥50 橙 / 其余绿）；
            // 7 天窗口叠加半透明灰「健康配额线」（随时间推进，对齐 macOS）
            BodyPanel.Children.Add(ProgressTrack(percent, HealthyMarker(window, now)));
        }
    }

    /// <summary>7 天窗口的健康配额线位置（0–100），非 7 天窗口返回 null。</summary>
    private static double? HealthyMarker(UsageWindow window, DateTimeOffset now) =>
        window.IsWeekly ? window.HealthyQuotaPercent(now) : null;

    private Grid ProgressTrack(double percent, double? markerPercent = null)
    {
        var grid = new Grid { Height = 4 };
        grid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(2),
            Background = TrackBrush(),
        });
        var clamped = Math.Clamp(percent, 0, 100);
        if (clamped > 0)
        {
            var fill = new Border
            {
                CornerRadius = new CornerRadius(2),
                Background = LevelBrush(clamped),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            grid.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * clamped / 100.0;
            grid.Children.Add(fill);
        }
        if (markerPercent is { } m && m > 0 && m < 100)
        {
            // 健康配额线：与进度条同高内嵌（不凸出，保持原高度设计），半透明灰
            var marker = new Border
            {
                Width = 2,
                Height = 4,
                CornerRadius = new CornerRadius(1),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xBF, 0x80, 0x80, 0x80)), // Gray ~75%
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            grid.SizeChanged += (_, e) =>
                marker.Margin = new Thickness(e.NewSize.Width * m / 100.0 - 1, 0, 0, 0);
            grid.Children.Add(marker);
        }
        return grid;
    }

    private void AddHint(string text, bool secondary, bool error = false)
    {
        var hint = new TextBlock
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = error ? new SolidColorBrush(Microsoft.UI.Colors.OrangeRed) : SecondaryBrush(),
        };
        BodyPanel.Children.Add(hint);
    }

    private static TextBlock SmallText(string text, Brush foreground) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = foreground,
    };

    private Brush StatusColor(ProviderUsage usage) => usage.State switch
    {
        UsageState.Ok when usage.Kind.ShowsBalance() && usage.IsAvailable == false =>
            new SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
        UsageState.Ok => new SolidColorBrush(Microsoft.UI.Colors.MediumSeaGreen),
        UsageState.MissingKey => new SolidColorBrush(Microsoft.UI.Colors.Gray),
        _ => new SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
    };

    private static Brush LevelBrush(double percent)
    {
        var color = percent switch
        {
            >= 80 => Microsoft.UI.Colors.OrangeRed,
            >= 50 => Microsoft.UI.Colors.DarkOrange,
            _ => Microsoft.UI.Colors.MediumSeaGreen,
        };
        return new SolidColorBrush(color);
    }

    // 从 Application.Resources 取画刷只会解析应用启动时的主题，窗口级 RequestedTheme
    // 切换后仍是旧主题快照（深色模式下黑字看不清）。改按控件 ActualTheme 取值。
    private bool IsDark => ActualTheme == ElementTheme.Dark;

    private Brush SecondaryBrush() => new SolidColorBrush(IsDark
        ? Windows.UI.Color.FromArgb(0xC5, 255, 255, 255)
        : Windows.UI.Color.FromArgb(0x9D, 0, 0, 0));

    private Brush TertiaryBrush() => new SolidColorBrush(IsDark
        ? Windows.UI.Color.FromArgb(0x8A, 255, 255, 255)
        : Windows.UI.Color.FromArgb(0x72, 0, 0, 0));

    private Brush TrackBrush() => new SolidColorBrush(IsDark
        ? Windows.UI.Color.FromArgb(0x0A, 255, 255, 255)
        : Windows.UI.Color.FromArgb(0x06, 0, 0, 0));
}
