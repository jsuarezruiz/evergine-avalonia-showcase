using System.Collections.Generic;
using System.Linq;

namespace AutomotiveConfigurator.AvaloniaEvergine.Rendering;

public static class AutomotiveSceneDefaults
{
    public const string BodyMaterial = "Mt_Body";
    public const string MirrorCoverMaterial = "Mt_MirrorCover";
    public const string AlloyWheelsMaterial = "Mt_AlloyWheels";
    public const string BrakeCaliperMaterial = "Mt_BrakeCaliper";
    public const string ShadowPlaneMaterial = "Mt_Shadow_Plane";
    public const string EnvironmentMaterial = "Mt_Environment";
    public const string RimObjectPrefix = "Obj_Rim";

    public static readonly Vector3Value OrbitCameraPosition = new(-27, 5, 10);
    public static readonly Vector3Value OrbitCameraTarget = new(0, 3, 0);

    public static IReadOnlyList<CinematicShot> CinematicShots { get; } = new[]
    {
        new CinematicShot(new(-28, -26, 3.5), new(-25, -23, 3.5), new(0, -45, 5), 9500),
        new CinematicShot(new(-18, 0, 2.5), new(-18, 0, 5.5), new(0, -90, 0), 5000),
        new CinematicShot(new(-13.5, -3.75, 3.75), new(-12, -5.5, 4.5), new(-41.79, -42.36, -19.55), 7000),
        new CinematicShot(new(-10.5, -8, 1.5), new(-14, -12, 1), new(10.12, -43.88, -7.06), 7000),
        new CinematicShot(new(-13, -14, 14), new(11, -14, 14), new(-38.28, 0, 0), 12000),
        new CinematicShot(new(12.85, -1, 4.35), new(12.85, 0.7, 4.35), new(47.34, 50.53, -33.9), 7000),
        new CinematicShot(new(13, -4.5, 2.5), new(13, -4.5, 5), new(0, 58, 5.35), 7000),
        new CinematicShot(new(-3.3, -6.5, 5), new(1.2, -6.5, 5.35), new(-30.65, -55.53, -1.88), 5000),
        new CinematicShot(new(-13.85, -0.35, 3.15), new(-14.5, -1.1, 3.75), new(-35.54, -35.16, -15.17), 8000),
    };

    public static int CinematicDurationMilliseconds { get; } =
        CinematicShots.Sum(shot => shot.DurationMilliseconds);
}

public sealed record CinematicShot(
    Vector3Value StartPosition,
    Vector3Value EndPosition,
    Vector3Value RotationDegrees,
    int DurationMilliseconds);

public readonly record struct Vector3Value(double X, double Y, double Z);
