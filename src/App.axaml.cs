using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using AutomotiveConfigurator.AvaloniaEvergine.Rendering;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Evergine.Avalonia;
using Evergine.Common.Graphics;
using Evergine.Common.IO;
using EvergineDefaultResourcesIDs = Evergine.Framework.DefaultResourcesIDs;

namespace AutomotiveConfigurator.AvaloniaEvergine;

public partial class App : Application
{
    public AutomotiveEvergineApplication? EvergineApplication { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            EvergineApplication = new AutomotiveEvergineApplication();

            var windowsSystem = new AvaloniaWindowsSystem();
            EvergineApplication.Container.RegisterInstance(windowsSystem);

            var graphicsContext = CreateGraphicsContext();
            graphicsContext.CreateDevice();
            EvergineApplication.Container.RegisterInstance(graphicsContext);

            var contentDirectory = Path.Combine(AppContext.BaseDirectory, "Content");
            ValidateEvergineRuntimeContent(contentDirectory);
            EvergineApplication.Container.RegisterInstance(new AssetsDirectory(contentDirectory));
            CreateAndRegisterAudioDevice();

            desktop.MainWindow = new MainWindow();
            desktop.Exit += (_, _) => DisposeEvergineApplication();
            EvergineApplication.Initialize();

            var clockTimer = Stopwatch.StartNew();
            var frameTime = TimeSpan.Zero;
            windowsSystem.Run(
                () =>
                {
                    frameTime = clockTimer.Elapsed;
                    clockTimer.Restart();
                    EvergineApplication.UpdateFrame(frameTime);
                },
                () =>
                {
                    if (frameTime == TimeSpan.Zero)
                    {
                        return;
                    }

                    if (desktop.MainWindow is MainWindow { HasReadyRenderSurface: true })
                    {
                        EvergineApplication.DrawFrame(frameTime);
                    }
                });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static GraphicsContext CreateGraphicsContext()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new Evergine.DirectX11.DX11GraphicsContext();
        }

        throw new PlatformNotSupportedException("The Evergine Avalonia sample build is configured for Windows/DirectX11.");
    }

    private static void ValidateEvergineRuntimeContent(string contentDirectory)
    {
        var requiredFiles = new[]
        {
            $"{EvergineDefaultResourcesIDs.StandardEffectID:D}.wepfx",
            $"{EvergineDefaultResourcesIDs.RenderQuadID:D}.wepfx",
            $"{EvergineDefaultResourcesIDs.OpaqueRenderLayerID:D}.weprl",
            $"{EvergineDefaultResourcesIDs.AlphaRenderLayerID:D}.weprl",
            $"{EvergineDefaultResourcesIDs.AlphaDoubleSidedRenderLayerID:D}.weprl",
            $"{EvergineDefaultResourcesIDs.LinearClampSamplerID:D}.wepsp",
            $"{EvergineDefaultResourcesIDs.LinearWrapSamplerID:D}.wepsp",
        };

        var missingFiles = requiredFiles
            .Select(file => Path.Combine(contentDirectory, file))
            .Where(path => !File.Exists(path))
            .ToArray();

        if (missingFiles.Length > 0)
        {
            throw new InvalidOperationException(
                "Evergine core content was not exported to the application output directory. " +
                "Clean and rebuild the Windows target, then verify the bin output contains the Content folder. " +
                "Missing files: " + string.Join(", ", missingFiles));
        }
    }

    private void CreateAndRegisterAudioDevice()
    {
        if (EvergineApplication == null)
        {
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            EvergineApplication.Container.RegisterInstance(new Evergine.XAudio2.XAudioDevice());
        }
    }

    private void DisposeEvergineApplication()
    {
        EvergineApplication?.Dispose();
        EvergineApplication = null;
    }
}
