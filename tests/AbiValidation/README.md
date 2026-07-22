# VLC ABI validation

This compile-only probe checks VLCLR's 64-bit managed layouts against the VLC
headers selected for a build. Any changed size or field offset fails at compile
time through a C++ `static_assert`.

From a Visual Studio developer machine, run:

```powershell
tests\AbiValidation\verify-vlc-abi.cmd vlc\include
```

CI passes the include directory from its downloaded VLC SDK, so upgrading the
pinned SDK cannot silently invalidate the managed interop structs.
