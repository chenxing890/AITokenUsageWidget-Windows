using AITokenUsageWidget.Shared.Models;
using AITokenUsageWidget.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AITokenUsageWidget.App.Controls;

/// <summary>
/// 供应商用量卡片（FR-2 / §3.4）：App 内嵌预览使用，与小组件卡片内容一致。
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
        BrandBar.Background = brand;
        Chip.Background = brand;
        ChipIcon.Glyph = usage.Kind switch
        {
            ProviderKind.DeepSeek => "\uEC4C",
            ProviderKind.Kimi => "\uE708",
            _ => "\uEF83",
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
                Style = Microsoft.UI.Xaml.Application.Current.Resources["CaptionTextBlockStyle"] as Style,
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
                // 无百分比窗口（如「30 天累计」）：单行文本
                var line = new Grid();
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
                line.Children.Add(Caption(window.Title, secondary: true, 0));
                line.Children.Add(new Border());
                var value = Caption(window.UsedText ?? "--", secondary: false, 2);
                value.Foreground = TertiaryBrush();
                line.Children.Add(value);
                BodyPanel.Children.Add(line);
                continue;
            }

            // 标题行：窗口名 + 用量文本 + 百分比 + 倒计时（五列）
            var head = new Grid();
            for (var i = 0; i < 5; i++)
            {
                head.ColumnDefinitions.Add(i == 1
                    ? new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                    : new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            }
            head.Children.Add(Caption(window.Title, secondary: true, 0));

            var nextColumn = 2;
            if (!string.IsNullOrEmpty(window.UsedText))
            {
                var text = Caption(window.UsedText, secondary: true, nextColumn++);
                text.Foreground = TertiaryBrush();
                text.Margin = new Thickness(0, 0, 8, 0);
                head.Children.Add(text);
            }

            head.Children.Add(new TextBlock
            {
                Text = $"{Format.Percent(percent)}%",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Grid.Column = nextColumn++,
            });

            var countdown = Format.ResetCountdown(window.ResetTime, now);
            if (countdown.Length > 0)
            {
                var timer = Caption(countdown, secondary: true, nextColumn);
                timer.Foreground = TertiaryBrush();
                timer.Margin = new Thickness(8, 0, 0, 0);
                head.Children.Add(timer);
            }

            BodyPanel.Children.Add(head);

            // 进度条（用量级别色：≥80 红 / ≥50 橙 / 其余绿）
            BodyPanel.Children.Add(new ProgressBar
            {
                Value = Math.Clamp(percent, 0, 100),
                Maximum = 100,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Foreground = LevelBrush(percent),
            });
        }
    }

    private void AddHint(string text, bool secondary, bool error = false)
    {
        var hint = new TextBlock
        {
            Text = text,
            Style = Microsoft.UI.Xaml.Application.Current.Resources["CaptionTextBlockStyle"] as Style,
            TextWrapping = TextWrapping.Wrap,
            Foreground = error ? new SolidColorBrush(Microsoft.UI.Colors.OrangeRed) : SecondaryBrush(),
        };
        BodyPanel.Children.Add(hint);
    }

    private TextBlock Caption(string text, bool secondary, int column)
    {
        var block = new TextBlock
        {
            Text = text,
            Style = Microsoft.UI.Xaml.Application.Current.Resources["CaptionTextBlockStyle"] as Style,
            Foreground = secondary ? SecondaryBrush() : null,
            Grid.Column = column,
        };
        return block;
    }

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

    private Brush SecondaryBrush() =>
        (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"];

    private Brush TertiaryBrush() =>
        (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorTertiaryBrush"];
}
