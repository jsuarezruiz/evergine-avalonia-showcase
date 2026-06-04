using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AutomotiveConfigurator.AvaloniaEvergine.Models;

public sealed class ConfiguratorTabItem : INotifyPropertyChanged
{
    private static readonly IBrush ActiveBackground = new SolidColorBrush(Color.FromRgb(204, 204, 204));
    private static readonly IBrush HoverBackground = Brushes.White;
    private static readonly IBrush RestingBackground = new SolidColorBrush(Color.FromArgb(190, 0, 0, 0));
    private static readonly IBrush ActiveForeground = Brushes.Black;
    private static readonly IBrush RestingForeground = new SolidColorBrush(Color.FromRgb(153, 153, 153));

    private bool isActive;
    private bool isHovered;

    public ConfiguratorTabItem(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string Label { get; }

    public bool IsActive
    {
        get => this.isActive;
        set
        {
            if (this.isActive == value)
            {
                return;
            }

            this.isActive = value;
            NotifyVisualStateChanged();
        }
    }

    public bool IsHovered
    {
        get => this.isHovered;
        set
        {
            if (this.isHovered == value)
            {
                return;
            }

            this.isHovered = value;
            NotifyVisualStateChanged();
        }
    }

    public IBrush Background =>
        IsActive ? ActiveBackground : IsHovered ? HoverBackground : RestingBackground;

    public IBrush Foreground =>
        IsActive || IsHovered ? ActiveForeground : RestingForeground;

    private void NotifyVisualStateChanged()
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsHovered));
        OnPropertyChanged(nameof(Background));
        OnPropertyChanged(nameof(Foreground));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ConfiguratorOptionItem : INotifyPropertyChanged
{
    private static readonly IBrush SelectedBorder = Brushes.White;
    private static readonly IBrush RestingBorder = new SolidColorBrush(Color.FromArgb(51, 255, 255, 255));

    private bool isSelected;
    private bool isHovered;

    private ConfiguratorOptionItem(
        string name,
        string target,
        string value,
        bool setMirrorAsBody,
        Color? swatchColor,
        Bitmap? thumbnail,
        Thickness margin,
        IBrush buttonBackground)
    {
        Name = name;
        Target = target;
        Value = value;
        SetMirrorAsBody = setMirrorAsBody;
        SwatchColor = swatchColor;
        Thumbnail = thumbnail;
        Margin = margin;
        ButtonBackground = buttonBackground;
        ColorBrush = swatchColor.HasValue ? new SolidColorBrush(swatchColor.Value) : null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public string Target { get; }

    public string Value { get; }

    public bool SetMirrorAsBody { get; }

    public Color? SwatchColor { get; }

    public IBrush? ColorBrush { get; }

    public Bitmap? Thumbnail { get; }

    public Thickness Margin { get; }

    public IBrush ButtonBackground { get; }

    public bool HasColor => ColorBrush is not null;

    public bool HasThumbnail => Thumbnail is not null;

    public bool IsSelected
    {
        get => this.isSelected;
        set
        {
            if (this.isSelected == value)
            {
                return;
            }

            this.isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BorderBrush));
        }
    }

    public bool IsHovered
    {
        get => this.isHovered;
        set
        {
            if (this.isHovered == value)
            {
                return;
            }

            this.isHovered = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LabelOpacity));
        }
    }

    public IBrush BorderBrush => IsSelected ? SelectedBorder : RestingBorder;

    public double LabelOpacity => IsHovered ? 1d : 0d;

    public static ConfiguratorOptionItem CreateColor(
        string name,
        Color color,
        string target,
        string value,
        bool setMirrorAsBody)
    {
        return new ConfiguratorOptionItem(
            name,
            target,
            value,
            setMirrorAsBody,
            color,
            thumbnail: null,
            margin: new Thickness(12, 16),
            buttonBackground: Brushes.Transparent);
    }

    public static ConfiguratorOptionItem CreateWheelDesign(string name, string target, string value, Bitmap thumbnail)
    {
        return new ConfiguratorOptionItem(
            name,
            target,
            value,
            setMirrorAsBody: false,
            swatchColor: null,
            thumbnail,
            margin: new Thickness(8, 10),
            buttonBackground: Brushes.Black);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
