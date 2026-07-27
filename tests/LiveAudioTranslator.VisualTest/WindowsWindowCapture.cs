using System.Diagnostics;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LiveAudioTranslator.VisualTest;

internal static class WindowsWindowCapture
{
    private const uint Th32csSnapProcess = 0x00000002;
    private const int Srccopy = 0x00CC0020;
    private const int Captureblt = 0x40000000;
    private const uint PwRenderFullContent = 0x00000002;
    private const uint WsPopup = 0x80000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const uint DwmTnpRectDestination = 0x00000001;
    private const uint DwmTnpOpacity = 0x00000004;
    private const uint DwmTnpVisible = 0x00000008;
    private const uint DibRgbColors = 0;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndTopmost = -1;
    private static readonly nint HwndNotTopmost = -2;

    public static async Task<(nint Handle, QtWindowMetadata Metadata)> WaitForVlcWindowAsync(
        int launchProcessId,
        TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            HashSet<int> processIds = GetDescendantsAndSelf(launchProcessId);
            nint window = FindTopLevelWindow(processIds);
            if (window != 0)
            {
                PromoteForCapture(window);
                await Task.Delay(300);
                QtWindowMetadata metadata = GetMetadata(window);
                if (metadata.Visible && !metadata.Minimized &&
                    metadata.ClientWidth > 0 && metadata.ClientHeight > 0 &&
                    metadata.Unobscured)
                {
                    return (window, metadata);
                }
                RestoreNormalZOrder(window);
            }
            await Task.Delay(100);
        }
        throw new TimeoutException(
            $"No visible VLC Qt window appeared for launch PID {launchProcessId}.");
    }

    public static async Task<QtWindowMetadata> CaptureAsync(
        nint window,
        string outputPath,
        TimeSpan timeout)
    {
        try
        {
            QtWindowMetadata metadata = GetMetadata(window);
            for (int attempt = 0; attempt < 20; attempt++)
            {
                PromoteForCapture(window);
                await Task.Delay(100);
                metadata = GetMetadata(window);
                if (metadata.Visible && !metadata.Minimized)
                    break;
            }
            await Task.Delay(500);
            PromoteForCapture(window);
            await Task.Delay(100);
            metadata = GetMetadata(window);
            if (!metadata.Visible || metadata.Minimized || !metadata.Unobscured)
            {
                throw new InvalidOperationException(
                    $"VLC window is not capturable: visible={metadata.Visible}, " +
                    $"minimized={metadata.Minimized}, unobscured={metadata.Unobscured}.");
            }

            CaptureScreenRegion(
                metadata.ClientX,
                metadata.ClientY,
                metadata.ClientWidth,
                metadata.ClientHeight,
                outputPath);
            return metadata with { CaptureMethod = "BitBlt(CAPTUREBLT)" };
        }
        finally
        {
            RestoreNormalZOrder(window);
        }
    }

    public static unsafe QtWindowMetadata CaptureWithPrintWindow(
        nint window,
        string outputPath)
    {
        PromoteForCapture(window);
        Thread.Sleep(250);
        try
        {
            QtWindowMetadata metadata = GetMetadata(window);
            if (!metadata.Visible || metadata.Minimized)
            {
                throw new InvalidOperationException(
                    $"VLC window is not printable: visible={metadata.Visible}, " +
                    $"minimized={metadata.Minimized}.");
            }
            if (!GetWindowRect(window, out Rect bounds))
                throw new InvalidOperationException("GetWindowRect failed for the VLC window.");

            int width = bounds.Right - bounds.Left;
            int height = bounds.Bottom - bounds.Top;
            nint screen = GetDC(0);
            nint memory = CreateCompatibleDC(screen);
            nint bitmap = CreateCompatibleBitmap(screen, width, height);
            nint previous = SelectObject(memory, bitmap);
            try
            {
                if (!PrintWindow(window, memory, PwRenderFullContent))
                {
                    throw new InvalidOperationException(
                        "PrintWindow failed while capturing the VLC window.");
                }

                var info = new BitmapInfo
                {
                    Header = new BitmapInfoHeader
                    {
                        Size = (uint)sizeof(BitmapInfoHeader),
                        Width = width,
                        Height = -height,
                        Planes = 1,
                        BitCount = 32,
                        Compression = 0
                    }
                };
                var pixels = new byte[checked(width * height * 4)];
                fixed (byte* data = pixels)
                {
                    if (GetDIBits(
                            memory,
                            bitmap,
                            0,
                            (uint)height,
                            data,
                            ref info,
                            DibRgbColors) == 0)
                    {
                        throw new InvalidOperationException(
                            "GetDIBits failed for the PrintWindow capture.");
                    }
                }
                using Image<Bgra32> image = Image.LoadPixelData<Bgra32>(
                    pixels,
                    width,
                    height);
                image.SaveAsPng(outputPath);
                return metadata with { CaptureMethod = "PrintWindow(PW_RENDERFULLCONTENT)" };
            }
            finally
            {
                _ = SelectObject(memory, previous);
                _ = DeleteObject(bitmap);
                _ = DeleteDC(memory);
                _ = ReleaseDC(0, screen);
            }
        }
        finally
        {
            RestoreNormalZOrder(window);
        }
    }

    public static QtWindowMetadata CaptureWithDwmThumbnail(
        nint sourceWindow,
        string outputPath)
    {
        PromoteForCapture(sourceWindow);
        Thread.Sleep(250);
        QtWindowMetadata metadata = GetMetadata(sourceWindow);
        if (!metadata.Visible || metadata.Minimized)
        {
            throw new InvalidOperationException(
                $"VLC window is not available for a DWM thumbnail: " +
                $"visible={metadata.Visible}, minimized={metadata.Minimized}.");
        }

        int screenWidth = GetSystemMetrics(0);
        int screenHeight = GetSystemMetrics(1);
        _ = GetWindowRect(sourceWindow, out Rect sourceBounds);
        var sourceSize = new Size(
            Math.Max(1, sourceBounds.Right - sourceBounds.Left),
            Math.Max(1, sourceBounds.Bottom - sourceBounds.Top));
        double scale = Math.Min(
            (double)screenWidth / sourceSize.Width,
            (double)screenHeight / sourceSize.Height);
        int width = Math.Max(1, (int)Math.Floor(sourceSize.Width * scale));
        int height = Math.Max(1, (int)Math.Floor(sourceSize.Height * scale));

        nint mirror = CreateWindowEx(
            WsExTopmost | WsExToolWindow | WsExNoActivate,
            "STATIC",
            "VLCLR DWM capture mirror",
            WsPopup | WsVisible,
            0,
            0,
            width,
            height,
            0,
            0,
            0,
            0);
        if (mirror == 0)
            throw new InvalidOperationException("Could not create the DWM capture mirror window.");

        nint thumbnail = 0;
        try
        {
            int registerResult = DwmRegisterThumbnail(mirror, sourceWindow, out thumbnail);
            if (registerResult < 0)
                Marshal.ThrowExceptionForHR(registerResult);

            var properties = new DwmThumbnailProperties
            {
                Flags = DwmTnpRectDestination | DwmTnpOpacity | DwmTnpVisible,
                Destination = new Rect { Left = 0, Top = 0, Right = width, Bottom = height },
                Opacity = 255,
                Visible = 1
            };
            int updateResult = DwmUpdateThumbnailProperties(thumbnail, ref properties);
            if (updateResult < 0)
                Marshal.ThrowExceptionForHR(updateResult);

            _ = ShowWindow(mirror, 5);
            _ = SetWindowPos(
                mirror,
                HwndTopmost,
                0,
                0,
                width,
                height,
                SwpShowWindow);
            _ = UpdateWindow(mirror);
            PumpWindowMessages();
            _ = DwmFlush();
            Thread.Sleep(750);
            PumpWindowMessages();
            CaptureScreenRegion(0, 0, width, height, outputPath);
            return metadata with { CaptureMethod = "DWM thumbnail + BitBlt" };
        }
        finally
        {
            if (thumbnail != 0)
                _ = DwmUnregisterThumbnail(thumbnail);
            _ = DestroyWindow(mirror);
            RestoreNormalZOrder(sourceWindow);
        }
    }

    private static unsafe void CaptureScreenRegion(
        int x,
        int y,
        int width,
        int height,
        string outputPath)
    {
        nint screen = GetDC(0);
        nint memory = CreateCompatibleDC(screen);
        nint bitmap = CreateCompatibleBitmap(screen, width, height);
        nint previous = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(
                    memory,
                    0,
                    0,
                    width,
                    height,
                    screen,
                    x,
                    y,
                    Srccopy | Captureblt))
            {
                throw new InvalidOperationException(
                    "BitBlt failed while capturing the DWM thumbnail.");
            }
            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)sizeof(BitmapInfoHeader),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0
                }
            };
            var pixels = new byte[checked(width * height * 4)];
            fixed (byte* data = pixels)
            {
                if (GetDIBits(
                        memory,
                        bitmap,
                        0,
                        (uint)height,
                        data,
                        ref info,
                        DibRgbColors) == 0)
                {
                    throw new InvalidOperationException(
                        "GetDIBits failed for the DWM thumbnail.");
                }
            }
            using Image<Bgra32> image = Image.LoadPixelData<Bgra32>(
                pixels,
                width,
                height);
            image.SaveAsPng(outputPath);
        }
        finally
        {
            _ = SelectObject(memory, previous);
            _ = DeleteObject(bitmap);
            _ = DeleteDC(memory);
            _ = ReleaseDC(0, screen);
        }
    }

    private static void PumpWindowMessages()
    {
        while (PeekMessage(out Message message, 0, 0, 0, 1))
        {
            _ = TranslateMessage(ref message);
            _ = DispatchMessage(ref message);
        }
    }

    private static void RestoreNormalZOrder(nint window) =>
        _ = SetWindowPos(
            window,
            HwndNotTopmost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpShowWindow);

    private static void PromoteForCapture(nint window)
    {
        _ = ShowWindow(window, 9);
        _ = ShowWindowAsync(window, 9);
        nint foreground = GetForegroundWindow();
        uint currentThread = GetCurrentThreadId();
        uint foregroundThread = foreground == 0
            ? 0
            : GetWindowThreadProcessId(foreground, out _);
        bool attached = foregroundThread != 0 &&
            foregroundThread != currentThread &&
            AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            _ = SetWindowPos(
                window,
                HwndTopmost,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpShowWindow);
            _ = BringWindowToTop(window);
            _ = SetForegroundWindow(window);
        }
        finally
        {
            if (attached)
                _ = AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    private static QtWindowMetadata GetMetadata(nint window)
    {
        _ = GetWindowThreadProcessId(window, out uint processId);
        _ = GetClientRect(window, out Rect client);
        var origin = new Point();
        _ = ClientToScreen(window, ref origin);
        int width = client.Right - client.Left;
        int height = client.Bottom - client.Top;
        return new QtWindowMetadata(
            window,
            (int)processId,
            GetWindowTitle(window),
            origin.X,
            origin.Y,
            width,
            height,
            IsWindowVisible(window),
            IsIconic(window),
            IsUnobscured(window, processId, origin, width, height));
    }

    private static bool IsUnobscured(
        nint window,
        uint processId,
        Point origin,
        int width,
        int height)
    {
        if (width <= 0 || height <= 0)
            return false;
        Point[] points =
        [
            new(origin.X + width / 2, origin.Y + height / 2),
            new(origin.X + width / 4, origin.Y + height / 4),
            new(origin.X + width * 3 / 4, origin.Y + height / 4),
            new(origin.X + width / 4, origin.Y + height * 3 / 4),
            new(origin.X + width * 3 / 4, origin.Y + height * 3 / 4)
        ];
        foreach (Point point in points)
        {
            nint covering = WindowFromPoint(point);
            if (covering == 0)
                return false;
            _ = GetWindowThreadProcessId(covering, out uint coveringProcess);
            if (coveringProcess != processId)
                return false;
        }
        return true;
    }

    private static nint FindTopLevelWindow(HashSet<int> processIds)
    {
        nint found = 0;
        _ = EnumWindows((window, parameter) =>
        {
            _ = GetWindowThreadProcessId(window, out uint processId);
            if (processIds.Contains((int)processId) && IsWindowVisible(window) && !IsIconic(window))
            {
                _ = GetClientRect(window, out Rect rect);
                if (rect.Right - rect.Left >= 320 && rect.Bottom - rect.Top >= 200)
                {
                    found = window;
                    return false;
                }
            }
            return true;
        }, 0);
        return found;
    }

    private static HashSet<int> GetDescendantsAndSelf(int root)
    {
        var parentByChild = new Dictionary<int, int>();
        nint snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == -1)
            return [root];
        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (Process32First(snapshot, ref entry))
            {
                do
                {
                    parentByChild[(int)entry.ProcessId] = (int)entry.ParentProcessId;
                    entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
                }
                while (Process32Next(snapshot, ref entry));
            }
        }
        finally
        {
            _ = CloseHandle(snapshot);
        }

        var result = new HashSet<int> { root };
        bool changed;
        do
        {
            changed = false;
            foreach ((int child, int parent) in parentByChild)
            {
                if (result.Contains(parent) && result.Add(child))
                    changed = true;
            }
        }
        while (changed);
        return result;
    }

    private static string GetWindowTitle(nint window)
    {
        int length = GetWindowTextLength(window);
        var text = new char[length + 1];
        _ = GetWindowText(window, text, text.Length);
        return new string(text, 0, length);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
        public Point(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int Width, Height;
        public Size(int width, int height) { Width = width; Height = height; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmThumbnailProperties
    {
        public uint Flags;
        public Rect Destination;
        public Rect Source;
        public byte Opacity;
        public int Visible;
        public int SourceClientAreaOnly;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Value;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public Point Position;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size, Usage, ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int PriorityClass;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [DllImport("user32")] private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32")] private static extern bool IsIconic(nint window);
    [DllImport("user32")] private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32")] private static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32")] private static extern bool BringWindowToTop(nint window);
    [DllImport("user32")] private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32")] private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint extendedStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32")] private static extern bool UpdateWindow(nint window);
    [DllImport("user32")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32")] private static extern bool PeekMessage(out Message message, nint window, uint minimum, uint maximum, uint remove);
    [DllImport("user32")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32")] private static extern nint GetForegroundWindow();
    [DllImport("user32")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, char[] text, int maximum);
    [DllImport("user32")] private static extern int GetWindowTextLength(nint window);
    [DllImport("user32")] private static extern nint GetDC(nint window);
    [DllImport("user32")] private static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("user32")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32")] private static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, int operation);
    [DllImport("gdi32")] private static extern unsafe int GetDIBits(nint dc, nint bitmap, uint start, uint lines, void* data, ref BitmapInfo info, uint usage);
    [DllImport("kernel32")] private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32")] private static extern bool CloseHandle(nint handle);
    [DllImport("dwmapi")] private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi")] private static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("dwmapi")] private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref DwmThumbnailProperties properties);
    [DllImport("dwmapi")] private static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out Size size);
    [DllImport("dwmapi")] private static extern int DwmFlush();
}
