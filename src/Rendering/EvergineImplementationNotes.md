# Evergine Implementation Notes

The Avalonia UI sends all 3D commands through `IAutomotiveSceneBridge`.
`MainWindow` hosts `Controls/EvergineRenderHost`, adapted from Evergine's Avalonia
sample. It creates a Win32 child window, an
`AvaloniaSurface`, a swap chain, and registers a display with `GraphicsPresenter`.

Implemented scene behavior:

1. Load `Assets/AutomotiveConfigurator/environment/model.glb`.
2. Load `Assets/AutomotiveConfigurator/stage/model.glb`.
3. Load `Assets/AutomotiveConfigurator/aventador/model.glb`.
4. Convert the environment dome material to Evergine's `SkyboxEffect` and
   `SkyboxRenderLayerID`. The source texture is generated from the original
   `royal_esplanade_1k.exr` and embedded in the GLB so it follows the same
   runtime loader path as the car and stage.
5. Apply startup colors from `meta.json`:
   - `Mt_Body`
   - `Mt_MirrorCover`
   - `Mt_AlloyWheels`
   - `Mt_BrakeCaliper`
6. Keep the imported `Mt_Shadow_Plane` texture, but move it to an alpha
   double-sided render layer with lighting disabled so it behaves like the
   original Three.js `MeshBasicMaterial` override.
7. Hide every `Obj_Rim*` node except `Obj_Rim_T0A`.
8. On `SetMaterialColor`, find material components whose assigned source
   material equals the target and update the standard material base color using
   the original demo's sRGB-to-linear color convention.
9. On `ShowWheelDesign`, show matching rim objects and hide the rest.
10. Implement the original camera values from `cameraController.js`:
   - Orbit camera position: `(-27, 5, 10)`
   - Orbit target: `(0, 3, 0)`
   - FOV: `45`
   - Distance range: `16..32`
   - Polar range: `0.75..1.6` translated to the equivalent Evergine pitch range
   - The cinematic sequence is the `CINE_SEQUENCE_POINTS` array in the original.
11. Attach Evergine's exported default post-processing graph to the scene camera
   for the built-in tone mapping/FXAA/fog path instead of hand-writing effects.
12. Update the on-demand reflection probe after color and wheel changes so
    material changes are reflected by the local image-based lighting.

The Evergine runtime content in `Content/` is intentionally checked in and
copied to the build output so a fresh clone does not rely on a local Evergine
Launcher cache. The renderer remains Windows/DirectX11 because that is the
official sample path the user wants to test on Windows.
