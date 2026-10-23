using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VLCLR;
using VLCLR.Module;
using VLCLR.Plugin;
using Interop = VLCLR.Native.Interop;

namespace McpRemote;

/// <summary>
/// An interface module: while VLC runs, it serves VLC's player to AI agents
/// over the Model Context Protocol, on 127.0.0.1 only.
/// </summary>
public static unsafe class PluginEntry
{
    private static readonly nint ApiVersion = Marshal.StringToCoTaskMemUTF8(VLCApi.VLC_API_VERSION_STRING);
    private static readonly nint Copyright = Marshal.StringToCoTaskMemUTF8("VLCLR");

    [UnmanagedCallersOnly(EntryPoint = "vlc_entry_api_version", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* VlcEntryApiVersion() => (byte*)ApiVersion;

    [UnmanagedCallersOnly(EntryPoint = "vlc_entry_copyright", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* VlcEntryCopyright() => (byte*)Copyright;

    [UnmanagedCallersOnly(EntryPoint = "vlc_entry", CallConvs = [typeof(CallConvCdecl)])]
    public static int VlcEntry(nint vlcSetPtr, nint opaque)
        => ModuleBuilder.Create(vlcSetPtr, opaque)
            .WithName("dotnet_mcp")
            .WithShortName(".NET MCP server")
            .WithDescription("Lets AI agents control playback over the Model Context Protocol")
            .WithCapability("interface")
            .WithScore(0)
            .WithNoUnload()
            .WithSubcategory(VLCConfigSubcategory.SUBCAT_INTERFACE_CONTROL)
            .AddIntegerConfig("mcp-port", McpServerHost.DefaultPort, 1, 65535, "Port",
                "Port of the MCP server, which listens on 127.0.0.1 only")
            .WithOpenCallback(&Open)
            .WithCloseCallback(&Close)
            .Register();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Open(nint intf)
    {
        var logger = new VLCLogger(intf);
        try
        {
            VLCPlayer? player = VLCPlayer.Create(intf, logger);
            VLCPlaylist? playlist = VLCPlaylist.Create(intf, logger);
            if (player == null || playlist == null)
                return -1;

            int port = (int)new VLCConfiguration(intf).GetInteger("mcp-port", McpServerHost.DefaultPort);
            McpServerHost host = McpServerHost.Start(player, playlist, port, logger);
            ((Interop.IntfThread*)intf)->Sys = (Interop.IntfSys*)GCHandle.ToIntPtr(GCHandle.Alloc(host));
            return 0;
        }
        catch (Exception ex)
        {
            logger.Error($"[McpRemote] Cannot start the MCP server: {ex.Message}");
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Close(nint intf)
    {
        var thread = (Interop.IntfThread*)intf;
        if (thread->Sys == null)
            return;

        GCHandle handle = GCHandle.FromIntPtr((nint)thread->Sys);
        thread->Sys = null;
        try
        {
            ((McpServerHost)handle.Target!).Dispose();
        }
        catch (Exception ex)
        {
            new VLCLogger(intf).Error($"[McpRemote] Cannot stop the MCP server: {ex.Message}");
        }
        finally
        {
            handle.Free();
        }
    }
}
