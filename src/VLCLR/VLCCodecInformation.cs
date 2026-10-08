using System.Collections.ObjectModel;
using System.Globalization;

namespace VLCLR;

/// <summary>Copied VLC input-item codec information. Keys are stable English field names; values follow VLC's locale.</summary>
public sealed record VLCCodecStreamInformation(int Index, IReadOnlyDictionary<string, string> Fields);

internal static class VLCCodecInformation
{
    // es_out.c assigns monotonically increasing metadata IDs independently of track IDs.
    // Bound the scan; do not stop at gaps left by deleted streams. This is an on-demand read.
    internal const int StreamIdentifierLimit = 1024;
    private static readonly string[] Fields = ["Type", "Language", "Description", "Original ID", "Bitrate",
        "Video resolution", "Buffer dimensions", "Frame rate", "Orientation", "Color primaries",
        "Color transfer function", "Color space", "Chroma location", "Projection", "Channels",
        "Sample rate", "Bits per sample", "Decoded format", "Decoded channels", "Decoded sample rate",
        "Decoded bits per sample", "Decoded Bitrate", "Track replay gain", "Album replay gain"];

    internal static IReadOnlyList<VLCCodecStreamInformation> Read(IVLCPlayerNative native, nint item)
    {
        string categoryFormat = native.Translate("Stream %d");
        string codecKey = native.Translate("Codec");
        var translatedFields = Fields.Select(key => (Key: key, NativeKey: native.Translate(key))).ToArray();
        List<VLCCodecStreamInformation> streams = [];
        for (int index = 0; index < StreamIdentifierLimit; index++)
        {
            string number = index.ToString(CultureInfo.InvariantCulture);
            string category = categoryFormat.Replace("%1$d", number, StringComparison.Ordinal).Replace("%d", number, StringComparison.Ordinal);
            string codec = ReadOwned(native, item, category, codecKey);
            if (codec.Length == 0) continue;
            Dictionary<string, string> values = new(StringComparer.Ordinal) { ["Codec"] = codec };
            foreach (var field in translatedFields)
            {
                string value = ReadOwned(native, item, category, field.NativeKey);
                if (value.Length != 0) values.Add(field.Key, value);
            }
            streams.Add(new(index, new ReadOnlyDictionary<string, string>(values)));
        }
        return streams.AsReadOnly();
    }

    private static string ReadOwned(IVLCPlayerNative native, nint item, string category, string field)
    {
        // input_item_GetInfo locks the item and returns strdup, including for a missing key.
        nint pointer = native.InputItemGetInfo(item, category, field);
        if (pointer == nint.Zero) return string.Empty;
        try { string value = native.Utf8Owned(pointer) ?? string.Empty; return value.Length <= 4096 ? value : value[..4096]; }
        finally { native.Free(pointer); }
    }
}
