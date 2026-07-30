using TerraFX.Interop;
using static TerraFX.Interop.Windows;

namespace YoloObjectSearch;

internal readonly record struct D3D11CapabilitySnapshot(
    string AdapterName,
    uint VendorId,
    uint DeviceId,
    string DriverVersion,
    D3D_FEATURE_LEVEL FeatureLevel,
    DXGI_FORMAT TextureFormat,
    uint TextureWidth,
    uint TextureHeight,
    uint TextureArraySize)
{
    public string Format(uint arraySlice, int visibleWidth, int visibleHeight)
    {
        return
            $"adapter=\"{AdapterName}\", vendor=0x{VendorId:X4}, " +
            $"device=0x{DeviceId:X4}, driver={DriverVersion}, " +
            $"feature-level={FormatFeatureLevel(FeatureLevel)}, " +
            $"texture={TextureFormat} {TextureWidth}x{TextureHeight} " +
            $"array={TextureArraySize} slice={arraySlice}, " +
            $"visible={visibleWidth}x{visibleHeight}";
    }

    private static string FormatFeatureLevel(D3D_FEATURE_LEVEL level)
    {
        uint value = (uint)level;
        return $"{value >> 12}.{(value >> 8) & 0xF}";
    }
}

internal static unsafe class D3D11CapabilityDiagnostics
{
    public static D3D11CapabilitySnapshot Inspect(nint texturePointer)
    {
        if (texturePointer == 0)
        {
            throw new ArgumentNullException(nameof(texturePointer));
        }

        ID3D11Texture2D* texture =
            (ID3D11Texture2D*)texturePointer;
        D3D11_TEXTURE2D_DESC textureDescription;
        texture->GetDesc(&textureDescription);

        ID3D11Device* device = null;
        texture->GetDevice(&device);
        if (device is null)
        {
            throw new InvalidOperationException(
                "The VLC texture has no D3D11 device.");
        }

        IDXGIDevice* dxgiDevice = null;
        IDXGIAdapter* adapter = null;
        try
        {
            Guid dxgiDeviceId = IID_IDXGIDevice;
            CheckHResult(
                device->QueryInterface(
                    &dxgiDeviceId,
                    (void**)&dxgiDevice),
                "QueryInterface(IDXGIDevice)");
            CheckHResult(
                dxgiDevice->GetAdapter(&adapter),
                "IDXGIDevice.GetAdapter");

            DXGI_ADAPTER_DESC adapterDescription;
            CheckHResult(
                adapter->GetDesc(&adapterDescription),
                "IDXGIAdapter.GetDesc");

            string adapterName = new(
                (char*)adapterDescription.Description);
            string driverVersion = ReadDriverVersion(adapter);
            return new D3D11CapabilitySnapshot(
                adapterName,
                adapterDescription.VendorId,
                adapterDescription.DeviceId,
                driverVersion,
                device->GetFeatureLevel(),
                textureDescription.Format,
                textureDescription.Width,
                textureDescription.Height,
                textureDescription.ArraySize);
        }
        finally
        {
            if (adapter is not null)
            {
                adapter->Release();
            }
            if (dxgiDevice is not null)
            {
                dxgiDevice->Release();
            }
            device->Release();
        }
    }

    private static string ReadDriverVersion(IDXGIAdapter* adapter)
    {
        LARGE_INTEGER version = default;
        Guid dxgiDeviceId = IID_IDXGIDevice;
        int result = adapter->CheckInterfaceSupport(
            &dxgiDeviceId,
            &version);
        if (result < 0)
        {
            return $"unavailable (HRESULT 0x{result:X8})";
        }

        ulong value = unchecked((ulong)version.QuadPart);
        return
            $"{(value >> 48) & 0xFFFF}." +
            $"{(value >> 32) & 0xFFFF}." +
            $"{(value >> 16) & 0xFFFF}." +
            $"{value & 0xFFFF}";
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
