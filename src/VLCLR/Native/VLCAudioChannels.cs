// VLC audio channel masks and interleaving order
// Source: vlc/include/vlc_es.h (AOUT_CHAN_*) and vlc_aout.h (pi_vlc_chan_order_wg4)
// VLC Version: 4.0.6

using System.Numerics;

namespace VLCLR.Native;

/// <summary>
/// VLC's audio channel bits (<c>i_physical_channels</c>) and the order in which
/// the channels of a mask are interleaved in a decoded block.
/// </summary>
public static class VLCAudioChannels
{
    public const ushort Left = Interop.VlcEs.AoutChanLeft;
    public const ushort Right = Interop.VlcEs.AoutChanRight;
    public const ushort Center = Interop.VlcEs.AoutChanCenter;
    public const ushort MiddleLeft = Interop.VlcEs.AoutChanMiddleleft;
    public const ushort MiddleRight = Interop.VlcEs.AoutChanMiddleright;
    public const ushort RearLeft = Interop.VlcEs.AoutChanRearleft;
    public const ushort RearRight = Interop.VlcEs.AoutChanRearright;
    public const ushort RearCenter = Interop.VlcEs.AoutChanRearcenter;
    public const ushort Lfe = Interop.VlcEs.AoutChanLfe;

    /// <summary>AOUT_CHANS_STEREO</summary>
    public const ushort Stereo = Interop.VlcEs.AoutChansStereo;

    /// <summary>AOUT_CHANS_5_1: L R RL RR C LFE once interleaved.</summary>
    public const ushort Surround51 = Interop.VlcEs.AoutChans51;

    /// <summary>
    /// The order VLC interleaves channels in (<c>pi_vlc_chan_order_wg4</c>):
    /// the channels of a mask appear in this order, absent ones skipped.
    /// </summary>
    public static ReadOnlySpan<ushort> Order =>
    [
        Left, Right, MiddleLeft, MiddleRight, RearLeft, RearRight, RearCenter, Center, Lfe,
    ];

    /// <summary>Number of channels in a mask (<c>aout_FormatNbChannels</c>).</summary>
    public static int Count(ushort mask) => BitOperations.PopCount((uint)(mask & AllChannels));

    /// <summary>
    /// Position of <paramref name="channel"/> in a frame of a block whose
    /// channels are <paramref name="mask"/>, or -1 when the mask lacks it.
    /// </summary>
    public static int IndexOf(ushort mask, ushort channel)
    {
        if ((mask & channel) == 0)
            return -1;

        int index = 0;
        foreach (ushort candidate in Order)
        {
            if (candidate == channel)
                return index;
            if ((mask & candidate) != 0)
                index++;
        }
        return -1;
    }

    private const int AllChannels = Left | Right | Center | MiddleLeft | MiddleRight
        | RearLeft | RearRight | RearCenter | Lfe;
}
