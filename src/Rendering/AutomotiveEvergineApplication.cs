using System;
using AutomotiveConfigurator.AvaloniaEvergine.Models;
using AvaloniaColor = Avalonia.Media.Color;
using Evergine.Common;
using Evergine.Common.Graphics;
using Evergine.Common.IO;
using Evergine.Framework;
using Evergine.Framework.Graphics.Effects;
using Evergine.Framework.Services;
using Evergine.Framework.Threading;

namespace AutomotiveConfigurator.AvaloniaEvergine.Rendering;

public sealed class AutomotiveEvergineApplication : Evergine.Framework.Application, IAutomotiveSceneBridge
{
    private readonly object stateGate = new();
    private AutomotiveScene? scene;
    private AutomotiveAssetSet? pendingAssets;
    private ConfiguratorMeta? pendingMeta;

    public AutomotiveEvergineApplication()
    {
        this.Container.Register<Clock>();
        this.Container.Register<TimerFactory>();
        this.Container.Register<Evergine.Framework.Services.Random>();
        this.Container.Register<ErrorHandler>();
        this.Container.Register<ScreenContextManager>();
        this.Container.Register<GraphicsPresenter>();
        this.Container.Register<AssetsService>();
        this.Container.Register<ForegroundTaskSchedulerService>();
    }

    public override void Initialize()
    {
        base.Initialize();
        PreloadCoreGraphicsAssets(this.Container.Resolve<AssetsService>());

        var screenContextManager = this.Container.Resolve<ScreenContextManager>();
        this.scene = new AutomotiveScene();

        lock (this.stateGate)
        {
            if (this.pendingAssets is not null && this.pendingMeta is not null)
            {
                this.scene.InitializeScene(this.pendingAssets, this.pendingMeta);
            }
        }

        screenContextManager.To(new ScreenContext(this.scene));
    }

    public bool IsSceneReady => this.scene?.IsSceneReady ?? false;

    public bool HasSceneError => this.scene?.HasSceneError ?? false;

    public string SceneStatus => this.scene?.SceneStatus ?? "Waiting for Evergine application initialization.";

    public void InitializeScene(AutomotiveAssetSet assets, ConfiguratorMeta meta)
    {
        lock (this.stateGate)
        {
            this.pendingAssets = assets;
            this.pendingMeta = meta;
            this.scene?.InitializeScene(assets, meta);
        }
    }

    public void StartCinematic()
    {
        this.scene?.StartCinematic();
    }

    public void StopCinematic()
    {
        this.scene?.StopCinematic();
    }

    public void SetMaterialColor(string materialName, AvaloniaColor color)
    {
        this.scene?.SetMaterialColor(materialName, color);
    }

    public void ShowWheelDesign(string objectName)
    {
        this.scene?.ShowWheelDesign(objectName);
    }

    public void OrbitCamera(float deltaX, float deltaY)
    {
        this.scene?.OrbitCamera(deltaX, deltaY);
    }

    public void ZoomCamera(float delta)
    {
        this.scene?.ZoomCamera(delta);
    }

    private static void PreloadCoreGraphicsAssets(AssetsService assetsService)
    {
        _ = LoadRequiredAsset<Effect>(assetsService, DefaultResourcesIDs.StandardEffectID, "standard material effect");
        _ = LoadRequiredAsset<Effect>(assetsService, DefaultResourcesIDs.RenderQuadID, "render-to-framebuffer effect");
        _ = LoadRequiredAsset<RenderLayerDescription>(assetsService, DefaultResourcesIDs.OpaqueRenderLayerID, "opaque render layer");
        _ = LoadRequiredAsset<RenderLayerDescription>(assetsService, DefaultResourcesIDs.AlphaRenderLayerID, "alpha render layer");
        _ = LoadRequiredAsset<RenderLayerDescription>(assetsService, DefaultResourcesIDs.AlphaDoubleSidedRenderLayerID, "alpha double-sided render layer");
        _ = LoadRequiredAsset<SamplerState>(assetsService, DefaultResourcesIDs.LinearClampSamplerID, "linear clamp sampler");
        _ = LoadRequiredAsset<SamplerState>(assetsService, DefaultResourcesIDs.LinearWrapSamplerID, "linear wrap sampler");
    }

    private static T LoadRequiredAsset<T>(AssetsService assetsService, Guid id, string description)
        where T : class, ILoadable
    {
        var asset = assetsService.Load<T>(id, forceNewInstance: false);
        if (asset is null)
        {
            throw new InvalidOperationException(
                $"Evergine could not load the {description} asset ({id:D}) during application startup.");
        }

        return asset;
    }
}
