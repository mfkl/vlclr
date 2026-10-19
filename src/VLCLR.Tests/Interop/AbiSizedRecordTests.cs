using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR.Native;
using Xunit;
using Gen = VLCLR.Native.Interop;

namespace VLCLR.Tests.Interop;

/// <summary>
/// No VLCLR struct, generated or hand-written, holds or passes by value a
/// struct whose C size differs between ABIs (see <see cref="AbiSizedRecords"/>).
/// </summary>
public sealed class AbiSizedRecordTests
{
    /// <summary>
    /// Every hand-written explicit-layout struct and the generated record it
    /// lays out, or null when it has none. A wrapper is ABI-sized when it is
    /// larger than that record on the smallest ABI.
    /// </summary>
    private static readonly Dictionary<Type, Type?> Wrappers = new()
    {
        [typeof(VLCAudioFormat)] = typeof(Gen.AudioFormat),
        [typeof(VLCBlock)] = typeof(Gen.Frame),
        [typeof(VLCChromaDescription)] = typeof(Gen.ChromaDescription),
        [typeof(VLCChromaPlaneSize)] = typeof(Gen.ChromaDescriptionP),
        [typeof(VLCRational)] = typeof(Gen.Rational),
        [typeof(VLCValueNative)] = typeof(Gen.Value),
        [typeof(VLCPlayerCbs)] = typeof(Gen.PlayerCbs),
        [typeof(VLCPictureContext)] = typeof(Gen.PictureContext),
        [typeof(VLCD3D11PictureSystem)] = null,
        [typeof(VLCD3D11PictureContext)] = null,
        [typeof(VLCEsFormat)] = typeof(Gen.EsFormat),
        [typeof(VLCFilter)] = typeof(Gen.Filter),
        [typeof(VLCFilterOperations)] = typeof(Gen.FilterOperations),
        [typeof(VLCFilterOwner)] = typeof(Gen.FilterOwner),
        [typeof(VLCFilterVideoCallbacks)] = typeof(Gen.FilterVideoCallbacks),
        [typeof(VLCObjectHeader)] = typeof(Gen.ObjectHeader),
        [typeof(VLCPicture)] = typeof(Gen.Picture),
        [typeof(VLCPlane)] = typeof(Gen.Plane),
        [typeof(VLCSubpicture)] = typeof(Gen.Subpicture),
        [typeof(VLCSubpictureUpdater)] = typeof(Gen.SubpictureUpdater),
        [typeof(VLCSubpictureUpdaterOps)] = typeof(Gen.SpuUpdaterOps),
        [typeof(VLCSubpictureRegion)] = typeof(Gen.SubpictureRegion),
        [typeof(VLCListNode)] = typeof(Gen.List),
        [typeof(VLCTextRendererOperations)] = typeof(Gen.FilterOperations),
        [typeof(VLCTextSegment)] = typeof(Gen.TextSegment),
        [typeof(VLCTextSegmentRuby)] = typeof(Gen.TextSegmentRuby),
        [typeof(VLCTextStyle)] = typeof(Gen.TextStyle),
        [typeof(VLCVideoFormat)] = typeof(Gen.VideoFormat),
    };

    /// <summary>
    /// Public members that predate this test. VLCEsFormat is sized for Windows
    /// (240 bytes, es_format_t is 232 on Linux and macOS), so assigning a whole
    /// one to either overruns fmt_in or fmt_out there; VLCFilter.FormatOut says
    /// so. Turning them into pointers changes the public API. Remove an entry
    /// once the member is fixed: the test fails while an entry no longer matches.
    /// </summary>
    private static readonly string[] KnownViolations =
    [
        "VLCFilter.FormatIn holds VLCEsFormat by value",
        "VLCFilter.FormatOut returns a ref to VLCEsFormat",
    ];

    private static readonly Assembly Vlclr = typeof(VLCCore).Assembly;

    [Fact]
    public void Every_hand_written_explicit_layout_struct_names_its_record()
    {
        var unlisted = Vlclr.GetTypes()
            .Where(t => t.IsValueType && t.StructLayoutAttribute?.Value == LayoutKind.Explicit)
            .Where(t => t.Namespace != typeof(Gen.Picture).Namespace && !Wrappers.ContainsKey(t))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(unlisted.Count == 0, "Add to Wrappers: " + string.Join(", ", unlisted));
    }

    [Fact]
    public void No_struct_holds_or_passes_an_abi_sized_struct_by_value()
    {
        var violations = AbiSizedRecords.Violations(Vlclr.GetTypes(), IsAbiSized);

        Assert.Equal([], violations.Except(KnownViolations));
        Assert.Equal([], KnownViolations.Except(violations));
    }

    private static bool IsAbiSized(Type type)
        => AbiSizedRecords.IsGeneratedAbiSized(type)
            || (Wrappers.TryGetValue(type, out var record) && record is not null
                && SizeOf(type) > (int)record.GetField("MinSizeOf")!.GetRawConstantValue()!);

    private static int SizeOf(Type type)
        => (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(type).Invoke(null, null)!;
}
