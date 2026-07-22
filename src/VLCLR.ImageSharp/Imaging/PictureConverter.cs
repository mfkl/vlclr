// Converts ImageSharp images to VLC pictures and subpicture regions
// Used to return rendered images to VLC for compositing

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using VLCLR.Native;

namespace VLCLR.Imaging;

/// <summary>
/// Converts rendered ImageSharp images to VLC picture structures.
/// Creates subpicture regions suitable for returning from text renderers or video filters.
/// </summary>
public static class PictureConverter
{
    /// <summary>
    /// Converts an ImageSharp image to a VLC subpicture region.
    /// </summary>
    /// <param name="image">The rendered image (RGBA format).</param>
    /// <param name="chromaListPtr">Pointer to null-terminated array of supported chromas from VLC.</param>
    /// <returns>Pointer to allocated subpicture_region_t, or nint.Zero on failure.</returns>
    /// <remarks>
    /// The returned region and its picture are owned by VLC after this call.
    /// VLC will free them when no longer needed.
    /// </remarks>
    public static unsafe nint ToSubpictureRegion(Image<Rgba32> image, nint chromaListPtr)
    {
        if (image == null)
        {
            return nint.Zero;
        }

        int width = image.Width;
        int height = image.Height;

        if (width <= 0 || height <= 0)
        {
            return nint.Zero;
        }

        // Determine which chroma to use
        // Check if RGBA is in the supported chroma list, otherwise try BGRA
        uint chroma = SelectChroma(chromaListPtr);
        if (chroma == 0)
        {
            Console.Error.WriteLine("[VLCLR] PictureConverter: No supported chroma format found");
            return nint.Zero;
        }

        // Create video format for the picture
        VLCVideoFormat format = CreateFormat(chroma, (uint)width, (uint)height);

        // Allocate format on stack and get pointer
        nint formatPtr = (nint)Unsafe.AsPointer(ref format);

        // Create picture from format
        nint picturePtr = VLCCore.PictureNewFromFormat(formatPtr);
        if (picturePtr == nint.Zero)
        {
            Console.Error.WriteLine("[VLCLR] PictureConverter: Failed to create picture");
            return nint.Zero;
        }

        // Copy pixels to picture
        bool copySuccess = CopyPixelsToPicture(image, picturePtr, chroma);
        if (!copySuccess)
        {
            Console.Error.WriteLine("[VLCLR] PictureConverter: Failed to copy pixels to picture");
            VLCCore.PictureDestroy(picturePtr);
            return nint.Zero;
        }

        // Create subpicture region from picture
        // Note: subpicture_region_ForPicture adds a reference to the picture
        nint regionPtr = VLCCore.SubpictureRegionForPicture(picturePtr);
        if (regionPtr == nint.Zero)
        {
            Console.Error.WriteLine("[VLCLR] PictureConverter: Failed to create subpicture region");
            VLCCore.PictureDestroy(picturePtr);
            return nint.Zero;
        }

        // Set region position - required or VLC will assert fail with INT_MAX
        // Position is relative to alignment point (0,0 = aligned position)
        ref VLCSubpictureRegion region = ref Unsafe.AsRef<VLCSubpictureRegion>((void*)regionPtr);
        region.X = 0;
        region.Y = 0;
        region.Align = VLCSubpictureAlign.Bottom; // Default to bottom center
        region.Alpha = 255; // Fully opaque - IMPORTANT or subtitle is invisible!

        return regionPtr;
    }

    /// <summary>
    /// Selects a suitable chroma format from the VLC-provided list.
    /// Prefers RGBA, falls back to BGRA.
    /// </summary>
    /// <param name="chromaListPtr">Pointer to null-terminated array of vlc_fourcc_t values.</param>
    /// <returns>The selected chroma FourCC, or 0 if no suitable format found.</returns>
    public static unsafe uint SelectChroma(nint chromaListPtr)
    {
        if (chromaListPtr == nint.Zero)
        {
            // Default to RGBA if no list provided
            return VLCFourCC.RGBA;
        }

        // The chroma list is a null-terminated array of vlc_fourcc_t (uint)
        uint* chromaList = (uint*)chromaListPtr;

        bool hasRgba = false;
        bool hasBgra = false;

        // Scan the list for supported formats
        for (int i = 0; chromaList[i] != 0; i++)
        {
            uint chroma = chromaList[i];
            if (chroma == VLCFourCC.RGBA)
            {
                hasRgba = true;
            }
            else if (chroma == VLCFourCC.BGRA)
            {
                hasBgra = true;
            }
        }

        // Prefer RGBA (native ImageSharp format)
        if (hasRgba)
        {
            return VLCFourCC.RGBA;
        }

        // Fall back to BGRA
        if (hasBgra)
        {
            return VLCFourCC.BGRA;
        }

        // Default to RGBA and hope VLC can handle it
        return VLCFourCC.RGBA;
    }

    /// <summary>
    /// Creates a VLCVideoFormat structure for the given chroma and dimensions.
    /// </summary>
    /// <param name="chroma">The FourCC chroma code.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>A configured VLCVideoFormat structure.</returns>
    public static VLCVideoFormat CreateFormat(uint chroma, uint width, uint height)
    {
        return new VLCVideoFormat
        {
            Chroma = chroma,
            Width = width,
            Height = height,
            XOffset = 0,
            YOffset = 0,
            VisibleWidth = width,
            VisibleHeight = height,
            SarNum = 1,
            SarDen = 1,
            FrameRate = 0,
            FrameRateBase = 1,
            Palette = nint.Zero,
            Orientation = 0,
            Primaries = 0,
            Transfer = 0,
            Space = 0,
            ColorRange = 0,
            ChromaLocation = 0,
            MultiviewMode = 0,
            MultiviewRightEyeFirst = 0,
            ProjectionMode = 0,
            PoseYaw = 0,
            PosePitch = 0,
            PoseRoll = 0,
            PoseFov = 0
        };
    }

    /// <summary>
    /// Copies pixels from an ImageSharp image to a VLC picture.
    /// </summary>
    /// <param name="image">Source ImageSharp image in RGBA format.</param>
    /// <param name="picturePtr">Pointer to the destination VLC picture.</param>
    /// <param name="chroma">Target chroma format (RGBA or BGRA).</param>
    /// <returns>True if copy succeeded, false otherwise.</returns>
    public static unsafe bool CopyPixelsToPicture(Image<Rgba32> image, nint picturePtr, uint chroma)
    {
        ref VLCPicture picture = ref Unsafe.AsRef<VLCPicture>((void*)picturePtr);

        if (picture.PlaneCount == 0)
        {
            return false;
        }

        VLCPlane plane = picture.Plane0;
        if (plane.Pixels == nint.Zero)
        {
            return false;
        }

        int width = image.Width;
        int height = image.Height;
        int pitch = plane.Pitch;
        int visiblePitch = plane.VisiblePitch;
        bool needSwizzle = (chroma == VLCFourCC.BGRA);

        // Get pixel data from ImageSharp
        byte[] pixelData = new byte[width * height * 4];
        image.CopyPixelDataTo(pixelData);

        byte* dstPtr = (byte*)plane.Pixels;

        // Copy row by row, respecting pitch
        for (int y = 0; y < height; y++)
        {
            int srcOffset = y * width * 4;
            byte* dstRow = dstPtr + (y * pitch);

            if (needSwizzle)
            {
                // Convert RGBA to BGRA
                for (int x = 0; x < width; x++)
                {
                    int srcIdx = srcOffset + (x * 4);
                    int dstIdx = x * 4;

                    // RGBA -> BGRA (swap R and B)
                    dstRow[dstIdx + 0] = pixelData[srcIdx + 2]; // B from R
                    dstRow[dstIdx + 1] = pixelData[srcIdx + 1]; // G
                    dstRow[dstIdx + 2] = pixelData[srcIdx + 0]; // R from B
                    dstRow[dstIdx + 3] = pixelData[srcIdx + 3]; // A
                }
            }
            else
            {
                // Direct copy for RGBA
                int copyBytes = Math.Min(width * 4, visiblePitch);
                fixed (byte* srcRow = &pixelData[srcOffset])
                {
                    Buffer.MemoryCopy(srcRow, dstRow, copyBytes, copyBytes);
                }
            }
        }

        return true;
    }
}
