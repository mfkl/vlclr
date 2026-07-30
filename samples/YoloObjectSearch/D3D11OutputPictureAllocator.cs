using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TerraFX.Interop;
using VLCLR.Native;
using VLCLR.Plugin;
using static TerraFX.Interop.Windows;

namespace YoloObjectSearch;

internal sealed unsafe class D3D11OutputPictureAllocator : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct OutputPictureContext
    {
        public VLCPictureContext Picture;
        public VLCD3D11PictureSystem System;
        public nint SurfaceHandle;
    }

    private sealed class PooledSurface
    {
        private readonly D3D11OutputPictureAllocator _owner;
        private int _leases;

        public PooledSurface(
            D3D11OutputPictureAllocator owner,
            ID3D11Texture2D* texture,
            ID3D11ShaderResourceView* yView,
            ID3D11ShaderResourceView* uvView)
        {
            _owner = owner;
            Texture = texture;
            YView = yView;
            UvView = uvView;
        }

        public ID3D11Texture2D* Texture { get; }

        public ID3D11ShaderResourceView* YView { get; }

        public ID3D11ShaderResourceView* UvView { get; }

        public void Acquire()
        {
            Interlocked.Increment(ref _leases);
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _leases) == 0)
            {
                _owner.Return(this);
            }
        }

        public void DisposeNative()
        {
            UvView->Release();
            YView->Release();
            Texture->Release();
        }
    }

    private const int MaximumPooledSurfaces = 6;

    private readonly ConcurrentQueue<PooledSurface> _available = new();
    private readonly VLCFilterContext _context;
    private readonly uint _width;
    private readonly uint _height;
    private readonly object _lifetimeLock = new();

    private ID3D11Device* _device;
    private long _created;
    private long _reused;
    private bool _disposed;

    public D3D11OutputPictureAllocator(
        nint sourceTexture,
        VLCFilterContext context)
    {
        if (sourceTexture == 0)
        {
            throw new ArgumentNullException(nameof(sourceTexture));
        }

        ID3D11Texture2D* texture =
            (ID3D11Texture2D*)sourceTexture;
        D3D11_TEXTURE2D_DESC description;
        texture->GetDesc(&description);
        if (description.Format != DXGI_FORMAT.DXGI_FORMAT_NV12)
        {
            throw new NotSupportedException(
                "Out-of-place overlay pictures require NV12.");
        }

        ID3D11Device* device = null;
        texture->GetDevice(&device);
        if (device is null)
        {
            throw new InvalidOperationException(
                "The VLC texture has no D3D11 device.");
        }
        _device = device;

        _context = context;
        _width = description.Width;
        _height = description.Height;
    }

    public long CreatedSurfaces => Interlocked.Read(ref _created);

    public long ReusedSurfaces => Interlocked.Read(ref _reused);

    public nint RentPicture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PooledSurface surface;
        lock (_lifetimeLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_available.TryDequeue(out PooledSurface? reused))
            {
                surface = reused;
                Interlocked.Increment(ref _reused);
            }
            else
            {
                surface = CreateSurface();
                Interlocked.Increment(ref _created);
            }
            surface.Acquire();
        }

        nint picture = 0;
        OutputPictureContext* pictureContext = null;
        GCHandle surfaceHandle = default;
        try
        {
            VLCVideoFormat format = _context.OutputFormat;
            picture = VLCCore.PictureNewFromFormat(
                (nint)(&format));
            if (picture == 0)
            {
                throw new InvalidOperationException(
                    "VLC could not allocate output picture metadata.");
            }

            pictureContext =
                (OutputPictureContext*)NativeMemory.AllocZeroed(
                    (nuint)sizeof(OutputPictureContext));
            surfaceHandle = GCHandle.Alloc(surface);
            pictureContext->Picture.Destroy =
                (nint)(delegate* unmanaged[Cdecl]<
                    VLCPictureContext*,
                    void>)&DestroyPictureContext;
            pictureContext->Picture.Copy =
                (nint)(delegate* unmanaged[Cdecl]<
                    VLCPictureContext*,
                    VLCPictureContext*>)&CopyPictureContext;
            pictureContext->Picture.VideoContext =
                VLCCore.VideoContextHold(
                    _context.OutputVideoContext);
            if (pictureContext->Picture.VideoContext == 0)
            {
                throw new InvalidOperationException(
                    "VLC did not expose an output video context.");
            }

            PopulateSystem(
                ref pictureContext->System,
                surface);
            pictureContext->SurfaceHandle =
                GCHandle.ToIntPtr(surfaceHandle);
            ((VLCPicture*)picture)->Context =
                (nint)pictureContext;
            return picture;
        }
        catch
        {
            if (pictureContext is not null)
            {
                if (pictureContext->Picture.VideoContext != 0)
                {
                    VLCCore.VideoContextRelease(
                        pictureContext->Picture.VideoContext);
                }
                NativeMemory.Free(pictureContext);
            }
            if (surfaceHandle.IsAllocated)
            {
                surfaceHandle.Free();
            }
            if (picture != 0)
            {
                VLCCore.PictureRelease(picture);
            }
            surface.Release();
            throw;
        }
    }

    public void Dispose()
    {
        lock (_lifetimeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            while (_available.TryDequeue(
                out PooledSurface? surface))
            {
                surface.DisposeNative();
            }
            if (_device is not null)
            {
                _device->Release();
                _device = null;
            }
        }
    }

    private PooledSurface CreateSurface()
    {
        D3D11_TEXTURE2D_DESC description = new()
        {
            Width = _width,
            Height = _height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_NV12,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = (uint)(
                D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET |
                D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE)
        };
        ID3D11Texture2D* texture = null;
        CheckHResult(
            _device->CreateTexture2D(
                &description,
                null,
                &texture),
            "CreateTexture2D(VLC overlay output)");

        ID3D11ShaderResourceView* yView = null;
        ID3D11ShaderResourceView* uvView = null;
        try
        {
            yView = CreateView(
                texture,
                DXGI_FORMAT.DXGI_FORMAT_R8_UNORM);
            uvView = CreateView(
                texture,
                DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM);
            return new PooledSurface(
                this,
                texture,
                yView,
                uvView);
        }
        catch
        {
            if (uvView is not null)
            {
                uvView->Release();
            }
            if (yView is not null)
            {
                yView->Release();
            }
            texture->Release();
            throw;
        }
    }

    private ID3D11ShaderResourceView* CreateView(
        ID3D11Texture2D* texture,
        DXGI_FORMAT format)
    {
        D3D11_SHADER_RESOURCE_VIEW_DESC description = new()
        {
            Format = format,
            ViewDimension = D3D_SRV_DIMENSION
                .D3D11_SRV_DIMENSION_TEXTURE2D
        };
        description.Texture2D.MostDetailedMip = 0;
        description.Texture2D.MipLevels = 1;

        ID3D11ShaderResourceView* view = null;
        CheckHResult(
            _device->CreateShaderResourceView(
                (ID3D11Resource*)texture,
                &description,
                &view),
            $"CreateShaderResourceView({format})");
        return view;
    }

    private void Return(PooledSurface surface)
    {
        lock (_lifetimeLock)
        {
            if (_disposed ||
                _available.Count >= MaximumPooledSurfaces)
            {
                surface.DisposeNative();
                return;
            }

            _available.Enqueue(surface);
        }
    }

    private static void PopulateSystem(
        ref VLCD3D11PictureSystem system,
        PooledSurface surface)
    {
        system.Texture0 = (nint)surface.Texture;
        system.Texture1 = (nint)surface.Texture;
        system.ArraySlice = 0;
        system.ShaderResourceView0 = (nint)surface.YView;
        system.ShaderResourceView1 = (nint)surface.UvView;
        system.SharedHandle = -1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DestroyPictureContext(
        VLCPictureContext* context)
    {
        if (context is null)
        {
            return;
        }

        OutputPictureContext* outputContext =
            (OutputPictureContext*)context;
        try
        {
            GCHandle surfaceHandle = GCHandle.FromIntPtr(
                outputContext->SurfaceHandle);
            if (surfaceHandle.Target is PooledSurface surface)
            {
                surfaceHandle.Free();
                surface.Release();
            }
            else if (surfaceHandle.IsAllocated)
            {
                surfaceHandle.Free();
            }
        }
        catch
        {
            // Never allow a managed exception through VLC's C callback.
        }
        finally
        {
            NativeMemory.Free(outputContext);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static VLCPictureContext* CopyPictureContext(
        VLCPictureContext* context)
    {
        if (context is null)
        {
            return null;
        }

        try
        {
            OutputPictureContext* source =
                (OutputPictureContext*)context;
            GCHandle sourceHandle = GCHandle.FromIntPtr(
                source->SurfaceHandle);
            if (sourceHandle.Target is not PooledSurface surface)
            {
                return null;
            }

            OutputPictureContext* copy =
                (OutputPictureContext*)NativeMemory.AllocZeroed(
                    (nuint)sizeof(OutputPictureContext));
            surface.Acquire();
            GCHandle copyHandle = default;
            try
            {
                copyHandle = GCHandle.Alloc(surface);
                copy->Picture.Destroy = source->Picture.Destroy;
                copy->Picture.Copy = source->Picture.Copy;
                copy->Picture.VideoContext =
                    VLCCore.VideoContextHold(
                        source->Picture.VideoContext);
                copy->System = source->System;
                copy->SurfaceHandle =
                    GCHandle.ToIntPtr(copyHandle);
                return &copy->Picture;
            }
            catch
            {
                if (copyHandle.IsAllocated)
                {
                    copyHandle.Free();
                }
                surface.Release();
                NativeMemory.Free(copy);
                throw;
            }
        }
        catch
        {
            return null;
        }
    }

    private static void CheckHResult(int result, string operation)
    {
        if (result < 0)
        {
            throw new InvalidOperationException(
                $"{operation} failed with HRESULT 0x{result:X8}.");
        }
    }
}
