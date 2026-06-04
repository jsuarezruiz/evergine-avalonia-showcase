using Avalonia.Platform;
using System;

namespace AutomotiveConfigurator.AvaloniaEvergine.Controls.Platform
{
    internal interface INativePlatformBackend : IDisposable
    {
        IntPtr NativeHandle { get; }

        IPlatformHandle CreateView(
            IPlatformHandle parent,
            int width,
            int height,
            NativeInputCallbacks callbacks);

        void DestroyView();

        void Resize(int width, int height);

        Evergine.Common.Graphics.SurfaceInfo.SurfaceTypes SurfaceType { get; }
    }
}