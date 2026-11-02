// Port of VLC's gain filter (modules/audio_filter/gain.c), which multiplies
// every sample through the "audio volume" module; for 32-bit float that is
// modules/audio_mixer/float.c, FilterFL32. Each output sample is one float
// multiplication, so the vector loop gives the same bits as the C.
//
// LGPL-2.1-or-later, like the VLC source it is ported from.

using System.Numerics;
using System.Runtime.InteropServices;

namespace VLCLR.Filters.Audio;

/// <summary>VLC's gain filter: every sample times one multiplier.</summary>
public static class Gain
{
    /// <summary>
    /// Multiplies every sample by <paramref name="multiplier"/>, in place.
    /// A multiplier of 1 leaves the samples untouched, as in VLC.
    /// </summary>
    public static void Amplify(Span<float> samples, float multiplier)
    {
        if (multiplier == 1f)
            return;

        int i = 0;
        if (Vector.IsHardwareAccelerated && samples.Length >= Vector<float>.Count)
        {
            var vectors = MemoryMarshal.Cast<float, Vector<float>>(samples);
            var factor = new Vector<float>(multiplier);
            for (int v = 0; v < vectors.Length; v++)
                vectors[v] *= factor;
            i = vectors.Length * Vector<float>.Count;
        }
        for (; i < samples.Length; i++)
            samples[i] *= multiplier;
    }
}
