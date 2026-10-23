// VLC Interface Context wrapper
// Provides safe access to interface module information
// VLC Version: 4.0.6

using Interop = VLCLR.Native.Interop;

namespace VLCLR.Plugin;

/// <summary>
/// Provides safe access to a VLC interface module's context.
/// Wraps the native intf_thread_t pointer and exposes commonly needed properties.
/// </summary>
public readonly unsafe struct VLCInterfaceContext
{
    private readonly nint _intfPtr;

    /// <summary>
    /// Creates an interface context from a native interface pointer.
    /// </summary>
    /// <param name="intfPtr">Pointer to intf_thread_t</param>
    public VLCInterfaceContext(nint intfPtr)
    {
        _intfPtr = intfPtr;
    }

    /// <summary>
    /// Gets a logger bound to this interface's VLC object.
    /// </summary>
    public VLCLogger Logger => new(_intfPtr);

    /// <summary>
    /// Gets the native interface pointer for advanced use.
    /// </summary>
    public nint NativePtr => _intfPtr;

    /// <summary>
    /// Gets whether this context is valid (has a non-null interface pointer).
    /// </summary>
    public bool IsValid => _intfPtr != 0;

    /// <summary>
    /// Gets the interface's private data pointer (intf->p_sys).
    /// </summary>
    public nint Sys => (nint)((Interop.IntfThread*)_intfPtr)->Sys;

    /// <summary>
    /// Sets the interface's private data pointer (intf->p_sys).
    /// </summary>
    public void SetSys(nint sys) => ((Interop.IntfThread*)_intfPtr)->Sys = (Interop.IntfSys*)sys;
}
