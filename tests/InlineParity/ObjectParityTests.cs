using System.Runtime.InteropServices;
using InlineParity.Native;
using VLCLR;

namespace InlineParity;

/// <summary>VLCObject.GetRoot against vlc_object_instance (vlc_objects.h) on a fake object chain.</summary>
public unsafe class ObjectParityTests
{
    [SkippableFact]
    public void GetRoot_matches_vlc_object_instance_for_chains_of_every_depth()
    {
        ParityLibrary.Require();

        nint name = Marshal.StringToCoTaskMemUTF8("object");
        List<nint> chain = [];
        try
        {
            nint parent = 0;
            for (int depth = 0; depth < 8; depth++)
            {
                nint obj = ParityNative.parity_object_new(parent, name);
                chain.Add(obj);
                parent = obj;

                var (cRoot, cLog) = Harness.Run<nint>(null, () => ParityNative.parity_vlc_object_instance(obj));
                var (managedRoot, managedLog) = Harness.Run(null, () => new VLCObject(obj).GetRoot().Handle);

                ParityAssert.Equal(cRoot, managedRoot, $"root at depth {depth}");
                ParityAssert.Equal(chain[0], managedRoot, $"root is the first object at depth {depth}");
                ParityAssert.Same(cLog, managedLog, $"parent lookups at depth {depth}");
                ParityAssert.Equal(depth + 1, managedLog.ParentCalls, $"parent calls at depth {depth}");
            }
        }
        finally
        {
            foreach (nint obj in chain)
            {
                ParityNative.parity_object_free(obj);
            }

            Marshal.FreeCoTaskMem(name);
        }
    }

    [SkippableFact]
    public void GetRoot_of_a_root_object_is_that_object()
    {
        ParityLibrary.Require();

        nint root = ParityNative.parity_object_new(0, 0);
        try
        {
            var wrapper = new VLCObject(root);

            Assert.Same(wrapper, wrapper.GetRoot());
            Assert.Null(wrapper.GetParent());
        }
        finally
        {
            ParityNative.parity_object_free(root);
        }
    }

    [SkippableFact]
    public void GetTypeName_returns_the_string_vlc_object_typename_gives()
    {
        ParityLibrary.Require();

        nint name = Marshal.StringToCoTaskMemUTF8("video filter (日本)");
        nint obj = ParityNative.parity_object_new(0, name);
        try
        {
            Assert.Equal("video filter (日本)", new VLCObject(obj).GetTypeName());
            Assert.Equal("video filter (日本)", VLCObject.GetTypeName(obj));
        }
        finally
        {
            ParityNative.parity_object_free(obj);
            Marshal.FreeCoTaskMem(name);
        }
    }
}
