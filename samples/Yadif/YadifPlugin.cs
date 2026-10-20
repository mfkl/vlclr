using VLCLR.Filters.Video;
using VLCLR.Native;
using VLCLR.Native.Interop;
using VLCLR.Plugin;

namespace YadifSample;

/// <summary>
/// VLC's yadif deinterlacer as a C# plugin: same output as VLC's deinterlace
/// filter in yadif mode, picture for picture, using the fastest
/// implementation the build supports. The kernels are <see cref="Yadif"/> and
/// <see cref="XDeinterlace"/> from VLCLR.Filters; this class is the VLC video
/// filter around them, with the picture history and timestamps of VLC's
/// DoDeinterlacing (modules/video_filter/deinterlace/common.c).
/// </summary>
[VLCModule("dotnet_yadif")]
[VLCCapability("video filter", Score = 0)]
[VLCDescription("Yadif deinterlacer (C#)")]
public unsafe partial class YadifPlugin : VLCVideoFilterBase
{
    private const int MaxPlanes = 5; // PICTURE_PLANE_MAX

    private Yadif? _yadif;

    // The last three input pictures, oldest first, held (pp_history), and
    // their dates (meta).
    private readonly nint[] _history = new nint[3];
    private readonly long[] _dates = new long[3];

    // How many pictures the output is behind the input: 0 until yadif has
    // its three pictures, then 1 (i_frame_offset).
    private int _frameOffset;

    // The date of the picture being returned, for SetOutputProperties.
    private long _outputDate;

    protected override bool OnOpen(VLCFilterContext context)
    {
        // Same checks as VLC's deinterlace filter in yadif mode: planar, one
        // or two bytes per sample, the same chroma out. Where VLC falls back
        // to its blend mode (packed chromas), this filter refuses.
        uint chroma = context.InputFormat.Chroma;
        nint description = VLCCore.FourccGetChromaDescription(chroma);
        if (description == 0 || ((VLCChromaDescription*)description)->PixelSize > 2)
        {
            context.Logger.Debug($"unsupported chroma {context.ChromaString}");
            return false;
        }
        if (((VLCChromaDescription*)description)->PlaneCount != 3)
        {
            context.Logger.Error($"yadif needs a planar chroma ({context.ChromaString})");
            return false;
        }
        if (context.OutputFormat.Chroma != chroma)
        {
            context.Logger.Error("Input and output chromas don't match");
            return false;
        }

        // Auto picks the fastest implementation the build and the CPU support
        // for the bit depth.
        uint pixelBits = ((VLCChromaDescription*)description)->PixelBits;
        int bitDepth = ((VLCChromaDescription*)description)->PixelSize == 1 ? 8
            : pixelBits is > 8 and <= 16 ? (int)pixelBits : 16;
        _yadif = new Yadif(YadifImplementation.Auto, bitDepth);
        ResetHistory();
        context.Logger.Debug($"deinterlacing with yadif ({_yadif.Implementation})");
        return true;
    }

    protected override nint ProcessFrameToOutput(VLCFrame frame)
    {
        nint output = Context.NewPicture();
        if (output == 0)
        {
            return 0;
        }

        // Slide the history and the dates.
        VLCCore.PictureRelease(_history[0]);
        _history[0] = _history[1];
        _history[1] = _history[2];
        _history[2] = VLCCore.PictureHold(frame.NativePtr);
        _dates[0] = _dates[1];
        _dates[1] = _dates[2];
        _dates[2] = frame.Date;

        // The output takes the date of the picture it is made from.
        _outputDate = _dates[2 - _frameOffset];

        nint previous = _history[0], current = _history[1], next = _history[2];
        Span<VLCPlane> outputPlanes = Planes(output, stackalloc VLCPlane[MaxPlanes]);
        Span<VLCPlane> nextPlanes = Planes(next, stackalloc VLCPlane[MaxPlanes]);
        if (previous != 0 && current != 0)
        {
            // RenderYadifSingle: the first field is kept, the second rebuilt
            // between the previous and the current picture.
            try
            {
                _yadif!.Render(Planes(previous, stackalloc VLCPlane[MaxPlanes]), Planes(current, stackalloc VLCPlane[MaxPlanes]),
                    nextPlanes, outputPlanes, field: 0, parity: 1);
            }
            catch
            {
                // Pictures of different layouts: the base logs it and passes
                // the input through.
                VLCCore.PictureRelease(output);
                throw;
            }
            _frameOffset = 1;
            return output;
        }
        if (current == 0 && previous == 0)
        {
            // The first picture, with no history yet: X on its own.
            XDeinterlace.Render(nextPlanes, outputPlanes);
            return output;
        }

        // The second picture: yadif has two of the three it needs, and VLC
        // drops it. (VLC's builds with NDEBUG return the input instead,
        // interlaced and with the date the next output gets.)
        _frameOffset = 1;
        VLCCore.PictureRelease(output);
        return 0;
    }

    protected override void SetOutputProperties(nint output, nint input)
    {
        base.SetOutputProperties(output, input);
        ref VLCPicture picture = ref *(VLCPicture*)output;
        picture.Date = _outputDate;
        picture.Progressive = 1;
        picture.FieldCount = 2;
    }

    // FlushDeinterlacing: the next picture is a first one again.
    protected override void OnFlush() => ResetHistory();

    protected override void OnClose() => ResetHistory();

    private void ResetHistory()
    {
        for (int i = 0; i < _history.Length; i++)
        {
            VLCCore.PictureRelease(_history[i]);
            _history[i] = 0;
            _dates[i] = VlcConfig.TickInvalid;
        }
        _frameOffset = 0;
    }

    // The planes of a picture, in a buffer of MaxPlanes.
    private Span<VLCPlane> Planes(nint picture, Span<VLCPlane> buffer)
    {
        var frame = new VLCFrame(picture, Context);
        int count = Math.Min(frame.PlaneCount, MaxPlanes);
        for (int i = 0; i < count; i++)
        {
            buffer[i] = frame.GetPlane(i);
        }
        return buffer[..count];
    }
}
