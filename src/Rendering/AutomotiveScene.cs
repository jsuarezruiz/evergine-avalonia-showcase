using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutomotiveConfigurator.AvaloniaEvergine.Models;
using Evergine.Common.Graphics;
using Evergine.Common.IO;
using Evergine.Components.Graphics3D;
using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Framework.Graphics.Effects;
using Evergine.Framework.Graphics.Materials;
using Evergine.Framework.Runtimes;
using Evergine.Framework.Services;
using Evergine.Mathematics;
using Evergine.Runtimes.GLB;
using AvaloniaColor = Avalonia.Media.Color;
using EvergineColor = Evergine.Common.Graphics.Color;

namespace AutomotiveConfigurator.AvaloniaEvergine.Rendering;

public sealed class AutomotiveScene : Scene, IAutomotiveSceneBridge
{
    private const float OrbitMinDistance = 16f;
    private const float OrbitMaxDistance = 32f;
    private const float OrbitMinPitch = MathHelper.PiOver2 - 1.6f;
    private const float OrbitMaxPitch = MathHelper.PiOver2 - 0.75f;
    private const float OrbitRotateSpeed = 0.00062f;
    private const float OrbitDamping = 0.93f;
    private const float OrbitAutoRotateSpeed = 0.00525f;

    private sealed record ModelLoadResult(int RequestId, Model EnvironmentModel, Model StageModel, Model CarModel);

    private readonly object stateGate = new();
    private readonly Dictionary<string, AvaloniaColor> pendingColors = new(StringComparer.Ordinal);
    private readonly HashSet<string> configurableMaterials = new(StringComparer.Ordinal);
    private AutomotiveAssetSet? assets;
    private Entity? environmentEntity;
    private Entity? stageEntity;
    private Entity? carEntity;
    private Entity? reflectionProbeEntity;
    private Entity? cameraEntity;
    private Camera3D? camera;
    private Transform3D? cameraTransform;
    private Transform3D? reflectionProbeTransform;
    private ReflectionProbeGenerator? reflectionProbeGenerator;
    private DirectionalLight? keyLight;
    private bool needsModelLoad;
    private bool cinematicEnabled;
    private bool pendingCameraReset;
    private bool sceneReady;
    private bool sceneLoadFailed;
    private string sceneStatus = "Waiting for scene initialization.";
    private string? pendingWheelDesign;
    private TimeSpan cinematicTime;
    private Effect? standardEffect;
    private Effect? skyboxEffect;
    private RenderLayerDescription? opaqueLayerDescription;
    private RenderLayerDescription? skyboxLayerDescription;
    private RenderLayerDescription? alphaDoubleSidedLayerDescription;
    private int modelLoadRequestId;
    private Task<ModelLoadResult>? modelLoadTask;
    private bool hasPendingVisualChanges;
    private float orbitYaw;
    private float orbitPitch;
    private float orbitDistance;
    private float pendingOrbitDeltaX;
    private float pendingOrbitDeltaY;
    private float pendingZoomDelta;
    private float orbitVelocityX;
    private float orbitVelocityY;
    private float zoomVelocity;
    private Vector3 orbitTarget;

    protected override void CreateScene()
    {
        this.CreateCamera();
        this.CreateLights();
        this.CreateReflectionProbe();
    }

    public bool IsSceneReady
    {
        get
        {
            lock (this.stateGate)
            {
                return this.sceneReady;
            }
        }
    }

    public bool HasSceneError
    {
        get
        {
            lock (this.stateGate)
            {
                return this.sceneLoadFailed;
            }
        }
    }

    public string SceneStatus
    {
        get
        {
            lock (this.stateGate)
            {
                return this.sceneStatus;
            }
        }
    }

    public void InitializeScene(AutomotiveAssetSet assets, ConfiguratorMeta meta)
    {
        lock (this.stateGate)
        {
            this.assets = assets;
            this.configurableMaterials.Clear();
            AddMaterialTarget(this.configurableMaterials, meta.BodyColors.Target);
            AddMaterialTarget(this.configurableMaterials, meta.MirrorColors.Target);
            AddMaterialTarget(this.configurableMaterials, meta.WheelColors.Target);
            AddMaterialTarget(this.configurableMaterials, meta.CaliperColors.Target);
            this.needsModelLoad = true;
            this.modelLoadRequestId++;
            this.hasPendingVisualChanges = true;
            this.sceneReady = false;
            this.sceneLoadFailed = false;
            this.sceneStatus = "Preparing automotive scene assets.";
        }

    }

    public void StartCinematic()
    {
        lock (this.stateGate)
        {
            this.cinematicTime = TimeSpan.Zero;
            this.cinematicEnabled = true;
            this.pendingCameraReset = false;
        }
    }

    public void StopCinematic()
    {
        lock (this.stateGate)
        {
            this.cinematicEnabled = false;
            this.pendingCameraReset = true;
        }
    }

    public void SetMaterialColor(string materialName, AvaloniaColor color)
    {
        lock (this.stateGate)
        {
            this.pendingColors[materialName] = color;
            this.hasPendingVisualChanges = true;
        }
    }

    public void ShowWheelDesign(string objectName)
    {
        lock (this.stateGate)
        {
            this.pendingWheelDesign = objectName;
            this.hasPendingVisualChanges = true;
        }
    }

    public void OrbitCamera(float deltaX, float deltaY)
    {
        lock (this.stateGate)
        {
            this.cinematicEnabled = false;
            this.pendingOrbitDeltaX += deltaX;
            this.pendingOrbitDeltaY += deltaY;
        }
    }

    public void ZoomCamera(float delta)
    {
        lock (this.stateGate)
        {
            this.cinematicEnabled = false;
            this.pendingZoomDelta += delta;
        }
    }

    protected override void Update(TimeSpan gameTime)
    {
        base.Update(gameTime);

        if (this.needsModelLoad)
        {
            this.BeginModelLoad();
        }

        this.CompleteModelLoadIfReady();
        this.ApplyPendingVisualChanges();
        this.ApplyPendingCameraInput(gameTime);

        if (this.AdvanceCinematic(gameTime, out var cinematicSnapshot))
        {
            this.UpdateCinematic(cinematicSnapshot);
        }
    }

    private void CreateCamera()
    {
        this.cameraTransform = new Transform3D();
        this.camera = new Camera3D
        {
            DisplayTag = "DefaultDisplay",
            ClearFlags = ClearFlags.All,
            BackgroundColor = new EvergineColor(3, 3, 4),
            NearPlane = 0.1f,
            FarPlane = 1000f,
            FieldOfView = MathHelper.ToRadians(42f),
            HDREnabled = true,
            AutoExposureEnabled = false,
            Exposure = 1.08f,
            Compensation = 0.15f,
        };

        this.cameraEntity = new Entity("AutomotiveCamera")
            .AddComponent(this.cameraTransform)
            .AddComponent(this.camera);

        this.Managers.EntityManager.Add(this.cameraEntity);
        this.InitializeOrbitCamera();
        this.ApplyOrbitCamera();
    }

    private void CreateLights()
    {
        this.AddRectangleLight("LeftSoftbox", new Vector3(0f, 16f, -18f), 12f, 6f, 8f);
        this.AddRectangleLight("RightSoftbox", new Vector3(0f, 16f, 18f), 12f, 6f, 8f);

        var keyTransform = new Transform3D
        {
            LocalPosition = new Vector3(-12f, 10f, 8f),
        };
        keyTransform.LookAt(new Vector3(0f, 1.5f, 0f));

        this.keyLight = new DirectionalLight
        {
            Color = EvergineColor.White,
            Intensity = 2.65f,
            IsShadowEnabled = true,
            ShadowBias = 0.0015f,
            ShadowDistance = 70f,
            ShadowOpacity = 0.42f,
        };

        var keyLight = new Entity("KeyLight")
            .AddComponent(keyTransform)
            .AddComponent(this.keyLight);

        var fillLight = new Entity("FillLight")
            .AddComponent(new Transform3D
            {
                LocalPosition = new Vector3(7f, 4f, -8f),
            })
            .AddComponent(new PointLight
            {
                Color = new EvergineColor(160, 185, 255),
                Intensity = 1.15f,
                LightRange = 35f,
            });

        var noseLight = new Entity("FrontLowFill")
            .AddComponent(new Transform3D
            {
                LocalPosition = new Vector3(-9f, 2.2f, 0f),
            })
            .AddComponent(new PointLight
            {
                Color = new EvergineColor(255, 226, 196),
                Intensity = 0.75f,
                LightRange = 22f,
            });

        this.Managers.EntityManager.Add(keyLight);
        this.Managers.EntityManager.Add(fillLight);
        this.Managers.EntityManager.Add(noseLight);

        this.Managers.EnvironmentManager.IntensityMultiplier = 0.72f;
        this.Managers.EnvironmentManager.SunLight = this.keyLight;
    }

    private void CreateReflectionProbe()
    {
        this.reflectionProbeTransform = new Transform3D
        {
            LocalPosition = this.orbitTarget,
        };

        this.reflectionProbeGenerator = new ReflectionProbeGenerator
        {
            ProbeSize = PowerOfTwoSize.Size_512,
            NearPlane = 0.25f,
            FarPlane = 90f,
            HDREnabled = true,
            ClearFlags = ClearFlags.All,
            BackgroundColor = new EvergineColor(3, 3, 4),
            UpdateStrategy = ReflectionProbeUpdateStrategy.OnDemand,
        };
        this.reflectionProbeGenerator.OnReflectionProbeUpdated += (_, probe) =>
        {
            this.Managers.EnvironmentManager.IBLReflectionProbe = probe;
        };

        this.reflectionProbeEntity = new Entity("StudioReflectionProbe")
            .AddComponent(this.reflectionProbeTransform)
            .AddComponent(this.reflectionProbeGenerator);

        this.Managers.EntityManager.Add(this.reflectionProbeEntity);
    }

    private void AddRectangleLight(string name, Vector3 position, float width, float height, float intensity)
    {
        var transform = new Transform3D
        {
            LocalPosition = position,
        };
        transform.LookAt(new Vector3(0f, 1.2f, 0f));

        var light = new RectangleLight
        {
            Width = width,
            Height = height,
            LightRange = 55f,
            Intensity = intensity,
            Color = EvergineColor.White,
        };

        this.Managers.EntityManager.Add(
            new Entity(name)
                .AddComponent(transform)
                .AddComponent(light));
    }

    private void BeginModelLoad()
    {
        AutomotiveAssetSet? assetSet;
        int requestId;

        lock (this.stateGate)
        {
            if (!this.needsModelLoad || this.assets is null || this.modelLoadTask is not null)
            {
                return;
            }

            assetSet = this.assets;
            requestId = this.modelLoadRequestId;
            this.needsModelLoad = false;
            this.sceneStatus = "Importing environment, stage, and car models.";
        }

        var assetsService = Evergine.Framework.Application.Current.Container.Resolve<AssetsService>();
        this.LoadCoreGraphicsAssets(assetsService);
        this.modelLoadTask = Task.Run(() => LoadModelsOnWorker(assetSet, requestId));
    }

    private void CompleteModelLoadIfReady()
    {
        var task = this.modelLoadTask;
        if (task is null || !task.IsCompleted)
        {
            return;
        }

        this.modelLoadTask = null;

        ModelLoadResult result;
        try
        {
            result = task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            lock (this.stateGate)
            {
                this.sceneLoadFailed = true;
                this.sceneReady = false;
                this.sceneStatus = $"Failed to import automotive GLB scene: {ex.GetBaseException().Message}";
            }

            return;
        }

        lock (this.stateGate)
        {
            if (result.RequestId != this.modelLoadRequestId)
            {
                return;
            }

            this.hasPendingVisualChanges = true;
            this.sceneStatus = "Applying configured materials.";
        }

        this.RemoveEntity(this.carEntity);
        this.RemoveEntity(this.stageEntity);
        this.RemoveEntity(this.environmentEntity);
        var assetsService = Evergine.Framework.Application.Current.Container.Resolve<AssetsService>();

        this.environmentEntity = result.EnvironmentModel.InstantiateModelHierarchy("EnvironmentDome", assetsService);
        this.EnsureRenderableMaterials(this.environmentEntity);
        this.Managers.EntityManager.Add(this.environmentEntity);

        this.stageEntity = result.StageModel.InstantiateModelHierarchy("Stage", assetsService);
        this.EnsureRenderableMaterials(this.stageEntity);
        this.Managers.EntityManager.Add(this.stageEntity);

        this.carEntity = result.CarModel.InstantiateModelHierarchy("Aventador", assetsService);
        this.EnsureRenderableMaterials(this.carEntity);
        this.Managers.EntityManager.Add(this.carEntity);
        this.ConfigureOrbitCamera();
        this.UpdateReflectionProbe();

    }

    private static ModelLoadResult LoadModelsOnWorker(AutomotiveAssetSet assetSet, int requestId)
    {
        var environmentModel = GLBRuntime.Instance
            .Read(ToAssetPath(assetSet.EnvironmentModelPath))
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        var stageModel = GLBRuntime.Instance
            .Read(ToAssetPath(assetSet.StageModelPath))
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        var carModel = GLBRuntime.Instance
            .Read(ToAssetPath(assetSet.CarModelPath))
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        return new ModelLoadResult(requestId, environmentModel, stageModel, carModel);
    }

    private void ApplyPendingVisualChanges()
    {
        if (this.carEntity is null)
        {
            return;
        }

        KeyValuePair<string, AvaloniaColor>[] colors;
        string? wheelDesign;

        lock (this.stateGate)
        {
            if (!this.hasPendingVisualChanges)
            {
                return;
            }

            colors = this.pendingColors.ToArray();
            wheelDesign = this.pendingWheelDesign;
            this.hasPendingVisualChanges = false;
        }

        foreach (var (materialName, color) in colors)
        {
            this.ApplyMaterialColor(materialName, color);
        }

        if (!string.IsNullOrWhiteSpace(wheelDesign))
        {
            this.ApplyWheelDesign(wheelDesign);
        }

        lock (this.stateGate)
        {
            if (!this.sceneLoadFailed && !this.sceneReady)
            {
                this.sceneReady = true;
                this.sceneStatus = "Automotive scene ready.";
            }
        }
    }

    private void ApplyMaterialColor(string materialName, AvaloniaColor color)
    {
        if (this.carEntity is null)
        {
            return;
        }

        foreach (var materialComponent in this.carEntity.FindComponentsInChildren<MaterialComponent>(isExactType: false))
        {
            if (!string.Equals(materialComponent.AsignedTo, materialName, StringComparison.Ordinal))
            {
                continue;
            }

            if (materialComponent.Material is null)
            {
                materialComponent.Material = this.CreateStandardMaterial(EvergineColor.White, materialName);
            }

            var standardMaterial = materialComponent.Material.Effect is null
                ? new StandardMaterial(this.GetRequiredStandardEffect())
                : new StandardMaterial(materialComponent.Material);

            standardMaterial.LayerDescription =
                materialComponent.Material.LayerDescription ?? this.GetRequiredOpaqueLayerDescription();
            standardMaterial.BaseColorLinear = ToLinearColor(color);
            standardMaterial.Alpha = color.A / 255f;
            this.ApplyMaterialPreset(materialName, standardMaterial);
            materialComponent.Material = standardMaterial.Material;
        }
    }

    private void LoadCoreGraphicsAssets(AssetsService assetsService)
    {
        this.standardEffect = LoadRequiredAsset<Effect>(
            assetsService,
            DefaultResourcesIDs.StandardEffectID,
            "standard material effect");

        this.skyboxEffect = LoadRequiredAsset<Effect>(
            assetsService,
            DefaultResourcesIDs.SkyboxEffectId,
            "skybox effect");

        _ = LoadRequiredAsset<Effect>(
            assetsService,
            DefaultResourcesIDs.RenderQuadID,
            "render-to-framebuffer effect");

        this.opaqueLayerDescription = LoadRequiredAsset<RenderLayerDescription>(
            assetsService,
            DefaultResourcesIDs.OpaqueRenderLayerID,
            "opaque render layer");

        this.skyboxLayerDescription = LoadRequiredAsset<RenderLayerDescription>(
            assetsService,
            DefaultResourcesIDs.SkyboxRenderLayerID,
            "skybox render layer");

        _ = LoadRequiredAsset<RenderLayerDescription>(
            assetsService,
            DefaultResourcesIDs.AlphaRenderLayerID,
            "alpha render layer");

        this.alphaDoubleSidedLayerDescription = LoadRequiredAsset<RenderLayerDescription>(
            assetsService,
            DefaultResourcesIDs.AlphaDoubleSidedRenderLayerID,
            "alpha double-sided render layer");

        _ = LoadRequiredAsset<SamplerState>(
            assetsService,
            DefaultResourcesIDs.LinearClampSamplerID,
            "linear clamp sampler");

        _ = LoadRequiredAsset<SamplerState>(
            assetsService,
            DefaultResourcesIDs.LinearWrapSamplerID,
            "linear wrap sampler");
    }

    private void EnsureRenderableMaterials(Entity root)
    {
        foreach (var materialComponent in root.FindComponentsInChildren<MaterialComponent>(isExactType: false))
        {
            if (materialComponent.Material is null)
            {
                materialComponent.Material = this.CreateStandardMaterial(EvergineColor.White, materialComponent.AsignedTo);
                this.ConfigureSpecialImportedMaterial(materialComponent);
                continue;
            }

            if (materialComponent.Material.Effect is null)
            {
                materialComponent.Material = this.CreateStandardMaterialFromExisting(
                    materialComponent.Material,
                    materialComponent.AsignedTo);
                this.ConfigureSpecialImportedMaterial(materialComponent);
                continue;
            }

            if (materialComponent.Material.LayerDescription is null)
            {
                materialComponent.Material.LayerDescription = this.GetRequiredOpaqueLayerDescription();
            }

            this.ConfigureSpecialImportedMaterial(materialComponent);

            if (this.configurableMaterials.Contains(materialComponent.AsignedTo))
            {
                var standardMaterial = new StandardMaterial(materialComponent.Material);
                this.ApplyMaterialPreset(materialComponent.AsignedTo, standardMaterial);
                materialComponent.Material = standardMaterial.Material;
            }
        }
    }

    private Material CreateStandardMaterial(EvergineColor color, string? materialName = null)
    {
        var standardMaterial = new StandardMaterial(this.GetRequiredStandardEffect())
        {
            BaseColor = color,
            Alpha = color.A / 255f,
            IBLEnabled = false,
            LayerDescription = this.GetRequiredOpaqueLayerDescription(),
            LightingEnabled = true,
        };

        if (!string.IsNullOrWhiteSpace(materialName))
        {
            this.ApplyMaterialPreset(materialName, standardMaterial);
        }

        return standardMaterial.Material;
    }

    private Material CreateStandardMaterialFromExisting(Material sourceMaterial, string? materialName)
    {
        var material = new StandardMaterial(this.GetRequiredStandardEffect())
        {
            LayerDescription = sourceMaterial.LayerDescription ?? this.GetRequiredOpaqueLayerDescription(),
            IBLEnabled = true,
            LightingEnabled = true,
        };

        try
        {
            var source = new StandardMaterial(sourceMaterial);
            material.BaseColor = source.BaseColor;
            material.Alpha = source.Alpha;
            material.AlphaCutout = source.AlphaCutout;
            material.ReferenceAlpha = source.ReferenceAlpha;
            material.Metallic = source.Metallic;
            material.Roughness = source.Roughness;
            material.Reflectance = source.Reflectance;
            material.EmissiveColor = source.EmissiveColor;
            material.EmissiveCompensation = source.EmissiveCompensation;
            material.BaseColorTexture = source.BaseColorTexture;
            material.BaseColorSampler = source.BaseColorSampler;
            material.MetallicRoughnessTexture = source.MetallicRoughnessTexture;
            material.MetallicRoughnessSampler = source.MetallicRoughnessSampler;
            material.NormalTexture = source.NormalTexture;
            material.NormalSampler = source.NormalSampler;
            material.OcclusionTexture = source.OcclusionTexture;
            material.OcclusionSampler = source.OcclusionSampler;
            material.EmissiveTexture = source.EmissiveTexture;
            material.EmissiveSampler = source.EmissiveSampler;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AutomotiveConfigurator] Falling back to a plain material because imported material '{materialName}' could not be copied: {ex.GetBaseException().Message}");
            material.BaseColor = EvergineColor.White;
            material.Alpha = 1f;
        }

        if (!string.IsNullOrWhiteSpace(materialName))
        {
            this.ApplyMaterialPreset(materialName, material);
        }

        return material.Material;
    }

    private void ApplyMaterialPreset(string materialName, StandardMaterial material)
    {
        material.LightingEnabled = true;
        material.IBLEnabled = true;

        if (string.Equals(materialName, AutomotiveSceneDefaults.BodyMaterial, StringComparison.Ordinal))
        {
            material.Metallic = 0.36f;
            material.Roughness = 0.18f;
            material.Reflectance = 0.72f;
            return;
        }

        if (string.Equals(materialName, AutomotiveSceneDefaults.MirrorCoverMaterial, StringComparison.Ordinal))
        {
            material.Metallic = 0.54f;
            material.Roughness = 0.16f;
            material.Reflectance = 0.72f;
            return;
        }

        if (string.Equals(materialName, AutomotiveSceneDefaults.AlloyWheelsMaterial, StringComparison.Ordinal))
        {
            material.Metallic = 0.92f;
            material.Roughness = 0.29f;
            material.Reflectance = 0.62f;
            return;
        }

        if (string.Equals(materialName, AutomotiveSceneDefaults.BrakeCaliperMaterial, StringComparison.Ordinal))
        {
            material.Metallic = 0.22f;
            material.Roughness = 0.31f;
            material.Reflectance = 0.5f;
        }
    }

    private Effect GetRequiredStandardEffect()
    {
        return this.standardEffect
            ?? throw new InvalidOperationException("Evergine StandardEffectID was not loaded before creating scene materials.");
    }

    private RenderLayerDescription GetRequiredOpaqueLayerDescription()
    {
        return this.opaqueLayerDescription
            ?? throw new InvalidOperationException("Evergine OpaqueRenderLayerID was not loaded before creating scene materials.");
    }

    private Effect GetRequiredSkyboxEffect()
    {
        return this.skyboxEffect
            ?? throw new InvalidOperationException("Evergine SkyboxEffectId was not loaded before creating environment materials.");
    }

    private RenderLayerDescription GetRequiredSkyboxLayerDescription()
    {
        return this.skyboxLayerDescription
            ?? throw new InvalidOperationException("Evergine SkyboxRenderLayerID was not loaded before creating environment materials.");
    }

    private RenderLayerDescription GetRequiredAlphaDoubleSidedLayerDescription()
    {
        return this.alphaDoubleSidedLayerDescription
            ?? throw new InvalidOperationException("Evergine AlphaDoubleSidedRenderLayerID was not loaded before creating scene materials.");
    }

    private static T LoadRequiredAsset<T>(AssetsService assetsService, Guid id, string description)
        where T : class, Evergine.Common.ILoadable
    {
        var asset = assetsService.Load<T>(id, forceNewInstance: false);
        if (asset is null)
        {
            throw new InvalidOperationException(
                $"Evergine could not load the {description} asset ({id:D}). " +
                $"Verify the output Content folder exists under '{AppContext.BaseDirectory}' and contains the exported Evergine.Core files.");
        }

        return asset;
    }

    private void ApplyWheelDesign(string objectName)
    {
        if (this.carEntity is null)
        {
            return;
        }

        foreach (var entity in Enumerate(this.carEntity))
        {
            if (!entity.Name.StartsWith(AutomotiveSceneDefaults.RimObjectPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            entity.IsEnabled = string.Equals(entity.Name, objectName, StringComparison.Ordinal)
                || entity.Name.Contains(objectName, StringComparison.Ordinal);
        }
    }

    private void ConfigureSpecialImportedMaterial(MaterialComponent materialComponent)
    {
        if (string.Equals(materialComponent.AsignedTo, AutomotiveSceneDefaults.ShadowPlaneMaterial, StringComparison.Ordinal))
        {
            this.ConfigureShadowPlaneMaterial(materialComponent);
        }

        if (string.Equals(materialComponent.AsignedTo, AutomotiveSceneDefaults.EnvironmentMaterial, StringComparison.Ordinal))
        {
            this.ConfigureEnvironmentMaterial(materialComponent);
        }
    }

    private void ConfigureShadowPlaneMaterial(MaterialComponent materialComponent)
    {
        if (materialComponent.Material is null)
        {
            materialComponent.Material = this.CreateStandardMaterial(EvergineColor.White, materialComponent.AsignedTo);
        }

        var shadowMaterial = materialComponent.Material.Effect is null
            ? new StandardMaterial(this.GetRequiredStandardEffect())
            : new StandardMaterial(materialComponent.Material);

        try
        {
            var source = new StandardMaterial(materialComponent.Material);
            shadowMaterial.BaseColorTexture = source.BaseColorTexture;
            shadowMaterial.BaseColorSampler = source.BaseColorSampler;
            shadowMaterial.AlphaCutout = source.AlphaCutout;
            shadowMaterial.ReferenceAlpha = source.ReferenceAlpha;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AutomotiveConfigurator] Imported shadow material settings could not be copied: {ex.GetBaseException().Message}");
        }

        if (shadowMaterial.BaseColorTexture is null)
        {
            if (materialComponent.Owner is not null)
            {
                materialComponent.Owner.IsEnabled = false;
            }

            return;
        }

        shadowMaterial.BaseColor = EvergineColor.White;
        shadowMaterial.Alpha = 1f;
        shadowMaterial.Metallic = 0f;
        shadowMaterial.Roughness = 1f;
        shadowMaterial.Reflectance = 0f;
        shadowMaterial.LightingEnabled = false;
        shadowMaterial.IBLEnabled = false;
        shadowMaterial.LayerDescription = this.GetRequiredAlphaDoubleSidedLayerDescription();
        materialComponent.Material = shadowMaterial.Material;
    }

    private void ConfigureEnvironmentMaterial(MaterialComponent materialComponent)
    {
        if (materialComponent.Material is null)
        {
            return;
        }

        Texture? texture = null;
        SamplerState? sampler = null;
        try
        {
            var source = new StandardMaterial(materialComponent.Material);
            texture = source.BaseColorTexture ?? source.EmissiveTexture;
            sampler = source.BaseColorSampler ?? source.EmissiveSampler;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AutomotiveConfigurator] Imported environment material settings could not be read: {ex.GetBaseException().Message}");
        }

        if (texture is null)
        {
            if (materialComponent.Owner is not null)
            {
                materialComponent.Owner.IsEnabled = false;
            }

            return;
        }

        var skyboxMaterial = new SkyboxMaterial(this.GetRequiredSkyboxEffect())
        {
            Texture = texture,
            TextureSampler = sampler,
            Parameters_Intensity = 0.42f,
        };
        skyboxMaterial.Material.LayerDescription = this.GetRequiredSkyboxLayerDescription();
        materialComponent.Material = skyboxMaterial.Material;
    }

    private void InitializeOrbitCamera()
    {
        var target = ToVector3(AutomotiveSceneDefaults.OrbitCameraTarget);
        this.orbitTarget = target;
        var position = ToVector3(AutomotiveSceneDefaults.OrbitCameraPosition);
        var offset = position - target;

        this.orbitDistance = Math.Clamp(offset.Length(), OrbitMinDistance, OrbitMaxDistance);
        this.orbitYaw = MathF.Atan2(offset.X, offset.Z);
        this.orbitPitch = Math.Clamp(
            MathF.Asin(Math.Clamp(offset.Y / this.orbitDistance, -0.9f, 0.9f)),
            OrbitMinPitch,
            OrbitMaxPitch);
    }

    private void ApplyPendingCameraInput(TimeSpan gameTime)
    {
        float deltaX;
        float deltaY;
        float zoomDelta;
        bool resetCamera;

        lock (this.stateGate)
        {
            deltaX = this.pendingOrbitDeltaX;
            deltaY = this.pendingOrbitDeltaY;
            zoomDelta = this.pendingZoomDelta;
            resetCamera = this.pendingCameraReset;
            this.pendingOrbitDeltaX = 0f;
            this.pendingOrbitDeltaY = 0f;
            this.pendingZoomDelta = 0f;
            this.pendingCameraReset = false;
        }

        if (resetCamera)
        {
            this.orbitVelocityX = 0f;
            this.orbitVelocityY = 0f;
            this.zoomVelocity = 0f;
            this.ApplyOrbitCamera();
            return;
        }

        var hadInput = deltaX != 0f || deltaY != 0f || zoomDelta != 0f;
        if (hadInput)
        {
            this.orbitVelocityX += -deltaX * OrbitRotateSpeed;
            this.orbitVelocityY += -deltaY * OrbitRotateSpeed;
            this.zoomVelocity += -zoomDelta * 0.0085f;
        }

        var hasMotion =
            MathF.Abs(this.orbitVelocityX) > 0.00005f ||
            MathF.Abs(this.orbitVelocityY) > 0.00005f ||
            MathF.Abs(this.zoomVelocity) > 0.0005f;

        if (!hasMotion && hadInput)
        {
            return;
        }

        if (hasMotion)
        {
            this.orbitYaw += this.orbitVelocityX;
            this.orbitPitch = Math.Clamp(this.orbitPitch + this.orbitVelocityY, OrbitMinPitch, OrbitMaxPitch);
            this.orbitDistance = Math.Clamp(this.orbitDistance + this.zoomVelocity, OrbitMinDistance, OrbitMaxDistance);

            this.orbitVelocityX *= OrbitDamping;
            this.orbitVelocityY *= OrbitDamping;
            this.zoomVelocity *= 0.72f;
        }
        else
        {
            this.orbitYaw += Math.Min((float)gameTime.TotalSeconds, 0.05f) * OrbitAutoRotateSpeed;
        }

        this.ApplyOrbitCamera();
    }

    private void ApplyOrbitCamera()
    {
        if (this.cameraTransform is null)
        {
            return;
        }

        var target = this.orbitTarget;
        var horizontalDistance = MathF.Cos(this.orbitPitch) * this.orbitDistance;
        var position = new Vector3(
            target.X + MathF.Sin(this.orbitYaw) * horizontalDistance,
            target.Y + MathF.Sin(this.orbitPitch) * this.orbitDistance,
            target.Z + MathF.Cos(this.orbitYaw) * horizontalDistance);

        this.cameraTransform.LocalPosition = position;
        this.cameraTransform.LookAt(target);
    }

    private bool AdvanceCinematic(TimeSpan gameTime, out TimeSpan cinematicSnapshot)
    {
        lock (this.stateGate)
        {
            if (!this.cinematicEnabled)
            {
                cinematicSnapshot = TimeSpan.Zero;
                return false;
            }

            this.cinematicTime += gameTime;
            var total = TimeSpan.FromMilliseconds(AutomotiveSceneDefaults.CinematicDurationMilliseconds);
            if (total > TimeSpan.Zero && this.cinematicTime >= total)
            {
                this.cinematicEnabled = false;
                cinematicSnapshot = total;
                return true;
            }

            cinematicSnapshot = this.cinematicTime;
            return true;
        }
    }

    private void UpdateCinematic(TimeSpan cinematicSnapshot)
    {
        var shots = AutomotiveSceneDefaults.CinematicShots;
        if (shots.Count == 0 || this.cameraTransform is null)
        {
            return;
        }

        var total = AutomotiveSceneDefaults.CinematicDurationMilliseconds;
        if (total <= 0)
        {
            return;
        }

        var time = (int)Math.Min(cinematicSnapshot.TotalMilliseconds, total);

        foreach (var shot in shots)
        {
            if (time > shot.DurationMilliseconds)
            {
                time -= shot.DurationMilliseconds;
                continue;
            }

            var amount = Math.Clamp(time / (float)shot.DurationMilliseconds, 0f, 1f);
            var start = ToCinematicPosition(shot.StartPosition);
            var end = ToCinematicPosition(shot.EndPosition);
            var position = Vector3.Lerp(start, end, amount);

            this.cameraTransform!.LocalPosition = position;
            this.cameraTransform.LocalRotation = ToRadiansVector(shot.RotationDegrees);
            return;
        }
    }

    private void ConfigureOrbitCamera()
    {
        this.orbitTarget = ToVector3(AutomotiveSceneDefaults.OrbitCameraTarget);
        this.orbitDistance = Math.Clamp(this.orbitDistance, OrbitMinDistance, OrbitMaxDistance);
        this.orbitPitch = Math.Clamp(this.orbitPitch, OrbitMinPitch, OrbitMaxPitch);
        this.ApplyOrbitCamera();
    }

    private void UpdateReflectionProbe()
    {
        if (this.reflectionProbeGenerator is null)
        {
            return;
        }

        if (this.reflectionProbeTransform is not null)
        {
            this.reflectionProbeTransform.LocalPosition = this.orbitTarget;
        }

        try
        {
            if (this.reflectionProbeGenerator.ReflectionProbe is { } probe)
            {
                this.Managers.EnvironmentManager.IBLReflectionProbe = probe;
            }

            this.reflectionProbeGenerator.RenderNextFrame();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AutomotiveConfigurator] Reflection probe update skipped: {ex.GetBaseException().Message}");
        }
    }

    private void RemoveEntity(Entity? entity)
    {
        if (entity is null)
        {
            return;
        }

        this.Managers.EntityManager.Remove(entity);
    }

    private static IEnumerable<Entity> Enumerate(Entity entity)
    {
        yield return entity;

        foreach (var child in entity.ChildEntities)
        {
            foreach (var nested in Enumerate(child))
            {
                yield return nested;
            }
        }
    }

    private static void AddMaterialTarget(ISet<string> targets, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            targets.Add(value);
        }
    }

    private static string ToAssetPath(string path)
    {
        if (!Path.IsPathRooted(path))
        {
            return path;
        }

        var contentDirectory = Path.Combine(AppContext.BaseDirectory, "Content");
        return Path.GetRelativePath(contentDirectory, path);
    }

    private static Vector3 ToVector3(Vector3Value value)
    {
        return new Vector3((float)value.X, (float)value.Y, (float)value.Z);
    }

    private static Vector3 ToCinematicPosition(Vector3Value value)
    {
        return new Vector3((float)value.X, (float)value.Z, -(float)value.Y);
    }

    private static Vector3 ToRadiansVector(Vector3Value degrees)
    {
        return new Vector3(
            MathHelper.ToRadians((float)degrees.X),
            MathHelper.ToRadians((float)degrees.Y),
            MathHelper.ToRadians((float)degrees.Z));
    }

    private static LinearColor ToLinearColor(AvaloniaColor color)
    {
        return new LinearColor(
            SrgbToLinear(color.R),
            SrgbToLinear(color.G),
            SrgbToLinear(color.B),
            color.A / 255f);
    }

    private static float SrgbToLinear(byte value)
    {
        var channel = value / 255f;
        return channel <= 0.04045f
            ? channel / 12.92f
            : MathF.Pow((channel + 0.055f) / 1.055f, 2.4f);
    }

}
