using System.Runtime.InteropServices;
using TerraFX.Interop;
using VLCLR.Native;
using VLCLR.ObjectDetection;
using static TerraFX.Interop.Windows;

namespace YoloObjectSearch;

internal sealed unsafe class D3D11DetectionOverlay : IDisposable
{
    private const int OverlayWidth = 416;
    private const int OverlayHeight = 416;
    private const int BytesPerPixel = 4;
    private const int BorderThickness = 2;
    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;
    private const int GlyphAdvance = 6;
    private const int LabelPadding = 1;
    private const int LabelHeight = GlyphHeight + LabelPadding * 2;

    private readonly byte[] _overlayPixels =
        new byte[OverlayWidth * OverlayHeight * BytesPerPixel];
    private readonly uint _textureWidth;
    private readonly uint _textureHeight;
    private readonly DXGI_FORMAT _sourceFormat;

    private ID3D11Device* _device;
    private ID3D11DeviceContext* _deviceContext;
    private ID3D11VideoDevice* _videoDevice;
    private ID3D11VideoContext* _videoContext;
    private ID3D11VideoProcessorEnumerator* _enumerator;
    private ID3D11VideoProcessor* _processor;
    private ID3D11Texture2D* _overlayTexture;
    private ID3D11VideoProcessorInputView* _overlayInputView;
    private long _uploadedGeneration = -1;
    private long _renderedFrames;
    private long _uploadedBatches;
    private long _uploadedBoxes;
    private bool _disposed;

    public long RenderedFrames => _renderedFrames;

    public long UploadedBatches => _uploadedBatches;

    public long UploadedBoxes => _uploadedBoxes;

    public D3D11DetectionOverlay(
        nint sourceTexture,
        int visibleWidth,
        int visibleHeight)
    {
        if (sourceTexture == 0)
        {
            throw new ArgumentNullException(nameof(sourceTexture));
        }
        if (visibleWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(visibleWidth));
        }
        if (visibleHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(visibleHeight));
        }

        ID3D11Texture2D* texture = (ID3D11Texture2D*)sourceTexture;
        D3D11_TEXTURE2D_DESC sourceDescription;
        texture->GetDesc(&sourceDescription);
        if (sourceDescription.Format != DXGI_FORMAT.DXGI_FORMAT_NV12)
        {
            throw new NotSupportedException(
                $"GPU overlay currently requires NV12; received " +
                $"{sourceDescription.Format}.");
        }

        _textureWidth = sourceDescription.Width;
        _textureHeight = sourceDescription.Height;
        _sourceFormat = sourceDescription.Format;

        ID3D11Device* device = null;
        texture->GetDevice(&device);
        if (device is null)
        {
            throw new InvalidOperationException(
                "The VLC texture has no D3D11 device.");
        }

        _device = device;
        ID3D11DeviceContext* deviceContext = null;
        _device->GetImmediateContext(&deviceContext);
        _deviceContext = deviceContext;

        try
        {
            CreateVideoInterfaces();
            CreateProcessor();
            CreateOverlayTexture();
            ConfigureStreams(visibleWidth, visibleHeight);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Render(
        VLCD3D11Surface sourceSurface,
        VLCD3D11Surface outputSurface,
        long generation,
        int sourceWidth,
        int sourceHeight,
        ReadOnlySpan<ObjectDetection> detections)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (sourceSurface.Texture == 0)
        {
            throw new ArgumentException(
                "The source D3D11 surface has no texture.",
                nameof(sourceSurface));
        }
        if (outputSurface.Texture == 0)
        {
            throw new ArgumentException(
                "The output D3D11 surface has no texture.",
                nameof(outputSurface));
        }
        if (sourceSurface.Texture == outputSurface.Texture &&
            sourceSurface.ArraySlice == outputSurface.ArraySlice)
        {
            throw new InvalidOperationException(
                "VLC returned the decoder surface as the filter output. " +
                "Out-of-place composition is required to avoid corrupting " +
                "hardware-decoder reference frames.");
        }
        if (detections.IsEmpty)
        {
            return;
        }

        ID3D11Texture2D* sourceTexture =
            (ID3D11Texture2D*)sourceSurface.Texture;
        D3D11_TEXTURE2D_DESC sourceDescription;
        sourceTexture->GetDesc(&sourceDescription);
        if (sourceDescription.Format != _sourceFormat ||
            sourceDescription.Width != _textureWidth ||
            sourceDescription.Height != _textureHeight)
        {
            throw new InvalidOperationException(
                "The VLC D3D11 surface format or size changed.");
        }
        if (sourceSurface.ArraySlice >= sourceDescription.ArraySize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceSurface),
                sourceSurface.ArraySlice,
                "The VLC D3D11 array slice is outside the texture.");
        }

        ID3D11Texture2D* outputTexture =
            (ID3D11Texture2D*)outputSurface.Texture;
        D3D11_TEXTURE2D_DESC outputTextureDescription;
        outputTexture->GetDesc(&outputTextureDescription);
        if (outputTextureDescription.Format != _sourceFormat ||
            outputTextureDescription.Width != _textureWidth ||
            outputTextureDescription.Height != _textureHeight)
        {
            throw new InvalidOperationException(
                "The VLC output surface does not match the input texture's " +
                "NV12 format and dimensions.");
        }
        if (outputSurface.ArraySlice >=
            outputTextureDescription.ArraySize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputSurface),
                outputSurface.ArraySlice,
                "The VLC D3D11 output array slice is outside the texture.");
        }

        if (generation != _uploadedGeneration)
        {
            UploadOverlay(
                generation,
                sourceWidth,
                sourceHeight,
                detections);
        }

        ID3D11VideoProcessorInputView* sourceInputView = null;
        D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC inputDescription = new()
        {
            ViewDimension = D3D11_VPIV_DIMENSION
                .D3D11_VPIV_DIMENSION_TEXTURE2D
        };
        inputDescription.Texture2D.MipSlice = 0;
        inputDescription.Texture2D.ArraySlice =
            sourceSurface.ArraySlice;
        CheckHResult(
            _videoDevice->CreateVideoProcessorInputView(
                (ID3D11Resource*)sourceTexture,
                _enumerator,
                &inputDescription,
                &sourceInputView),
            "CreateVideoProcessorInputView(VLC source)");

        ID3D11VideoProcessorOutputView* outputView = null;
        D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC outputDescription =
            new();
        if (outputTextureDescription.ArraySize == 1)
        {
            outputDescription.ViewDimension =
                D3D11_VPOV_DIMENSION
                    .D3D11_VPOV_DIMENSION_TEXTURE2D;
            outputDescription.Texture2D.MipSlice = 0;
        }
        else
        {
            outputDescription.ViewDimension =
                D3D11_VPOV_DIMENSION
                    .D3D11_VPOV_DIMENSION_TEXTURE2DARRAY;
            outputDescription.Texture2DArray.MipSlice = 0;
            outputDescription.Texture2DArray.FirstArraySlice =
                outputSurface.ArraySlice;
            outputDescription.Texture2DArray.ArraySize = 1;
        }
        try
        {
            CheckHResult(
                _videoDevice->CreateVideoProcessorOutputView(
                    (ID3D11Resource*)outputTexture,
                    _enumerator,
                    &outputDescription,
                    &outputView),
                "CreateVideoProcessorOutputView(VLC output)");

            D3D11_VIDEO_PROCESSOR_STREAM* streams =
                stackalloc D3D11_VIDEO_PROCESSOR_STREAM[2];
            streams[0] = new D3D11_VIDEO_PROCESSOR_STREAM
            {
                Enable = 1,
                pInputSurface = sourceInputView
            };
            streams[1] = new D3D11_VIDEO_PROCESSOR_STREAM
            {
                Enable = 1,
                pInputSurface = _overlayInputView
            };

            CheckHResult(
                _videoContext->VideoProcessorBlt(
                    _processor,
                    outputView,
                    0,
                    2,
                    streams),
                "VideoProcessorBlt(detection overlay)");
            _renderedFrames++;
        }
        finally
        {
            if (outputView is not null)
            {
                outputView->Release();
            }
            sourceInputView->Release();
        }
    }

    public void Reset()
    {
        _uploadedGeneration = -1;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_overlayInputView is not null)
        {
            _overlayInputView->Release();
            _overlayInputView = null;
        }
        if (_overlayTexture is not null)
        {
            _overlayTexture->Release();
            _overlayTexture = null;
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

        _disposed = true;
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

    private void CreateProcessor()
    {
        D3D11_VIDEO_PROCESSOR_CONTENT_DESC content = new()
        {
            InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT
                .D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE,
            InputFrameRate = new DXGI_RATIONAL
            {
                Numerator = 30,
                Denominator = 1
            },
            InputWidth = _textureWidth,
            InputHeight = _textureHeight,
            OutputFrameRate = new DXGI_RATIONAL
            {
                Numerator = 30,
                Denominator = 1
            },
            OutputWidth = _textureWidth,
            OutputHeight = _textureHeight,
            Usage = D3D11_VIDEO_USAGE
                .D3D11_VIDEO_USAGE_PLAYBACK_NORMAL
        };
        ID3D11VideoProcessorEnumerator* enumerator = null;
        CheckHResult(
            _videoDevice->CreateVideoProcessorEnumerator(
                &content,
                &enumerator),
            "CreateVideoProcessorEnumerator(overlay)");
        _enumerator = enumerator;

        ValidateFormat(
            _sourceFormat,
            D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT
                .D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_INPUT |
            D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT
                .D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_OUTPUT);
        ValidateFormat(
            DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT
                .D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_INPUT);

        D3D11_VIDEO_PROCESSOR_CAPS capabilities;
        CheckHResult(
            _enumerator->GetVideoProcessorCaps(&capabilities),
            "GetVideoProcessorCaps(overlay)");
        if (capabilities.MaxInputStreams < 2)
        {
            throw new NotSupportedException(
                "The D3D11 video processor exposes fewer than two streams.");
        }
        uint alphaStream = (uint)D3D11_VIDEO_PROCESSOR_FEATURE_CAPS
            .D3D11_VIDEO_PROCESSOR_FEATURE_CAPS_ALPHA_STREAM;
        if ((capabilities.FeatureCaps & alphaStream) == 0)
        {
            throw new NotSupportedException(
                "The D3D11 video processor does not support alpha streams.");
        }

        ID3D11VideoProcessor* processor = null;
        CheckHResult(
            _videoDevice->CreateVideoProcessor(
                _enumerator,
                0,
                &processor),
            "CreateVideoProcessor(overlay)");
        _processor = processor;
    }

    private void CreateOverlayTexture()
    {
        D3D11_TEXTURE2D_DESC description = new()
        {
            Width = OverlayWidth,
            Height = OverlayHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = (uint)(
                D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET |
                D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE)
        };
        ID3D11Texture2D* overlayTexture = null;
        CheckHResult(
            _device->CreateTexture2D(
                &description,
                null,
                &overlayTexture),
            "CreateTexture2D(BGRA overlay)");
        _overlayTexture = overlayTexture;

        D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC inputDescription = new()
        {
            ViewDimension = D3D11_VPIV_DIMENSION
                .D3D11_VPIV_DIMENSION_TEXTURE2D
        };
        inputDescription.Texture2D.MipSlice = 0;
        inputDescription.Texture2D.ArraySlice = 0;
        ID3D11VideoProcessorInputView* overlayInputView = null;
        CheckHResult(
            _videoDevice->CreateVideoProcessorInputView(
                (ID3D11Resource*)_overlayTexture,
                _enumerator,
                &inputDescription,
                &overlayInputView),
            "CreateVideoProcessorInputView(BGRA overlay)");
        _overlayInputView = overlayInputView;
    }

    private void ConfigureStreams(int visibleWidth, int visibleHeight)
    {
        RECT videoRectangle = new(
            0,
            0,
            checked((int)_textureWidth),
            checked((int)_textureHeight));
        RECT overlaySourceRectangle = new(
            0,
            0,
            OverlayWidth,
            OverlayHeight);
        RECT overlayDestinationRectangle = new(
            0,
            0,
            Math.Min(visibleWidth, checked((int)_textureWidth)),
            Math.Min(visibleHeight, checked((int)_textureHeight)));

        _videoContext->VideoProcessorSetOutputTargetRect(
            _processor,
            1,
            &videoRectangle);
        _videoContext->VideoProcessorSetStreamFrameFormat(
            _processor,
            0,
            D3D11_VIDEO_FRAME_FORMAT
                .D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
        _videoContext->VideoProcessorSetStreamSourceRect(
            _processor,
            0,
            1,
            &videoRectangle);
        _videoContext->VideoProcessorSetStreamDestRect(
            _processor,
            0,
            1,
            &videoRectangle);
        _videoContext->VideoProcessorSetStreamAutoProcessingMode(
            _processor,
            0,
            0);

        _videoContext->VideoProcessorSetStreamFrameFormat(
            _processor,
            1,
            D3D11_VIDEO_FRAME_FORMAT
                .D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
        _videoContext->VideoProcessorSetStreamSourceRect(
            _processor,
            1,
            1,
            &overlaySourceRectangle);
        _videoContext->VideoProcessorSetStreamDestRect(
            _processor,
            1,
            1,
            &overlayDestinationRectangle);
        _videoContext->VideoProcessorSetStreamAlpha(
            _processor,
            1,
            1,
            1.0f);
        _videoContext->VideoProcessorSetStreamAutoProcessingMode(
            _processor,
            1,
            0);
    }

    private void ValidateFormat(
        DXGI_FORMAT format,
        D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT required)
    {
        uint support;
        CheckHResult(
            _enumerator->CheckVideoProcessorFormat(format, &support),
            $"CheckVideoProcessorFormat({format})");
        if ((support & (uint)required) != (uint)required)
        {
            throw new NotSupportedException(
                $"The D3D11 video processor does not provide the required " +
                $"support for {format}.");
        }
    }

    private void UploadOverlay(
        long generation,
        int sourceWidth,
        int sourceHeight,
        ReadOnlySpan<ObjectDetection> detections)
    {
        if (sourceWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        }
        if (sourceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceHeight));
        }

        Array.Clear(_overlayPixels);
        foreach (ObjectDetection detection in detections)
        {
            DrawBox(detection, sourceWidth, sourceHeight);
        }

        fixed (byte* pixels = _overlayPixels)
        {
            _deviceContext->UpdateSubresource(
                (ID3D11Resource*)_overlayTexture,
                0,
                null,
                pixels,
                OverlayWidth * BytesPerPixel,
                0);
        }
        _uploadedGeneration = generation;
        _uploadedBatches++;
        _uploadedBoxes += detections.Length;
    }

    private void DrawBox(
        ObjectDetection detection,
        int sourceWidth,
        int sourceHeight)
    {
        float scaleX = (float)OverlayWidth / sourceWidth;
        float scaleY = (float)OverlayHeight / sourceHeight;
        int left = Math.Clamp(
            (int)MathF.Round(detection.Box.X * scaleX),
            0,
            OverlayWidth - 1);
        int top = Math.Clamp(
            (int)MathF.Round(detection.Box.Y * scaleY),
            0,
            OverlayHeight - 1);
        int right = Math.Clamp(
            (int)MathF.Round(detection.Box.Right * scaleX),
            left,
            OverlayWidth - 1);
        int bottom = Math.Clamp(
            (int)MathF.Round(detection.Box.Bottom * scaleY),
            top,
            OverlayHeight - 1);

        (byte blue, byte green, byte red) =
            GetColor(detection.ClassId);
        DrawHorizontal(
            left,
            right,
            top,
            BorderThickness,
            blue,
            green,
            red);
        DrawHorizontal(
            left,
            right,
            Math.Max(top, bottom - BorderThickness + 1),
            BorderThickness,
            blue,
            green,
            red);
        DrawVertical(
            top,
            bottom,
            left,
            BorderThickness,
            blue,
            green,
            red);
        DrawVertical(
            top,
            bottom,
            Math.Max(left, right - BorderThickness + 1),
            BorderThickness,
            blue,
            green,
            red);
        DrawLabel(detection, left, top);
    }

    private void DrawLabel(
        ObjectDetection detection,
        int left,
        int top)
    {
        Span<char> text = stackalloc char[64];
        int length = Math.Min(
            detection.Label.Length,
            text.Length - 6);
        detection.Label.AsSpan(0, length).CopyTo(text);
        text[length++] = ' ';

        int confidence = Math.Clamp(
            (int)MathF.Round(detection.Confidence * 100),
            0,
            100);
        if (!confidence.TryFormat(
                text[length..],
                out int confidenceLength))
        {
            return;
        }
        length += confidenceLength;
        text[length++] = '%';

        int labelTop = top >= LabelHeight
            ? top - LabelHeight
            : top;
        int labelWidth = Math.Min(
            OverlayWidth - left,
            length * GlyphAdvance - 1 + LabelPadding * 2);
        FillRectangle(
            left,
            labelTop,
            labelWidth,
            LabelHeight,
            0,
            0,
            0,
            190);

        int x = left + LabelPadding;
        int y = labelTop + LabelPadding;
        for (int index = 0; index < length; index++)
        {
            if (x + GlyphWidth > OverlayWidth)
            {
                break;
            }
            DrawGlyph(text[index], x, y);
            x += GlyphAdvance;
        }
    }

    private void DrawGlyph(char character, int left, int top)
    {
        ulong glyph = GetGlyph(character);
        if (glyph == 0)
        {
            return;
        }

        for (int y = 0; y < GlyphHeight; y++)
        {
            for (int x = 0; x < GlyphWidth; x++)
            {
                int bit = y * GlyphWidth + x;
                if ((glyph & (1UL << bit)) != 0)
                {
                    SetPixel(
                        left + x,
                        top + y,
                        255,
                        255,
                        255,
                        255);
                }
            }
        }
    }

    private void FillRectangle(
        int left,
        int top,
        int width,
        int height,
        byte blue,
        byte green,
        byte red,
        byte alpha)
    {
        int right = Math.Min(OverlayWidth, left + width);
        int bottom = Math.Min(OverlayHeight, top + height);
        for (int y = Math.Max(0, top); y < bottom; y++)
        {
            for (int x = Math.Max(0, left); x < right; x++)
            {
                SetPixel(x, y, blue, green, red, alpha);
            }
        }
    }

    private void DrawHorizontal(
        int left,
        int right,
        int top,
        int thickness,
        byte blue,
        byte green,
        byte red)
    {
        int bottom = Math.Min(OverlayHeight, top + thickness);
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                SetPixel(x, y, blue, green, red);
            }
        }
    }

    private void DrawVertical(
        int top,
        int bottom,
        int left,
        int thickness,
        byte blue,
        byte green,
        byte red)
    {
        int right = Math.Min(OverlayWidth, left + thickness);
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                SetPixel(x, y, blue, green, red);
            }
        }
    }

    private void SetPixel(
        int x,
        int y,
        byte blue,
        byte green,
        byte red,
        byte alpha = 235)
    {
        int offset =
            (y * OverlayWidth + x) * BytesPerPixel;
        _overlayPixels[offset] = blue;
        _overlayPixels[offset + 1] = green;
        _overlayPixels[offset + 2] = red;
        _overlayPixels[offset + 3] = alpha;
    }

    private static ulong GetGlyph(char character)
    {
        // Row-major 5 x 7 bitmap; the low bit is the upper-left pixel.
        if (character is >= 'a' and <= 'z')
        {
            character = (char)(character - ('a' - 'A'));
        }

        return character switch
        {
            '0' => 0x3A33AE62EUL,
            '1' => 0x3884210C4UL,
            '2' => 0x7C444422EUL,
            '3' => 0x3E107420FUL,
            '4' => 0x211F4A988UL,
            '5' => 0x3E107843FUL,
            '6' => 0x3A317842EUL,
            '7' => 0x08422221FUL,
            '8' => 0x3A317462EUL,
            '9' => 0x3A10F462EUL,
            'A' => 0x4631FC62EUL,
            'B' => 0x3E317C62FUL,
            'C' => 0x78210843EUL,
            'D' => 0x3E318C62FUL,
            'E' => 0x7C217843FUL,
            'F' => 0x04217843FUL,
            'G' => 0x7A31E843EUL,
            'H' => 0x4631FC631UL,
            'I' => 0x38842108EUL,
            'J' => 0x19294211CUL,
            'K' => 0x452519531UL,
            'L' => 0x7C2108421UL,
            'M' => 0x4631AD771UL,
            'N' => 0x4631CD671UL,
            'O' => 0x3A318C62EUL,
            'P' => 0x04217C62FUL,
            'Q' => 0x59358C62EUL,
            'R' => 0x45257C62FUL,
            'S' => 0x3E107043EUL,
            'T' => 0x10842109FUL,
            'U' => 0x3A318C631UL,
            'V' => 0x11518C631UL,
            'W' => 0x2AB5AC631UL,
            'X' => 0x462A22A31UL,
            'Y' => 0x108422A31UL,
            'Z' => 0x7C222221FUL,
            '%' => 0x018D11173UL,
            '-' => 0x0000F8000UL,
            '.' => 0x18C000000UL,
            _ => 0
        };
    }

    private static (byte Blue, byte Green, byte Red) GetColor(int classId)
    {
        return (Math.Abs(classId) % 6) switch
        {
            0 => (68, 255, 68),
            1 => (255, 190, 40),
            2 => (55, 210, 255),
            3 => (255, 80, 220),
            4 => (80, 120, 255),
            _ => (255, 255, 80)
        };
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
