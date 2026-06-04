using AutomotiveConfigurator.AvaloniaEvergine.Models;
using Avalonia.Media;

namespace AutomotiveConfigurator.AvaloniaEvergine.Rendering;

public interface IAutomotiveSceneBridge
{
    bool IsSceneReady { get; }

    bool HasSceneError { get; }

    string SceneStatus { get; }

    void InitializeScene(AutomotiveAssetSet assets, ConfiguratorMeta meta);

    void StartCinematic();

    void StopCinematic();

    void SetMaterialColor(string materialName, Color color);

    void ShowWheelDesign(string objectName);

    void OrbitCamera(float deltaX, float deltaY);

    void ZoomCamera(float delta);
}
