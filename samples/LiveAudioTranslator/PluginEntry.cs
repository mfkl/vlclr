using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR;
using VLCLR.Module;
using VLCLR.Native;
using VLCLR.Plugin;

namespace LiveAudioTranslator;

public static unsafe class PluginEntry
{
    private static readonly nint ApiVersion = Marshal.StringToCoTaskMemUTF8("4.0.6");
    private static readonly nint Copyright = Marshal.StringToCoTaskMemUTF8("VLCLR");
    private static readonly nint AudioOperations = CreateOperations(
        (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint>)&FilterAudio,
        (nint)(delegate* unmanaged[Cdecl]<nint, void>)&FlushAudio,
        (nint)(delegate* unmanaged[Cdecl]<nint, void>)&CloseAudio);
    private static readonly nint SubSourceOperations = CreateOperations(
        (nint)(delegate* unmanaged[Cdecl]<nint, long, nint>)&SourceSubpicture,
        0,
        (nint)(delegate* unmanaged[Cdecl]<nint, void>)&CloseSubSource);

    [UnmanagedCallersOnly(EntryPoint = "vlc_entry_api_version", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* VlcEntryApiVersion() => (byte*)ApiVersion;

    [UnmanagedCallersOnly(EntryPoint = "vlc_entry_copyright", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* VlcEntryCopyright() => (byte*)Copyright;

    [UnmanagedCallersOnly(EntryPoint = "vlc_entry", CallConvs = [typeof(CallConvCdecl)])]
    public static int VlcEntry(nint vlcSetPtr, nint opaque)
    {
        int result = ModuleBuilder.Create(vlcSetPtr, opaque)
            .WithName("dotnet_audio_translator")
            .WithShortcut("dotnet_audio_translator")
            .WithShortName(".NET live audio translator")
            .WithDescription("Capture decoded audio for offline Whisper transcription and translation")
            .WithCapability("audio filter")
            .WithScore(100)
            .WithNoUnload()
            .WithSubcategory(VLCConfigSubcategory.SUBCAT_AUDIO_AFILTER)
            .AddFileConfig(
                "live-translator-whisper-model",
                null,
                "Whisper GGML model",
                "Multilingual Whisper GGML model used to translate speech into English")
            .AddFileConfig(
                "live-translator-whisper-runtime",
                null,
                "Whisper native runtime",
                "Path to the Whisper.net CPU whisper.dll")
            .AddDirectoryConfig(
                "live-translator-translation-model",
                null,
                "English-to-target translation model",
                "Directory containing the validated OPUS-MT ONNX model bundle")
            .AddStringConfig("live-translator-source-language", "auto", "Spoken language", "auto or an ISO language code")
            .AddStringConfig("live-translator-target-language", "fr", "Subtitle language", "Target OPUS-MT language code")
            .AddIntegerConfig("live-translator-whisper-threads", 4, 1, 16, "Whisper threads")
            .AddIntegerConfig("live-translator-translation-threads", 4, 1, 16, "Translation threads")
            .AddFloatConfig("live-translator-vad-threshold", 0.012, 0.001, 0.25, "Speech energy threshold")
            .AddIntegerConfig("live-translator-silence-ms", 650, 200, 3_000, "Silence ending an utterance")
            .AddIntegerConfig("live-translator-max-utterance-ms", 6_000, 1_000, 20_000, "Maximum speech chunk")
            .AddIntegerConfig("live-translator-subtitle-duration-ms", 3_500, 500, 10_000, "Minimum subtitle duration")
            .WithOpenCallback(&OpenAudio, "OpenAudio")
            .Register();
        if (result != 0)
            return result;

        return ModuleBuilder.Create(vlcSetPtr, opaque)
            // VLC_MODULE_NAME is only legal on the root descriptor. A second
            // VLC_MODULE_CREATE is a submodule and is selected by shortcut.
            .WithShortcut("dotnet_live_subtitles")
            .WithShortName(".NET live translated subtitles")
            .WithDescription("Display speech translated by the .NET live audio filter")
            .WithCapability("sub source")
            .WithScore(100)
            .WithSubcategory(VLCConfigSubcategory.SUBCAT_VIDEO_SUBPIC)
            .WithOpenCallback(&OpenSubSource, "OpenSubSource")
            .Register();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OpenAudio(nint filterPtr)
    {
        var context = new VLCFilterContext(filterPtr);
        GCHandle handle = default;
        AudioFilterInstance? instance = null;
        try
        {
            var filter = (VLCFilter*)filterPtr;
            context.Logger.Info(
                $"[LiveAudioTranslator] Audio open invoked category={filter->FormatIn.Category} " +
                $"codec={VLCFourCC.ToString(filter->FormatIn.Codec)}");
            VLCAudioFormat format = filter->FormatIn.Audio;
            if (format.Format is not (VLCFourCC.F32L or VLCFourCC.S16L) ||
                format.Rate == 0 ||
                format.Channels == 0)
            {
                context.Logger.Warning(
                    $"[LiveAudioTranslator] Unsupported PCM format " +
                    $"{VLCFourCC.ToString(format.Format)} {format.Rate}Hz/{format.Channels}ch");
                return -1;
            }

            LiveAudioTranslationOptions options = LiveAudioTranslationOptions.Read(filterPtr);
            instance = new AudioFilterInstance(
                LiveTranslationHubRegistry.Acquire(options),
                format,
                context.Logger);
            handle = GCHandle.Alloc(instance);
            context.SetSys(GCHandle.ToIntPtr(handle));
            context.SetOperations(AudioOperations);
            context.Logger.Info(
                $"[LiveAudioTranslator] Audio capture opened " +
                $"format={VLCFourCC.ToString(format.Format)} rate={format.Rate} channels={format.Channels}");
            return 0;
        }
        catch (Exception ex)
        {
            context.Logger.Error($"[LiveAudioTranslator] Audio open failed: {ex.Message}");
            instance?.Dispose();
            if (handle.IsAllocated)
                handle.Free();
            context.SetSys(0);
            context.SetOperations(0);
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint FilterAudio(nint filterPtr, nint blockPtr)
    {
        if (blockPtr == 0)
            return 0;
        try
        {
            AudioFilterInstance? instance = GetInstance<AudioFilterInstance>(filterPtr);
            if (instance == null)
                return blockPtr;

            var block = (VLCBlock*)blockPtr;
            if ((block->Flags & VLCBlockFlags.Discontinuity) != 0)
                instance.Hub.ResetAudio();
            if ((block->Flags & VLCBlockFlags.Corrupted) == 0 && block->Buffer != 0)
                instance.Push(block);
            instance.DrainStatus();
        }
        catch
        {
        }
        return blockPtr;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FlushAudio(nint filterPtr)
    {
        try
        {
            GetInstance<AudioFilterInstance>(filterPtr)?.Hub.ResetAudio();
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CloseAudio(nint filterPtr) => CloseInstance<AudioFilterInstance>(filterPtr);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OpenSubSource(nint filterPtr)
    {
        var context = new VLCFilterContext(filterPtr);
        GCHandle handle = default;
        SubSourceInstance? instance = null;
        try
        {
            LiveAudioTranslationOptions options = LiveAudioTranslationOptions.Read(filterPtr);
            instance = new SubSourceInstance(
                LiveTranslationHubRegistry.Acquire(options),
                context.Logger);
            handle = GCHandle.Alloc(instance);
            context.SetSys(GCHandle.ToIntPtr(handle));
            context.SetOperations(SubSourceOperations);
            context.Logger.Info("[LiveAudioTranslator] Live subtitle source opened");
            return 0;
        }
        catch (Exception ex)
        {
            context.Logger.Error($"[LiveAudioTranslator] Subtitle source open failed: {ex.Message}");
            instance?.Dispose();
            if (handle.IsAllocated)
                handle.Free();
            context.SetSys(0);
            context.SetOperations(0);
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint SourceSubpicture(nint filterPtr, long date)
    {
        try
        {
            SubSourceInstance? instance = GetInstance<SubSourceInstance>(filterPtr);
            if (instance == null)
                return 0;
            instance.DrainStatus();
            if (!instance.Hub.TryTakeCue(out TranslatedCue cue))
                return 0;

            nint subpicture = CreateTextSubpicture(filterPtr, date, cue);
            if (subpicture != 0)
                instance.ReportRendered(cue);
            return subpicture;
        }
        catch
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CloseSubSource(nint filterPtr) => CloseInstance<SubSourceInstance>(filterPtr);

    private static nint CreateTextSubpicture(nint filterPtr, long date, TranslatedCue cue)
    {
        var filter = (VLCFilter*)filterPtr;
        if (filter->Owner.Callbacks == 0)
            return 0;

        nint bufferNewAddress = Marshal.ReadIntPtr(filter->Owner.Callbacks);
        if (bufferNewAddress == 0)
            return 0;
        var bufferNew = (delegate* unmanaged[Cdecl]<nint, nint>)bufferNewAddress;
        nint subpicturePtr = bufferNew(filterPtr);
        if (subpicturePtr == 0)
            return 0;

        nint regionPtr = VLCCore.SubpictureRegionNewText();
        if (regionPtr == 0)
        {
            VLCCore.SubpictureDelete(subpicturePtr);
            return 0;
        }

        nint textPtr = VLCCore.TextSegmentNew(cue.Text);
        if (textPtr == 0)
        {
            VLCCore.SubpictureRegionDelete(regionPtr);
            VLCCore.SubpictureDelete(subpicturePtr);
            return 0;
        }

        var subpicture = (VLCSubpicture*)subpicturePtr;
        var region = (VLCSubpictureRegion*)regionPtr;
        region->Text = textPtr;
        region->Format.SarNum = 1;
        region->Format.SarDen = 1;
        region->Align = VLCSubpictureAlign.Bottom;
        region->IsAbsolute = 0;
        region->IsInWindow = 0;
        region->TextFlags = VLCSubpictureTextFlags.IsText | VLCSubpictureTextFlags.NoRegionBackground;
        AppendRegion(subpicture, region);

        subpicture->Start = date;
        subpicture->Stop = date + cue.DurationMilliseconds * VLCTick.Millisecond;
        subpicture->IsEphemer = 1;
        subpicture->IsSubtitle = 1;
        return subpicturePtr;
    }

    private static void AppendRegion(VLCSubpicture* subpicture, VLCSubpictureRegion* region)
    {
        var head = (VLCListNode*)(&subpicture->RegionsPrev);
        var node = (VLCListNode*)(&region->NodePrev);
        node->Prev = head->Prev;
        node->Next = (nint)head;
        ((VLCListNode*)head->Prev)->Next = (nint)node;
        head->Prev = (nint)node;
    }

    private static T? GetInstance<T>(nint filterPtr) where T : class
    {
        nint sys = ((VLCFilter*)filterPtr)->Sys;
        return sys == 0 ? null : GCHandle.FromIntPtr(sys).Target as T;
    }

    private static void CloseInstance<T>(nint filterPtr) where T : class, IDisposable
    {
        var context = new VLCFilterContext(filterPtr);
        nint sys = context.Sys;
        context.SetSys(0);
        context.SetOperations(0);
        if (sys == 0)
            return;

        GCHandle handle = GCHandle.FromIntPtr(sys);
        try
        {
            (handle.Target as T)?.Dispose();
        }
        catch
        {
        }
        finally
        {
            if (handle.IsAllocated)
                handle.Free();
        }
    }

    private static nint CreateOperations(nint firstCallback, nint flush, nint close)
    {
        nint pointer = Marshal.AllocHGlobal(Marshal.SizeOf<VLCFilterOperations>());
        Marshal.StructureToPtr(new VLCFilterOperations
        {
            FilterVideo = firstCallback,
            Flush = flush,
            Close = close
        }, pointer, false);
        return pointer;
    }
}

internal sealed unsafe class AudioFilterInstance(
    LiveTranslationHubLease lease,
    VLCAudioFormat format,
    VLCLogger logger) : IDisposable
{
    public LiveTranslationHub Hub => lease.Hub;

    public void Push(VLCBlock* block)
    {
        int channels = format.Channels;
        nuint requestedSamples = (nuint)block->SampleCount * (nuint)channels;
        if (format.Format == VLCFourCC.F32L)
        {
            int available = checked((int)Math.Min(block->BufferLength / sizeof(float), requestedSamples));
            Hub.PushFloat32(new ReadOnlySpan<float>((void*)block->Buffer, available), (int)format.Rate, channels);
        }
        else
        {
            int available = checked((int)Math.Min(block->BufferLength / sizeof(short), requestedSamples));
            Hub.PushPcm16(new ReadOnlySpan<short>((void*)block->Buffer, available), (int)format.Rate, channels);
        }
    }

    public void DrainStatus()
    {
        while (Hub.TryTakeStatus(out string status))
            logger.Info($"[LiveAudioTranslator] {status}");
    }

    public void Dispose()
    {
        logger.Info("[LiveAudioTranslator] Audio capture closed");
        lease.Dispose();
    }
}

internal sealed class SubSourceInstance(
    LiveTranslationHubLease lease,
    VLCLogger logger) : IDisposable
{
    public LiveTranslationHub Hub => lease.Hub;

    public void DrainStatus()
    {
        while (Hub.TryTakeStatus(out string status))
            logger.Info($"[LiveAudioTranslator] {status}");
    }

    public void ReportRendered(TranslatedCue cue) =>
        logger.Info($"[LiveAudioTranslator] event=subtitle outcome=rendered duration_ms={cue.DurationMilliseconds}");

    public void Dispose()
    {
        logger.Info("[LiveAudioTranslator] Live subtitle source closed");
        lease.Dispose();
    }
}
