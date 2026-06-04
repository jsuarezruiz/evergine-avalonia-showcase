using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AutomotiveConfigurator.AvaloniaEvergine.Controls;
using AutomotiveConfigurator.AvaloniaEvergine.Models;
using AutomotiveConfigurator.AvaloniaEvergine.Rendering;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;

namespace AutomotiveConfigurator.AvaloniaEvergine;

public partial class MainWindow : Window
{
    private const string WheelDesignSelectionTarget = "__wheel_design";
    private const double SwatchWidth = 128;
    private const double SwatchHeight = 81;
    private const int MaxCinematicStartAttempts = 10;
    private static readonly TimeSpan SwatchFadeDuration = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan CinematicStartRetryInterval = TimeSpan.FromMilliseconds(100);
    private const string MetaResourceUri =
        "avares://AutomotiveConfigurator.AvaloniaEvergine/Assets/AutomotiveConfigurator/aventador/meta.json";

    private readonly Dictionary<string, Button> tabButtons = new();
    private readonly Dictionary<string, string> selectedValues = new(StringComparer.Ordinal);
    private ConfiguratorMeta meta = new();
    private string? activeTabId;
    private string? selectedWheelDesign;
    private Color currentBodyColor;
    private Color currentMirrorColor;
    private bool mirrorUsesBodyColor = true;
    private bool showroomActive;
    private IAutomotiveSceneBridge? sceneBridge;
    private DispatcherTimer? cinematicStartTimer;
    private DispatcherTimer? cinematicCompletionTimer;
    private DispatcherTimer? sceneReadyTimer;
    private int cinematicStartAttemptCount;

    private sealed record SwatchOption(string Target, string Value);

    internal bool HasReadyRenderSurface =>
        this.sceneBridge is EvergineRenderHost { IsReady: true };

    private bool CanStartCinematicNow =>
        SceneBridge.IsSceneReady &&
        !SceneBridge.HasSceneError &&
        HasReadyRenderSurface;

    public MainWindow()
    {
        InitializeComponent();

        AttachRenderSurface();

        this.Loaded += (_, _) => InitializeConfigurator();
        StartDemoButton.Click += (_, _) => StartDemo();
        ShowroomButton.Click += (_, _) => ToggleShowroom();
        SkipIntroButton.Click += (_, _) => SkipIntro();
        GitHubButton.Click += (_, _) => OpenGitHub();
    }

    private void InitializeConfigurator()
    {
        StopCinematicStartTimer();
        StopCinematicCompletionTimer();
        StopSceneReadyTimer();
        AttachRenderSurface();
        this.meta = LoadMeta();

        currentBodyColor = ParseColor(
            this.meta.BodyColors.Colors[this.meta.BodyColors.Default].Value);
        currentMirrorColor = ParseColor(
            this.meta.MirrorColors.Colors[this.meta.MirrorColors.Default].Value);
        this.selectedValues[this.meta.BodyColors.Target] = ColorKey(currentBodyColor);
        this.selectedValues[this.meta.MirrorColors.Target] = ColorKey(currentMirrorColor);
        this.selectedValues[this.meta.WheelColors.Target] = ColorKey(
            ParseColor(this.meta.WheelColors.Colors[this.meta.WheelColors.Default].Value));
        this.selectedValues[this.meta.CaliperColors.Target] = ColorKey(
            ParseColor(this.meta.CaliperColors.Colors[this.meta.CaliperColors.Default].Value));
        this.selectedWheelDesign = this.meta.WheelDesigns.Designs[this.meta.WheelDesigns.Default].Value;

        var assets = new AutomotiveAssetSet(
            ResolveAssetPath("stage/model.glb"),
            ResolveAssetPath("aventador/model.glb"),
            ResolveAssetPath("environment/model.glb"));

        SceneBridge.InitializeScene(assets, this.meta);
        ApplyDefaults();
        BuildTabs();
        ShowTab("body_colors");

        ShowSceneLoadingState();
        StartSceneReadyPolling();
    }

    private static ConfiguratorMeta LoadMeta()
    {
        using var stream = AssetLoader.Open(new Uri(MetaResourceUri));
        return JsonSerializer.Deserialize<ConfiguratorMeta>(stream) ?? new ConfiguratorMeta();
    }

    private static string ResolveAssetPath(string relativePath)
    {
        return Path.Combine("Assets", "AutomotiveConfigurator", relativePath);
    }

    private void ApplyDefaults()
    {
        var bridge = SceneBridge;
        bridge.SetMaterialColor(this.meta.BodyColors.Target, currentBodyColor);
        bridge.SetMaterialColor(this.meta.MirrorColors.Target, currentMirrorColor);
        bridge.SetMaterialColor(
            this.meta.WheelColors.Target,
            ParseColor(this.meta.WheelColors.Colors[this.meta.WheelColors.Default].Value));
        bridge.SetMaterialColor(
            this.meta.CaliperColors.Target,
            ParseColor(this.meta.CaliperColors.Colors[this.meta.CaliperColors.Default].Value));
        bridge.ShowWheelDesign(this.meta.WheelDesigns.Designs[this.meta.WheelDesigns.Default].Value);
    }

    private void StartDemo()
    {
        if (SceneBridge.HasSceneError)
        {
            ShowSceneErrorState();
            return;
        }

        if (!SceneBridge.IsSceneReady)
        {
            return;
        }

        ViewportHost.IsVisible = true;
        LoaderOverlay.IsVisible = false;
        PalettePanel.IsVisible = false;
        WelcomeOverlay.IsVisible = false;
        BeginCinematicOrFallback();
    }

    private void SkipIntro()
    {
        StopCinematicStartTimer();
        StopCinematicCompletionTimer();
        SceneBridge.StopCinematic();
        ShowConfigurator();
    }

    private void ShowConfigurator()
    {
        ViewportHost.IsVisible = true;
        LoaderOverlay.IsVisible = false;
        WelcomeOverlay.IsVisible = false;
        PalettePanel.IsVisible = true;
        AuthoringPanel.IsVisible = true;
        showroomActive = false;
        ShowroomButton.Content = "SHOWROOM";
        ShowTab("body_colors");
    }

    private void BeginCinematicOrFallback()
    {
        StopCinematicStartTimer();
        this.cinematicStartAttemptCount = 0;

        if (TryStartCinematic())
        {
            return;
        }

        var timer = new DispatcherTimer
        {
            Interval = CinematicStartRetryInterval,
        };

        timer.Tick += (_, _) =>
        {
            this.cinematicStartAttemptCount++;
            if (SceneBridge.HasSceneError)
            {
                StopCinematicStartTimer();
                ShowSceneErrorState();
                return;
            }

            if (TryStartCinematic())
            {
                return;
            }

            if (this.cinematicStartAttemptCount < MaxCinematicStartAttempts)
            {
                return;
            }

            StopCinematicStartTimer();
            SceneBridge.StopCinematic();
            ShowConfigurator();
        };

        this.cinematicStartTimer = timer;
        timer.Start();
    }

    private bool TryStartCinematic()
    {
        if (!CanStartCinematicNow)
        {
            return false;
        }

        StopCinematicStartTimer();
        WelcomeOverlay.IsVisible = true;
        showroomActive = true;
        ShowroomButton.Content = "STOP";
        SceneBridge.StartCinematic();
        ScheduleCinematicCompletion();
        return true;
    }

    private void ToggleShowroom()
    {
        if (showroomActive)
        {
            SkipIntro();
            return;
        }

        StartDemo();
    }

    private void ScheduleCinematicCompletion()
    {
        StopCinematicCompletionTimer();

        var duration = TimeSpan.FromMilliseconds(AutomotiveSceneDefaults.CinematicDurationMilliseconds + 250);
        var timer = new DispatcherTimer
        {
            Interval = duration,
        };

        timer.Tick += (_, _) =>
        {
            StopCinematicCompletionTimer();
            SkipIntro();
        };

        this.cinematicCompletionTimer = timer;
        timer.Start();
    }

    private void StopCinematicCompletionTimer()
    {
        this.cinematicCompletionTimer?.Stop();
        this.cinematicCompletionTimer = null;
    }

    private void StopCinematicStartTimer()
    {
        this.cinematicStartTimer?.Stop();
        this.cinematicStartTimer = null;
        this.cinematicStartAttemptCount = 0;
    }

    private void BuildTabs()
    {
        this.tabButtons.Clear();
        TabPanel.Children.Clear();
        OptionsPanel.Children.Clear();

        AddTab("body_colors", "BODY COLOR");
        AddTab("mirror_colors", "SIDE MIRRORS");
        AddTab("wheel_designs", "WHEELS");
        AddTab("wheel_colors", "WHEEL COLOR");
        AddTab("caliper_colors", "CALIPERS");
    }

    private void ShowSceneLoadingState()
    {
        LoaderProgress.IsVisible = true;
        LoaderTitle.Text = "- LOADING -";
        LoaderDescription.Text = SceneBridge.SceneStatus;
        StatusTextBlock.Text = "Loading scene";
        StatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(153, 153, 153));
        LoaderOverlay.IsVisible = true;
        StartDemoButton.IsVisible = false;
        ViewportHost.IsVisible = true;
        WelcomeOverlay.IsVisible = false;
        PalettePanel.IsVisible = false;
        AuthoringPanel.IsVisible = false;
        showroomActive = false;
        ShowroomButton.Content = "SHOWROOM";
    }

    private void ShowSceneReadyState()
    {
        LoaderProgress.IsVisible = false;
        LoaderTitle.Text = "Automotive Configurator";
        LoaderDescription.Text = "Avalonia + Evergine scene ready.";
        StatusTextBlock.Text = "Renderer active";
        StatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(79, 139, 255));
        LoaderOverlay.IsVisible = true;
        StartDemoButton.IsVisible = true;
        StartDemoButton.Content = "START";
        ViewportHost.IsVisible = false;
        WelcomeOverlay.IsVisible = false;
        PalettePanel.IsVisible = false;
        AuthoringPanel.IsVisible = false;
    }

    private void ShowSceneErrorState()
    {
        StopCinematicStartTimer();
        StopCinematicCompletionTimer();
        LoaderProgress.IsVisible = false;
        LoaderTitle.Text = "ERROR LOADING";
        LoaderDescription.Text = SceneBridge.SceneStatus;
        StatusTextBlock.Text = "Scene failed";
        StatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 96, 96));
        LoaderOverlay.IsVisible = true;
        StartDemoButton.IsVisible = false;
        ViewportHost.IsVisible = false;
        WelcomeOverlay.IsVisible = false;
        PalettePanel.IsVisible = false;
        AuthoringPanel.IsVisible = false;
    }

    private void StartSceneReadyPolling()
    {
        StopSceneReadyTimer();

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        timer.Tick += (_, _) => UpdateSceneReadyState();

        this.sceneReadyTimer = timer;
        timer.Start();
        UpdateSceneReadyState();
    }

    private void UpdateSceneReadyState()
    {
        var bridge = SceneBridge;
        if (bridge.HasSceneError)
        {
            StopSceneReadyTimer();
            ShowSceneErrorState();
            return;
        }

        if (bridge.IsSceneReady)
        {
            StopSceneReadyTimer();
            ShowSceneReadyState();
            return;
        }

        LoaderDescription.Text = bridge.SceneStatus;
    }

    private void StopSceneReadyTimer()
    {
        this.sceneReadyTimer?.Stop();
        this.sceneReadyTimer = null;
    }

    private void AddTab(string id, string label)
    {
        var text = new TextBlock
        {
            Text = label,
            Foreground = Brushes.White,
            FontSize = 13,
            Margin = new Avalonia.Thickness(4, 0),
            RenderTransform = new SkewTransform(-20, 0),
        };

        var button = new Button
        {
            Content = text,
            Tag = id,
            Padding = new Avalonia.Thickness(16, 8),
            Background = new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)),
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new CornerRadius(0),
            RenderTransform = new SkewTransform(20, 0),
        };

        button.Click += (_, _) => ToggleTab(id);
        button.PointerEntered += (_, _) => SetTabVisual(button, isActive: id == activeTabId, isHover: true);
        button.PointerExited += (_, _) => SetTabVisual(button, isActive: id == activeTabId, isHover: false);

        this.tabButtons[id] = button;
        TabPanel.Children.Add(button);
    }

    private void ToggleTab(string id)
    {
        if (activeTabId == id)
        {
            activeTabId = null;
            OptionsPanel.Children.Clear();
            UpdateTabStates();
            return;
        }

        ShowTab(id);
    }

    private void ShowTab(string id)
    {
        activeTabId = id;
        OptionsPanel.Children.Clear();

        switch (id)
        {
            case "body_colors":
                AddColorSwatches(this.meta.BodyColors, includeCurrentBody: false);
                break;
            case "mirror_colors":
                AddColorSwatches(this.meta.MirrorColors, includeCurrentBody: true);
                break;
            case "wheel_designs":
                AddWheelDesignSwatches();
                break;
            case "wheel_colors":
                AddColorSwatches(this.meta.WheelColors, includeCurrentBody: false);
                break;
            case "caliper_colors":
                AddColorSwatches(this.meta.CaliperColors, includeCurrentBody: false);
                break;
        }

        UpdateTabStates();
        UpdateOptionSelection(id);
    }

    private void AddColorSwatches(ColorGroup group, bool includeCurrentBody)
    {
        if (includeCurrentBody)
        {
            AddColorSwatch("Current", currentBodyColor, group.Target, setMirrorAsBody: true);
        }

        foreach (var color in group.Colors)
        {
            AddColorSwatch(color.Name, ParseColor(color.Value), group.Target, setMirrorAsBody: false);
        }
    }

    private void AddColorSwatch(string name, Color color, string target, bool setMirrorAsBody)
    {
        var label = CreateSwatchLabel(name);
        var layout = CreateColorSwatchContent(color, label);
        var button = new Button
        {
            Width = 128,
            Height = 81,
            Margin = new Avalonia.Thickness(12, 16),
            Padding = new Avalonia.Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromArgb(51, 255, 255, 255)),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new CornerRadius(6),
            Content = layout,
            Tag = new SwatchOption(target, ColorKey(color)),
        };
        button.Width = SwatchWidth;
        button.Height = SwatchHeight;
        button.Margin = new Avalonia.Thickness(12, 16);

        AttachSwatchHover(button, label);
        button.Click += (_, _) =>
        {
            if (target == this.meta.BodyColors.Target)
            {
                currentBodyColor = color;
                this.selectedValues[target] = ColorKey(color);
            }

            if (target == this.meta.MirrorColors.Target)
            {
                mirrorUsesBodyColor = setMirrorAsBody;
                currentMirrorColor = color;
                this.selectedValues[target] = ColorKey(color);
            }

            SceneBridge.SetMaterialColor(target, color);

            if (target == this.meta.BodyColors.Target && mirrorUsesBodyColor)
            {
                currentMirrorColor = color;
                this.selectedValues[this.meta.MirrorColors.Target] = ColorKey(color);
                SceneBridge.SetMaterialColor(this.meta.MirrorColors.Target, color);
            }

            UpdateOptionSelection(activeTabId);
        };

        OptionsPanel.Children.Add(button);
    }

    private void AddWheelDesignSwatches()
    {
        foreach (var design in this.meta.WheelDesigns.Designs)
        {
            var imageUri = new Uri(
                $"avares://AutomotiveConfigurator.AvaloniaEvergine/Assets/AutomotiveConfigurator/aventador/{design.Thumb}.png");
            using var stream = AssetLoader.Open(imageUri);
            var image = new Image
            {
                Source = new Bitmap(stream),
                Stretch = Stretch.UniformToFill,
            };

            var label = CreateSwatchLabel(design.Name);
            var layout = new Grid();
            layout.Children.Add(image);
            layout.Children.Add(CreateSwatchGloss());
            layout.Children.Add(label);

            var button = new Button
            {
                Width = SwatchWidth,
                Height = SwatchHeight,
                Margin = new Avalonia.Thickness(8, 10),
                Padding = new Avalonia.Thickness(0),
                Background = Brushes.Black,
                BorderBrush = new SolidColorBrush(Color.FromArgb(51, 255, 255, 255)),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new CornerRadius(6),
                Content = layout,
                Tag = new SwatchOption(WheelDesignSelectionTarget, design.Value),
            };

            AttachSwatchHover(button, label);
            button.Click += (_, _) =>
            {
                this.selectedWheelDesign = design.Value;
                SceneBridge.ShowWheelDesign(design.Value);
                UpdateOptionSelection(activeTabId);
            };
            OptionsPanel.Children.Add(button);
        }
    }

    private IAutomotiveSceneBridge SceneBridge =>
        this.sceneBridge ?? throw new InvalidOperationException("The Evergine render surface has not been created.");

    private void AttachRenderSurface()
    {
        if (this.sceneBridge is not null)
        {
            return;
        }

        var renderSurface = new EvergineRenderHost();
        this.sceneBridge = renderSurface;
        ViewportHost.Content = renderSurface;
    }

    private static Grid CreateColorSwatchContent(Color color, Border label)
    {
        var layout = new Grid
        {
            ClipToBounds = true,
        };

        layout.Children.Add(new Rectangle
        {
            Fill = new SolidColorBrush(color),
            RadiusX = 6,
            RadiusY = 6,
        });
        layout.Children.Add(CreateSwatchGloss());
        layout.Children.Add(label);
        return layout;
    }

    private static Border CreateSwatchGloss()
    {
        return new Border
        {
            IsHitTestVisible = false,
            CornerRadius = new CornerRadius(5),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new(Color.FromArgb(128, 255, 255, 255), 0),
                    new(Color.FromArgb(64, 255, 255, 255), 0.5),
                    new(Color.FromArgb(128, 255, 255, 255), 0.5),
                    new(Color.FromArgb(0, 255, 255, 255), 1),
                },
            },
        };
    }

    private static Border CreateSwatchLabel(string text)
    {
        return new Border
        {
            Opacity = 0,
            Background = new SolidColorBrush(Color.FromArgb(64, 0, 0, 0)),
            Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = SwatchFadeDuration,
                },
            },
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeight.Light,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            },
        };
    }

    private static void AttachSwatchHover(InputElement hitTarget, Border label)
    {
        hitTarget.PointerEntered += (_, _) => label.Opacity = 1;
        hitTarget.PointerExited += (_, _) => label.Opacity = 0;
    }

    private void UpdateTabStates()
    {
        foreach (var (id, button) in this.tabButtons)
        {
            SetTabVisual(button, isActive: id == activeTabId, isHover: false);
        }
    }

    private void UpdateOptionSelection(string? _)
    {
        foreach (var child in OptionsPanel.Children)
        {
            if (child is not Button button || button.Tag is not SwatchOption option)
            {
                continue;
            }

            var selected = option.Target == WheelDesignSelectionTarget
                ? string.Equals(option.Value, selectedWheelDesign, StringComparison.Ordinal)
                : this.selectedValues.TryGetValue(option.Target, out var value) &&
                  string.Equals(value, option.Value, StringComparison.Ordinal);

            SetSwatchSelected(button, selected);
        }
    }

    private static void SetSwatchSelected(Button button, bool selected)
    {
        button.BorderBrush = selected
            ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(51, 255, 255, 255));
    }

    private static void SetTabVisual(Button button, bool isActive, bool isHover)
    {
        if (button.Content is TextBlock text)
        {
            text.Foreground = isActive || isHover ? Brushes.Black : new SolidColorBrush(Color.FromRgb(153, 153, 153));
        }

        button.Background = isActive
            ? new SolidColorBrush(Color.FromRgb(204, 204, 204))
            : isHover
                ? Brushes.White
                : new SolidColorBrush(Color.FromArgb(190, 0, 0, 0));
    }

    private static Color ParseColor(string value)
    {
        return Color.Parse(value);
    }

    private static string ColorKey(Color color)
    {
        return $"{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static void OpenGitHub()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/jsuarezruiz/evergine-avalonia-showcase",
            UseShellExecute = true,
        });
    }
}
