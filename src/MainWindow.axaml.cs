using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AutomotiveConfigurator.AvaloniaEvergine.Controls;
using AutomotiveConfigurator.AvaloniaEvergine.Models;
using AutomotiveConfigurator.AvaloniaEvergine.Rendering;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AutomotiveConfigurator.AvaloniaEvergine;

public partial class MainWindow : Window
{
    internal const string WheelDesignSelectionTarget = "__wheel_design";
    private const int MaxCinematicStartAttempts = 10;
    private static readonly TimeSpan CinematicStartRetryInterval = TimeSpan.FromMilliseconds(100);
    private const string MetaResourceUri =
        "avares://AutomotiveConfigurator.AvaloniaEvergine/Assets/AutomotiveConfigurator/aventador/meta.json";

    private readonly ObservableCollection<ConfiguratorTabItem> tabs = new();
    private readonly ObservableCollection<ConfiguratorOptionItem> options = new();
    private readonly Dictionary<string, ConfiguratorTabItem> tabItems = new(StringComparer.Ordinal);
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

    internal bool HasReadyRenderSurface =>
        this.sceneBridge is EvergineRenderHost { IsReady: true };

    private bool CanStartCinematicNow =>
        SceneBridge.IsSceneReady &&
        !SceneBridge.HasSceneError &&
        HasReadyRenderSurface;

    public MainWindow()
    {
        InitializeComponent();

        TabItemsControl.ItemsSource = this.tabs;
        OptionsItemsControl.ItemsSource = this.options;
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
        this.tabItems.Clear();
        this.tabs.Clear();
        this.options.Clear();

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
        var item = new ConfiguratorTabItem(id, label);
        this.tabItems[id] = item;
        this.tabs.Add(item);
    }

    private void ToggleTab(string id)
    {
        if (activeTabId == id)
        {
            activeTabId = null;
            this.options.Clear();
            UpdateTabStates();
            return;
        }

        ShowTab(id);
    }

    private void ShowTab(string id)
    {
        activeTabId = id;
        this.options.Clear();

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
        this.options.Add(ConfiguratorOptionItem.CreateColor(name, color, target, ColorKey(color), setMirrorAsBody));
    }

    private void AddWheelDesignSwatches()
    {
        foreach (var design in this.meta.WheelDesigns.Designs)
        {
            var imageUri = new Uri(
                $"avares://AutomotiveConfigurator.AvaloniaEvergine/Assets/AutomotiveConfigurator/aventador/{design.Thumb}.png");
            using var stream = AssetLoader.Open(imageUri);
            this.options.Add(ConfiguratorOptionItem.CreateWheelDesign(
                design.Name,
                WheelDesignSelectionTarget,
                design.Value,
                new Bitmap(stream)));
        }
    }

    private void TabButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ConfiguratorTabItem tab })
        {
            ToggleTab(tab.Id);
        }
    }

    private static void TabButton_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: ConfiguratorTabItem tab })
        {
            tab.IsHovered = true;
        }
    }

    private static void TabButton_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: ConfiguratorTabItem tab })
        {
            tab.IsHovered = false;
        }
    }

    private void OptionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ConfiguratorOptionItem option })
        {
            return;
        }

        if (option.Target == WheelDesignSelectionTarget)
        {
            this.selectedWheelDesign = option.Value;
            SceneBridge.ShowWheelDesign(option.Value);
            UpdateOptionSelection(activeTabId);
            return;
        }

        if (option.SwatchColor is not { } color)
        {
            return;
        }

        if (option.Target == this.meta.BodyColors.Target)
        {
            currentBodyColor = color;
            this.selectedValues[option.Target] = option.Value;
        }

        if (option.Target == this.meta.MirrorColors.Target)
        {
            mirrorUsesBodyColor = option.SetMirrorAsBody;
            currentMirrorColor = color;
            this.selectedValues[option.Target] = option.Value;
        }

        SceneBridge.SetMaterialColor(option.Target, color);

        if (option.Target == this.meta.BodyColors.Target && mirrorUsesBodyColor)
        {
            currentMirrorColor = color;
            this.selectedValues[this.meta.MirrorColors.Target] = option.Value;
            SceneBridge.SetMaterialColor(this.meta.MirrorColors.Target, color);
        }

        UpdateOptionSelection(activeTabId);
    }

    private static void OptionButton_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: ConfiguratorOptionItem option })
        {
            option.IsHovered = true;
        }
    }

    private static void OptionButton_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: ConfiguratorOptionItem option })
        {
            option.IsHovered = false;
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

    private void UpdateTabStates()
    {
        foreach (var (id, item) in this.tabItems)
        {
            item.IsActive = id == activeTabId;
        }
    }

    private void UpdateOptionSelection(string? _)
    {
        foreach (var option in this.options)
        {
            var selected = option.Target == WheelDesignSelectionTarget
                ? string.Equals(option.Value, selectedWheelDesign, StringComparison.Ordinal)
                : this.selectedValues.TryGetValue(option.Target, out var value) &&
                  string.Equals(value, option.Value, StringComparison.Ordinal);

            option.IsSelected = selected;
        }
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
