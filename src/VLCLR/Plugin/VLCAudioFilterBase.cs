// VLC Audio Filter Base Class
// Lifecycle, formats and blocks of an "audio filter" module
// VLC Version: 4.0.6

using System.Runtime.InteropServices;
using VLCLR.Module;
using VLCLR.Native;
using Interop = VLCLR.Native.Interop;

namespace VLCLR.Plugin;

/// <summary>
/// Base class for audio filter plugins (capability "audio filter"), the
/// filters of VLC's <c>--audio-filter</c> list and of the transcoder's
/// <c>afilter</c>. The filter works on interleaved 32-bit float samples at
/// the input's rate: VLC converts what comes before and after it. Subclass
/// and override <see cref="ProcessAudio"/>.
/// </summary>
/// <remarks>
/// In VLC's audio output, user filters run on the decoder's channel layout,
/// before the downmix to the device's: a filter sees every channel of a 5.1
/// track. By default the output layout is the input's and blocks are
/// processed in place; a filter that changes the layout calls
/// <see cref="VLCAudioFilterContext.SetOutputChannels"/> in
/// <see cref="OnOpen"/>, and gets a new output block for each input block.
/// </remarks>
public abstract class VLCAudioFilterBase : IDisposable
{
    private nint _filterPtr;
    private long _blockCount;
    private bool _initialized;
    private bool _disposed;
    private int _inputChannelCount;
    private int _outputChannelCount;
    private bool _warnedShortBlock;

    private VLCAudioFilterContext _context;
    private GCHandle _filterOpsHandle;
    private VLCFilterOperations _filterOps;
    private readonly FrameAllocationMeter? _allocations =
        VLCLRFeatures.FrameAllocationCheck ? new() : null;

    /// <summary>Number of blocks processed.</summary>
    protected long BlockCount => Interlocked.Read(ref _blockCount);

    /// <summary>Whether the filter is open.</summary>
    protected bool IsInitialized => _initialized;

    /// <summary>The filter's formats and logger.</summary>
    protected VLCAudioFilterContext Context => _context;

    /// <summary>
    /// Called when the filter opens, with the input already set to 32-bit
    /// float and the output to the same format. Return false to refuse the
    /// input (VLC then tries without this filter).
    /// </summary>
    protected virtual bool OnOpen(VLCAudioFilterContext context) => true;

    /// <summary>Called when the filter closes.</summary>
    protected virtual void OnClose() { }

    /// <summary>Called on a seek or a discontinuity: drop the state of past blocks.</summary>
    protected virtual void OnFlush() { }

    /// <summary>
    /// Processes one block: writes <see cref="VLCAudioBlock.Output"/> from
    /// <see cref="VLCAudioBlock.Input"/>. Runs on VLC's audio thread, for every
    /// block: don't allocate or block.
    /// </summary>
    protected abstract void ProcessAudio(in VLCAudioBlock block);

    /// <summary>
    /// Internal: called by the generated Open callback.
    /// </summary>
    /// <returns>0 on success, -1 when the filter refused the input.</returns>
    public unsafe int InternalOpen(nint filterPtr, delegate* unmanaged<nint, nint, nint> filterAudio,
        delegate* unmanaged<nint, void> close, delegate* unmanaged<nint, void> flush = null)
    {
        _filterPtr = filterPtr;
        _context = new VLCAudioFilterContext(filterPtr);
        var filter = (VLCFilter*)filterPtr;

        // VLC asserts that a module that fails to open leaves both formats as
        // they were, so keep them to put them back.
        VLCAudioFormat inputFormat = filter->FormatIn->Audio;
        VLCAudioFormat outputFormat = filter->FormatOut->Audio;
        uint inputCodec = filter->FormatIn->Codec;
        uint outputCodec = filter->FormatOut->Codec;

        try
        {
            if (inputFormat.ChannelType != (int)Interop.AudioChannelType.Bitmap
                || VLCAudioChannels.Count(inputFormat.PhysicalChannels) == 0)
            {
                _context.Logger.Debug($"[VLCLR] {GetType().Name}: no channel map, not a format for this filter");
                return -1;
            }

            var input = (Interop.AudioFormat*)&filter->FormatIn->Video;
            input->Format = VLCFourCC.FL32;
            Interop.Aout.FormatPrepare(input);
            filter->FormatIn->Codec = VLCFourCC.FL32;
            filter->FormatOut->Audio = filter->FormatIn->Audio;
            filter->FormatOut->Codec = VLCFourCC.FL32;

            _context.Logger.Info($"[VLCLR] Opening audio filter: {GetType().Name}");
            if (!OnOpen(_context))
            {
                _context.Logger.Debug($"[VLCLR] Filter {GetType().Name} OnOpen() returned false");
                Restore();
                return -1;
            }

            _inputChannelCount = VLCAudioChannels.Count(filter->FormatIn->Audio.PhysicalChannels);
            _outputChannelCount = VLCAudioChannels.Count(filter->FormatOut->Audio.PhysicalChannels);
            _context.Logger.Info(
                $"[VLCLR] Format: {_context.Rate} Hz, {_inputChannelCount} to {_outputChannelCount} channels");

            _filterOps = new VLCFilterOperations
            {
                FilterVideo = (nint)filterAudio, // filter_audio: the same union member
                Close = (nint)close,
                Flush = flush != null ? (nint)flush : 0,
            };
            _filterOpsHandle = GCHandle.Alloc(_filterOps, GCHandleType.Pinned);
            filter->Operations = _filterOpsHandle.AddrOfPinnedObject();

            _initialized = true;
            _context.Logger.Info($"[VLCLR] Filter {GetType().Name} initialized successfully");
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                Restore();
                _context.Logger.Error($"[VLCLR] Exception in audio filter Open: {ex.Message}");
            }
            catch
            {
                // Ignore logging failures
            }
            return -1;
        }

        void Restore()
        {
            filter->FormatIn->Audio = inputFormat;
            filter->FormatOut->Audio = outputFormat;
            filter->FormatIn->Codec = inputCodec;
            filter->FormatOut->Codec = outputCodec;
        }
    }

    /// <summary>
    /// Internal: called by the generated filter_audio callback. Returns the
    /// input block, processed in place, or a new block with the output
    /// layout (the input is released). On an error, an in-place filter
    /// passes the block on unchanged and a layout-changing one drops it.
    /// </summary>
    public unsafe nint InternalFilterAudio(nint filterPtr, nint blockPtr)
    {
        if (blockPtr == 0)
            return 0;

        var input = (Interop.Frame*)blockPtr;
        bool inPlace = _inputChannelCount == _outputChannelCount;
        Interop.Frame* output = null;
        try
        {
            if (!_initialized)
                return inPlace ? blockPtr : Drop(input, null);

            int frames = (int)input->NbSamples;
            long inputSamples = (long)frames * _inputChannelCount;
            if ((ulong)inputSamples * sizeof(float) > input->BufferSize)
            {
                if (!_warnedShortBlock)
                {
                    _warnedShortBlock = true;
                    _context.Logger.Warning(
                        $"[VLCLR] {GetType().Name}: a block holds {input->BufferSize} bytes for {frames} frames, skipped");
                }
                return inPlace ? blockPtr : Drop(input, null);
            }

            _allocations?.BeginFrame();
            var samples = new Span<float>(input->Buffer, (int)inputSamples);
            if (inPlace)
            {
                ProcessAudio(new VLCAudioBlock(samples, samples, frames,
                    _inputChannelCount, _outputChannelCount, input->Pts, inPlace: true));
            }
            else
            {
                int outputSamples = frames * _outputChannelCount;
                output = Interop.VlcFrame.Alloc((nuint)outputSamples * sizeof(float));
                if (output == null)
                {
                    _allocations?.EndFrame();
                    return Drop(input, null);
                }
                Interop.VlcFrame.CopyProperties(output, input);
                ProcessAudio(new VLCAudioBlock(samples, new Span<float>(output->Buffer, outputSamples), frames,
                    _inputChannelCount, _outputChannelCount, input->Pts, inPlace: false));
                Interop.VlcFrame.Release(input);
            }

            Interlocked.Increment(ref _blockCount);
            _allocations?.EndFrame();
            return inPlace ? blockPtr : (nint)output;
        }
        catch (Exception ex)
        {
            try
            {
                _context.Logger.Error($"[VLCLR] Exception in FilterAudio: {ex.Message}");
            }
            catch
            {
                // Ignore logging failures
            }
            _allocations?.EndFrame();
            return inPlace ? blockPtr : Drop(input, output);
        }
    }

    private static unsafe nint Drop(Interop.Frame* input, Interop.Frame* output)
    {
        if (output != null)
            Interop.VlcFrame.Release(output);
        Interop.VlcFrame.Release(input);
        return 0;
    }

    /// <summary>Internal: called by the generated close callback.</summary>
    public void InternalClose(nint filterPtr)
    {
        try
        {
            _allocations?.Stop();
            _context.Logger.Info($"[VLCLR] Closing audio filter: {GetType().Name} (processed {BlockCount} blocks)");
            ReportAllocations();

            OnClose();

            if (_filterOpsHandle.IsAllocated)
                _filterOpsHandle.Free();

            _initialized = false;
        }
        catch (Exception ex)
        {
            try
            {
                _context.Logger.Error($"[VLCLR] Exception in audio filter Close: {ex.Message}");
            }
            catch
            {
                // Ignore logging failures
            }
        }
    }

    /// <summary>Internal: called by the generated flush callback.</summary>
    public void InternalFlush(nint filterPtr)
    {
        try
        {
            OnFlush();
            _allocations?.Restart();
        }
        catch (Exception ex)
        {
            try
            {
                _context.Logger.Error($"[VLCLR] Exception in audio filter Flush: {ex.Message}");
            }
            catch
            {
                // Ignore logging failures
            }
        }
    }

    /// <summary>
    /// Logs what the blocks allocated on the managed heap after the warm-up,
    /// as a warning when they allocated anything (see <see cref="FrameAllocationMeter"/>).
    /// </summary>
    private void ReportAllocations()
    {
        if (_allocations is null)
            return;

        string message = $"[VLCLR] {GetType().Name}: {_allocations.Summary()}";
        if (_allocations.IsAllocationFree)
            _context.Logger.Info(message);
        else
            _context.Logger.Warning(message);
    }

    /// <summary>Disposes the filter instance.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Disposes managed and unmanaged resources.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (_filterOpsHandle.IsAllocated)
            _filterOpsHandle.Free();

        _disposed = true;
    }

    /// <summary>Finalizer.</summary>
    ~VLCAudioFilterBase()
    {
        Dispose(false);
    }
}
