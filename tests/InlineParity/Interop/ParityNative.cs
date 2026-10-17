using System.Runtime.InteropServices;
using System.Text;

namespace InlineParity.Native;

/// <summary>Mirror of <c>parity_log</c> in native/inline_parity.c (checked by LayoutTests).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ParityLog
{
    public int SetCalls;
    public int SetType;
    public long SetInt;
    public int SetStrIsNull;
    public fixed byte SetName[128];
    public fixed byte SetStr[512];
    public nint SetObj;

    public int GetCalls;
    public int GetVarType;
    public fixed byte GetName[128];
    public nint GetObj;

    public int InheritCalls;
    public int InheritType;
    public fixed byte InheritName[128];
    public nint InheritObj;

    public int CreateCalls;
    public int CreateType;
    public fixed byte CreateName[128];
    public nint CreateObj;

    public int DestroyCalls;
    public nint DestroyArg;

    public int NewFmtCalls;
    public nint NewFmtArg;

    public int LogCalls;
    public int LogType;
    public nint LogObj;
    public fixed byte LogMessage[512];

    public int BufferNewCalls;
    public nint BufferNewArg;

    public int CapsCalls;
    public nint CapsArg;
    public int LockCalls;
    public int UnlockCalls;

    public int SeekTimeCalls;
    public long SeekTime;
    public int SeekSpeed;
    public int SeekWhence;

    public int ParentCalls;
    public int TypenameCalls;
}

/// <summary>Mirror of <c>parity_script</c> in native/inline_parity.c.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ParityScript
{
    public int SetResult;
    public int GetResult;
    public long GetValue;
    public int GetStringIsNull;
    public fixed byte GetString[512];
    public int InheritResult;
    public long InheritValue;
    public int InheritStringIsNull;
    public fixed byte InheritString[512];
    public int CreateResult;
    public int PnffNull;
    public int Capabilities;
    public long PlayerTime;
    public long PlayerLength;

    public void SetGetString(string? value)
    {
        GetStringIsNull = value is null ? 1 : 0;
        fixed (byte* p = GetString) { ParityNative.WriteCString(p, 512, value ?? ""); }
    }

    public void SetInheritString(string? value)
    {
        InheritStringIsNull = value is null ? 1 : 0;
        fixed (byte* p = InheritString) { ParityNative.WriteCString(p, 512, value ?? ""); }
    }
}

/// <summary>Everything the stand-ins recorded, as a value that compares with ==.</summary>
internal sealed record LogSnapshot(
    int SetCalls, int SetType, long SetInt, int SetStrIsNull, string SetName, string SetStr, nint SetObj,
    int GetCalls, int GetVarType, string GetName, nint GetObj,
    int InheritCalls, int InheritType, string InheritName, nint InheritObj,
    int CreateCalls, int CreateType, string CreateName, nint CreateObj,
    int DestroyCalls, nint DestroyArg,
    int NewFmtCalls, nint NewFmtArg,
    int LogCalls, int LogType, nint LogObj, string LogMessage,
    int BufferNewCalls, nint BufferNewArg,
    int CapsCalls, nint CapsArg,
    int SeekTimeCalls, long SeekTime, int SeekSpeed, int SeekWhence,
    int ParentCalls, int TypenameCalls)
{
    // Lock and unlock are left out on purpose: the C inlines expect the caller to
    // hold the player lock, the managed VLCPlayer takes it itself.
}

internal static unsafe partial class ParityNative
{
    private const string Lib = ParityLibrary.Name;

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ParityLog* parity_log_ptr();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ParityScript* parity_script_ptr();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void parity_reset();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int parity_log_size();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int parity_script_size();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nint parity_object_new(nint parent, nint typeName);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void parity_object_free(nint obj);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nint parity_vlc_object_instance(nint obj);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void parity_vlc_fourcc_to_char(uint fcc, byte* out4);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nint parity_picture_new(nuint refs);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nuint parity_picture_refs(nint picture);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void parity_picture_set_refs(nint picture, nuint refs);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void parity_picture_free(nint picture);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void parity_picture_Release(nint picture);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nint parity_fake_pnff_picture();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nint parity_fake_buffer_picture();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nint parity_filter_new(int mode);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void parity_filter_free(nint filter);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nint parity_filter_fmt_out_video(nint filter);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nint parity_filter_NewPicture(nint filter);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int parity_var_SetInteger(nint obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, long value);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern long parity_var_GetInteger(nint obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int parity_var_SetString(nint obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string? value);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nint parity_var_GetString(nint obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int parity_var_Create(nint obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int type);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int parity_create_integer(nint obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, long initial);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int parity_create_string(nint obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string? initial);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void parity_free(nint ptr);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int parity_var_InheritBool(nint obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern long parity_var_InheritInteger(nint obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern float parity_var_InheritFloat(nint obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nint parity_var_InheritString(nint obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nint parity_fake_player();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern nint parity_fake_intf();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int parity_vlc_player_CanSeek(nint player);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int parity_vlc_player_CanPause(nint player);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int parity_VLC_PLAYER_CAP_SEEK();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int parity_VLC_PLAYER_CAP_PAUSE();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int parity_VLC_MSG_WARN();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern long parity_VLC_TICK_FROM_US(long v);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern long parity_US_FROM_VLC_TICK(long v);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern long parity_VLC_TICK_FROM_MS(long v);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern long parity_MS_FROM_VLC_TICK(long v);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern long parity_VLC_TICK_FROM_SEC(long v);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern long parity_SEC_FROM_VLC_TICK(long v);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern long parity_VLC_TICK_FROM_NS(long v);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern long parity_NS_FROM_VLC_TICK(long v);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern long parity_CLOCK_FREQ();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern long parity_VLC_TICK_INVALID();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern long parity_VLC_TICK_0();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint parity_vlc_fourcc_GetChromaBPP(uint fcc);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int parity_chroma_pixel_size(uint fcc);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int parity_codec_count();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern nint parity_codec_name(int index);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint parity_codec_value(int index);

    public static string CodecName(int index) => Marshal.PtrToStringUTF8(parity_codec_name(index))!;

    // ---- Helpers shared by the tests -------------------------------------

    public static void WriteCString(byte* destination, int capacity, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        int length = Math.Min(bytes.Length, capacity - 1);
        for (int i = 0; i < length; i++)
        {
            destination[i] = bytes[i];
        }

        destination[length] = 0;
    }

    public static string ReadCString(byte* source) => Marshal.PtrToStringUTF8((nint)source) ?? "";

    /// <summary>Resets the stand-ins and applies <paramref name="script"/> to the scripted results.</summary>
    public static void Reset(ScriptSetup? script = null)
    {
        parity_reset();
        script?.Invoke(parity_script_ptr());
    }

    public delegate void ScriptSetup(ParityScript* script);

    public static LogSnapshot Snapshot()
    {
        ParityLog* l = parity_log_ptr();
        return new LogSnapshot(
            l->SetCalls, l->SetType, l->SetInt, l->SetStrIsNull, ReadCString(l->SetName), ReadCString(l->SetStr), l->SetObj,
            l->GetCalls, l->GetVarType, ReadCString(l->GetName), l->GetObj,
            l->InheritCalls, l->InheritType, ReadCString(l->InheritName), l->InheritObj,
            l->CreateCalls, l->CreateType, ReadCString(l->CreateName), l->CreateObj,
            l->DestroyCalls, l->DestroyArg,
            l->NewFmtCalls, l->NewFmtArg,
            l->LogCalls, l->LogType, l->LogObj, ReadCString(l->LogMessage),
            l->BufferNewCalls, l->BufferNewArg,
            l->CapsCalls, l->CapsArg,
            l->SeekTimeCalls, l->SeekTime, l->SeekSpeed, l->SeekWhence,
            l->ParentCalls, l->TypenameCalls);
    }

    public static (int Lock, int Unlock) LockCounts()
    {
        ParityLog* l = parity_log_ptr();
        return (l->LockCalls, l->UnlockCalls);
    }
}
