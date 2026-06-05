# Evergine Avalonia Showcase

An automotive configurator sample that embeds an Evergine 3D scene inside an Avalonia desktop application.

The sample recreates the interaction model from [`rendercodeninja/automotive-configurator`](https://github.com/rendercodeninja/automotive-configurator): a cinematic intro, an orbitable Lamborghini Aventador scene, material customization, wheel selection, and an Avalonia overlay UI driving the 3D renderer.

![Evergine Avalonia automotive configurator](images/evergine-avalonia-showcase.gif)

## Official Context

Evergine announced Avalonia support in the May 2026 post [Evergine + Avalonia](https://evergine.com/es/soporte-avalonia-en-evergine/). The integration is aimed at applications where a modern .NET UI and real-time 3D renderer need to live in the same product: product configurators, digital twins, industrial HMIs, scene editors, dashboards, and simulation tools.

That is the architecture used here:

- Avalonia owns the application shell, layout, controls, styling, and interaction flow.
- Evergine owns the render surface, 3D scene, cameras, lighting, materials, model loading, and real-time visual updates.
- UI commands flow through a small scene bridge, keeping Avalonia interaction code separate from Evergine scene code.

The blog also mentions the official Evergine Launcher template and the updated `UIWindowSystemsDemo` sample as the starting point for new Avalonia + Evergine projects. This repository builds on that direction with a more complete product-style configurator experience.

## Requirements

- .NET SDK capable of building `net10.0`.
- Access to the [Evergine nightly NuGet feed](https://pkgs.dev.azure.com/plainconcepts/Evergine.Nightly/_packaging/Evergine.NightlyBuilds/nuget/v3/index.json) used in `NuGet.config`.
- DirectX 11 capable Windows environment.

The required Evergine runtime content is checked into `src/Content` and copied
to the output during build, so a fresh clone does not depend on a local
`%USERPROFILE%\.evergine\packages` cache created by Evergine Launcher.

## Run

Run the showcase on Windows:

```bash
cd src
dotnet restore AutomotiveConfigurator.AvaloniaEvergine.csproj
dotnet run --project AutomotiveConfigurator.AvaloniaEvergine.csproj
```

## Build

```bash
cd src
dotnet build AutomotiveConfigurator.AvaloniaEvergine.csproj
```

The project targets `net10.0-windows10.0.19041.0` and always builds the Evergine-backed renderer path.

## How It Works

`EvergineRenderHost` creates the native render surface and hosts `AutomotiveEvergineApplication`, which initializes the Evergine scene, loads the GLB assets, configures lighting/materials, and exposes scene operations to Avalonia.

## Project Layout

```text
.
├── images/                       # README media
└── src/
    ├── Assets/
    │   ├── AutomotiveConfigurator/   # GLB models, metadata, environment, thumbnails
    │   └── Fonts/                    # Bundled Lato fonts used by the UI
    ├── Content/                      # Evergine runtime content copied to output
    ├── Controls/                     # Evergine host control and native platform plumbing
    ├── Models/                       # Metadata models for the configurator JSON
    ├── Rendering/                    # Evergine application, scene, bridge, and defaults
    ├── MainWindow.axaml              # Avalonia layout
    ├── MainWindow.axaml.cs           # Configurator UI behavior
    └── AutomotiveConfigurator.AvaloniaEvergine.csproj
```

## Integration Notes

- This project follows the architecture described in the official [Evergine + Avalonia announcement](https://evergine.com/es/soporte-avalonia-en-evergine/): Avalonia acts as the main application container while Evergine is embedded as a dedicated 3D render area.
- `AutomotiveConfigurator.AvaloniaEvergine.weproj` is the minimal Evergine project file used by the build.
- Checked-in Evergine runtime content from `src/Content` is copied into the output `Content` folder before compilation, and the build validates the required effect/render-layer/sampler files are present.

## Credits

This sample is based on the interaction and assets from [`rendercodeninja/automotive-configurator`](https://github.com/rendercodeninja/automotive-configurator).
