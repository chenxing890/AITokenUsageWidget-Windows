using AITokenUsageWidget.Shared.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AITokenUsageWidget.App.Controls;

/// <summary>
/// 品牌图标（对齐 macOS ProviderChip）：白色矢量图标 + 45° 品牌色渐变圆角底。
/// 使用 PathIcon 矢量图形，避免字体字形缺失导致图标不显示。
/// </summary>
public sealed partial class ProviderChip : UserControl
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(ProviderKind), typeof(ProviderChip),
        new PropertyMetadata(ProviderKind.DeepSeek, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ChipSizeProperty = DependencyProperty.Register(
        nameof(ChipSize), typeof(double), typeof(ProviderChip),
        new PropertyMetadata(28.0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(ProviderChip),
        new PropertyMetadata(13.0, OnLayoutPropertyChanged));

    public ProviderChip()
    {
        InitializeComponent();
        Render();
    }

    public ProviderKind Kind
    {
        get => (ProviderKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double ChipSize
    {
        get => (double)GetValue(ChipSizeProperty);
        set => SetValue(ChipSizeProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chip = (ProviderChip)d;
        if (chip.Root != null) chip.Render();
    }

    private void Render()
    {
        Root.Width = Root.Height = ChipSize;
        Root.CornerRadius = new CornerRadius(ChipSize * 0.3);
        DropIcon.Width = DropIcon.Height = IconSize;
        MoonIcon.Width = MoonIcon.Height = IconSize;
        SparkleIcon.Width = SparkleIcon.Height = IconSize;

        var color = ParseColor(Kind.BrandHex());
        Root.Background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = color, Offset = 0 },
                // macOS：accentColor → accentColor.opacity(0.62)
                new GradientStop { Color = Windows.UI.Color.FromArgb(158, color.R, color.G, color.B), Offset = 1 },
            },
        };

        DropIcon.Visibility = Kind == ProviderKind.DeepSeek ? Visibility.Visible : Visibility.Collapsed;
        MoonIcon.Visibility = Kind == ProviderKind.Kimi ? Visibility.Visible : Visibility.Collapsed;
        SparkleIcon.Visibility = Kind == ProviderKind.Glm ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Windows.UI.Color ParseColor(string hex)
    {
        var value = Convert.ToUInt32(hex.TrimStart('#'), 16);
        return Windows.UI.Color.FromArgb(
            255,
            (byte)(value >> 16 & 0xFF),
            (byte)(value >> 8 & 0xFF),
            (byte)(value & 0xFF));
    }
}
