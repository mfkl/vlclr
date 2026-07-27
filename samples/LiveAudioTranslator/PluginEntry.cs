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
            .WithDescription("Transport current decoded audio to a warming translation worker")
            .WithCapability("audio filter")
            .WithScore(100)
            .WithNoUnload()
            .WithSubcategory(VLCConfigSubcategory.SUBCAT_AUDIO_AFILTER)
            .AddStringConfig(
                "live-translator-mode",
                "live-immediate",
                "Translation mode",
                "prepared, live-immediate, or delayed live-sync")
            .AddFileConfig(
                "live-translator-cue-file",
                null,
                "Prepared timed-cue file",
                "Versioned JSONL timeline produced by LiveAudioTranslator.Prepare")
            .AddStringConfig(
                "live-translator-session",
                null,
                "Worker session ID",
                "Unique session ID created by the runner")
            .AddStringConfig(
                "live-translator-pipe",
                null,
                "Worker pipe",
                "Unique named pipe created by the runner")
            .AddStringConfig(
                "live-translator-speech-model",
                "whisper-tiny-multilingual",
                "Speech model profile",
                "Stable speech model profile ID")
            .AddStringConfig(
                "live-translator-translation-model",
                "opus-mt-en-fr",
                "Translation model profile",
                "Stable translation model profile ID")
            .AddStringConfig("live-translator-speech-provider", "auto", "Speech inference provider", "auto, cpu, openvino, or vulkan")
            .AddStringConfig("live-translator-translation-provider", "auto", "Translation inference provider", "auto, cpu, directml, or openvino")
            .AddStringConfig("live-translator-source-language", "auto", "Spoken language", "auto or an ISO language code")
            .AddStringConfig("live-translator-target-language", "fr", "Subtitle language", "Target OPUS-MT language code")
            .AddIntegerConfig("live-translator-input-delay-ms", 15_000, 8_000, 60_000, "VLC live-sync input delay")
            .AddIntegerConfig("live-translator-max-utterance-ms", 2_500, 1_000, 15_000, "Maximum speech chunk")
            .AddIntegerConfig("live-translator-burst-jitter-ms", 2_000, 250, 10_000, "Extra transport queue burst budget")
            .AddIntegerConfig("live-translator-subtitle-duration-ms", 2_500, 500, 5_000, "Live subtitle duration")
            .AddIntegerConfig("live-translator-maximum-age-ms", 7_000, 500, 10_000, "Maximum live caption age")
            .AddIntegerConfig("live-translator-early-tolerance-ms", 80, 0, 500, "Synchronized cue early tolerance")
            .AddIntegerConfig("live-translator-stale-clock-ms", 2_000, 250, 10_000, "Maximum audio clock age")
            .AddIntegerConfig("live-translator-lead-tolerance-ms", 1_000, 250, 5_000, "Decode-lead anchor tolerance")
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
            bool discontinuity = (block->Flags & VLCBlockFlags.Discontinuity) != 0;
            if ((block->Flags & VLCBlockFlags.Corrupted) == 0)
                instance.Push(block, discontinuity);
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
            if (!instance.Hub.TryTakeCue(date, out TranslatedCue cue))
            {
                instance.DrainStatus();
                return 0;
            }
            instance.DrainStatus();

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
        // subpicture_region_NewText initializes relative coordinates to
        // INT_MAX as an unset sentinel. Native sub-source modules such as
        // marq always replace both values before returning the region.
        region->X = 0;
        region->Y = 0;
        region->IsAbsolute = 0;
        region->IsInWindow = 0;
        region->TextFlags = VLCSubpictureTextFlags.IsText | VLCSubpictureTextFlags.NoRegionBackground;
        AppendRegion(subpicture, region);

        subpicture->Start = date;
        subpicture->Stop = date + cue.DurationMilliseconds * VLCTick.Millisecond;
        subpicture->IsEphemer = 0;
        // A sub-source is called with VLC's system clock, just like the native
        // marquee source. Marking this as an input-timestamped subtitle mixes
        // clock domains and can trip VLC's debug-build subpicture assertions.
        subpicture->IsSubtitle = 0;
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

    public void Push(VLCBlock* block, bool discontinuity)
    {
        if (block->PresentationTimestamp == VLCTick.Invalid)
        {
            if (discontinuity)
                Hub.ResetAudio();
            return;
        }

        long blockDuration = block->Length > 0
            ? block->Length
            : block->SampleCount * VLCTick.Second / Math.Max(1, format.Rate);
        long systemTick = VLCCore.TickNow();
        Hub.ObserveAudio(block->PresentationTimestamp, blockDuration, systemTick, discontinuity);
        if (Hub.Mode == LiveAudioTranslationMode.Prepared || block->Buffer == 0)
            return;

        int channels = format.Channels;
        nuint requestedSamples = (nuint)block->SampleCount * (nuint)channels;
        if (format.Format == VLCFourCC.F32L)
        {
            int available = checked((int)Math.Min(block->BufferLength / sizeof(float), requestedSamples));
            Hub.PushFloat32(
                new ReadOnlySpan<float>((void*)block->Buffer, available),
                (int)format.Rate,
                channels,
                block->PresentationTimestamp,
                blockDuration);
        }
        else
        {
            int available = checked((int)Math.Min(block->BufferLength / sizeof(short), requestedSamples));
            Hub.PushPcm16(
                new ReadOnlySpan<short>((void*)block->Buffer, available),
                (int)format.Rate,
                channels,
                block->PresentationTimestamp,
                blockDuration);
        }
    }

    public void DrainStatus()
    {
        for (int count = 0; count < 4 && Hub.TryTakeStatus(out string status); count++)
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
        for (int count = 0; count < 4 && Hub.TryTakeStatus(out string status); count++)
            logger.Info($"[LiveAudioTranslator] {status}");
    }

    public void ReportRendered(TranslatedCue cue) =>
        logger.Info(
            $"[LiveAudioTranslator] event=subtitle outcome=rendered sequence={cue.Sequence} " +
            $"duration_ms={cue.DurationMilliseconds} scheduler_error_ms={cue.SchedulingErrorTicks / 1000d:F1} " +
            $"semantic_latency_ms={cue.SemanticLatencyTicks / 1000d:F1} " +
            $"generation={cue.Generation}");

    public void Dispose()
    {
        logger.Info("[LiveAudioTranslator] Live subtitle source closed");
        lease.Dispose();
    }
}
