using System.Runtime.InteropServices;
using TerraFX.Interop;
using static TerraFX.Interop.Windows;

namespace YoloObjectSearch;

internal sealed unsafe class D3D11Nv12Scaler : IDisposable
{
    private ID3D11Device* _device;
    private ID3D11DeviceContext* _deviceContext;
    private ID3D11VideoDevice* _videoDevice;
    private ID3D11VideoContext* _videoContext;
    private ID3D11VideoProcessorEnumerator* _enumerator;
    private ID3D11VideoProcessor* _processor;
    private ID3D11VideoProcessorOutputView* _outputView;
    private ID3D11Texture2D* _outputTexture;

    public D3D11Nv12Scaler(
        ID3D11Device* device,
        uint sourceWidth,
        uint sourceHeight,
        uint outputWidth,
        uint outputHeight)
    {
        if (device is null)
        {
            throw new ArgumentNullException(nameof(device));
        }

        _device = device;
        _device->AddRef();
        ID3D11DeviceContext* deviceContext = null;
        _device->GetImmediateContext(&deviceContext);
        _deviceContext = deviceContext;

        try
        {
            CreateVideoInterfaces();

            D3D11_VIDEO_PROCESSOR_CONTENT_DESC content = new()
            {
                InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT
                    .D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE,
                InputFrameRate = new DXGI_RATIONAL
                {
                    Numerator = 30,
                    Denominator = 1
                },
                InputWidth = sourceWidth,
                InputHeight = sourceHeight,
                OutputFrameRate = new DXGI_RATIONAL
                {
                    Numerator = 30,
                    Denominator = 1
                },
                OutputWidth = outputWidth,
                OutputHeight = outputHeight,
                Usage = D3D11_VIDEO_USAGE
                    .D3D11_VIDEO_USAGE_PLAYBACK_NORMAL
            };
            ID3D11VideoProcessorEnumerator* enumerator = null;
            CheckHResult(
                _videoDevice->CreateVideoProcessorEnumerator(
                    &content,
                    &enumerator),
                "CreateVideoProcessorEnumerator");
            _enumerator = enumerator;

            uint outputSupport;
            CheckHResult(
                _enumerator->CheckVideoProcessorFormat(
                    DXGI_FORMAT.DXGI_FORMAT_NV12,
                    &outputSupport),
                "CheckVideoProcessorFormat(NV12)");
            if ((outputSupport &
                    (uint)D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT
                        .D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_OUTPUT) == 0)
            {
                throw new NotSupportedException(
                    "The D3D11 video processor cannot output NV12.");
            }

            D3D11_TEXTURE2D_DESC outputDescription = new()
            {
                Width = outputWidth,
                Height = outputHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_NV12,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
                BindFlags = (uint)(
                    D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET |
                    D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE)
            };
            ID3D11Texture2D* outputTexture = null;
            CheckHResult(
                _device->CreateTexture2D(
                    &outputDescription,
                    null,
                    &outputTexture),
                "CreateTexture2D(inference NV12)");
            _outputTexture = outputTexture;

            D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC outputViewDescription =
                new()
                {
                    ViewDimension = D3D11_VPOV_DIMENSION
                        .D3D11_VPOV_DIMENSION_TEXTURE2D
                };
            outputViewDescription.Texture2D.MipSlice = 0;
            ID3D11VideoProcessorOutputView* outputView = null;
            CheckHResult(
                _videoDevice->CreateVideoProcessorOutputView(
                    (ID3D11Resource*)_outputTexture,
                    _enumerator,
                    &outputViewDescription,
                    &outputView),
                "CreateVideoProcessorOutputView");
            _outputView = outputView;

            ID3D11VideoProcessor* processor = null;
            CheckHResult(
                _videoDevice->CreateVideoProcessor(
                    _enumerator,
                    0,
                    &processor),
                "CreateVideoProcessor");
            _processor = processor;

            ConfigureRectangles(
                sourceWidth,
                sourceHeight,
                outputWidth,
                outputHeight);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public ID3D11Device* Device => _device;

    public ID3D11Texture2D* OutputTexture => _outputTexture;

    public void Blit(
        ID3D11Texture2D* sourceTexture,
        uint sourceArraySlice)
    {
        ObjectDisposedException.ThrowIf(
            _processor is null,
            this);
        if (sourceTexture is null)
        {
            throw new ArgumentNullException(nameof(sourceTexture));
        }

        D3D11_TEXTURE2D_DESC sourceDescription;
        sourceTexture->GetDesc(&sourceDescription);
        if (sourceArraySlice >= sourceDescription.ArraySize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceArraySlice));
        }

        uint inputSupport;
        CheckHResult(
            _enumerator->CheckVideoProcessorFormat(
                sourceDescription.Format,
                &inputSupport),
            "CheckVideoProcessorFormat(source)");
        if ((inputSupport &
                (uint)D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT
                    .D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_INPUT) == 0)
        {
            throw new NotSupportedException(
                $"The D3D11 video processor cannot read " +
                $"{sourceDescription.Format}.");
        }

        D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC inputDescription = new()
        {
            ViewDimension = D3D11_VPIV_DIMENSION
                .D3D11_VPIV_DIMENSION_TEXTURE2D
        };
        inputDescription.Texture2D.MipSlice = 0;
        inputDescription.Texture2D.ArraySlice = sourceArraySlice;

        ID3D11VideoProcessorInputView* inputView = null;
        CheckHResult(
            _videoDevice->CreateVideoProcessorInputView(
                (ID3D11Resource*)sourceTexture,
                _enumerator,
                &inputDescription,
                &inputView),
            "CreateVideoProcessorInputView");
        try
        {
            D3D11_VIDEO_PROCESSOR_STREAM stream = new()
            {
                Enable = 1,
                pInputSurface = inputView
            };
            CheckHResult(
                _videoContext->VideoProcessorBlt(
                    _processor,
                    _outputView,
                    0,
                    1,
                    &stream),
                "VideoProcessorBlt");
            _deviceContext->Flush();
        }
        finally
        {
            inputView->Release();
        }
    }

    public void Dispose()
    {
        if (_outputView is not null)
        {
            _outputView->Release();
            _outputView = null;
        }
        if (_processor is not null)
        {
            _processor->Release();
            _processor = null;
        }
        if (_enumerator is not null)
        {
            _enumerator->Release();
            _enumerator = null;
        }
        if (_outputTexture is not null)
        {
            _outputTexture->Release();
            _outputTexture = null;
        }
        if (_videoContext is not null)
        {
            _videoContext->Release();
            _videoContext = null;
        }
        if (_videoDevice is not null)
        {
            _videoDevice->Release();
            _videoDevice = null;
        }
        if (_deviceContext is not null)
        {
            _deviceContext->Release();
            _deviceContext = null;
        }
        if (_device is not null)
        {
            _device->Release();
            _device = null;
        }
    }

    private void CreateVideoInterfaces()
    {
        Guid videoDeviceIid = IID_ID3D11VideoDevice;
        ID3D11VideoDevice* videoDevice = null;
        CheckHResult(
            _device->QueryInterface(
                &videoDeviceIid,
                (void**)&videoDevice),
            "QueryInterface(ID3D11VideoDevice)");
        _videoDevice = videoDevice;

        Guid videoContextIid = IID_ID3D11VideoContext;
        ID3D11VideoContext* videoContext = null;
        CheckHResult(
            _deviceContext->QueryInterface(
                &videoContextIid,
                (void**)&videoContext),
            "QueryInterface(ID3D11VideoContext)");
        _videoContext = videoContext;
    }

    private void ConfigureRectangles(
        uint sourceWidth,
        uint sourceHeight,
        uint outputWidth,
        uint outputHeight)
    {
        RECT sourceRectangle = new(
            0,
            0,
            checked((int)sourceWidth),
            checked((int)sourceHeight));
        float scale = MathF.Min(
            (float)outputWidth / sourceWidth,
            (float)outputHeight / sourceHeight);
        int contentWidth = checked((int)MathF.Round(
            sourceWidth * scale));
        int contentHeight = checked((int)MathF.Round(
            sourceHeight * scale));
        int contentX =
            (checked((int)outputWidth) - contentWidth) / 2;
        int contentY =
            (checked((int)outputHeight) - contentHeight) / 2;
        RECT destinationRectangle = new(
            contentX,
            contentY,
            contentX + contentWidth,
            contentY + contentHeight);
        RECT targetRectangle = new(
            0,
            0,
            checked((int)outputWidth),
            checked((int)outputHeight));

        _videoContext->VideoProcessorSetStreamSourceRect(
            _processor,
            0,
            1,
            &sourceRectangle);
        _videoContext->VideoProcessorSetStreamDestRect(
            _processor,
            0,
            1,
            &destinationRectangle);
        _videoContext->VideoProcessorSetOutputTargetRect(
            _processor,
            1,
            &targetRectangle);
    }

    private static void CheckHResult(int result, string operation)
    {
        if (result >= 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{operation} failed: " +
            $"{Marshal.GetExceptionForHR(result)?.Message ?? $"0x{result:X8}"}");
    }
}
