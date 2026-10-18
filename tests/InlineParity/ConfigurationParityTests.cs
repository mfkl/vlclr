using InlineParity.Native;
using VLCLR.Plugin;
using VLCLR.Types;

namespace InlineParity;

/// <summary>VLCConfiguration against var_InheritBool, var_InheritInteger, var_InheritFloat and var_InheritString.</summary>
public unsafe class ConfigurationParityTests
{
    private static readonly int[] Results = [0, 0, -1, -2, 1, 12345];

    private static IEnumerable<(string Name, long Raw, int Result)> Scripts(int salt)
    {
        string[] names = Cases.Names(salt).ToArray();
        int i = 0;
        foreach (long raw in Cases.AnyInt64(salt))
        {
            yield return (names[i % names.Length], raw, Results[i++ % Results.Length]);
        }
    }

    /// <summary>
    /// Keeps the union's other bytes as noise but makes the b_bool byte a valid
    /// _Bool (0 or 1). Any other value is undefined behaviour in C: Apple clang
    /// tests only bit 0, MSVC and gcc test the whole byte. VLC stores 0 or 1.
    /// </summary>
    private static long CanonicalBool(long raw) => (raw & ~0xFFL) | ((raw & 0xFF) != 0 ? 1 : 0);

    private static readonly long[] BoolPatterns = [0, 1, 0x100, 0x101, 0xFF, 0x7F00, 0x0100_0000_0000_0001, unchecked((long)0xFFFF_FFFF_FFFF_FF00)];

    [SkippableFact]
    public void GetBool_matches_var_InheritBool()
    {
        ParityLibrary.Require();

        nint obj = ParityNative.parity_object_new(0, 0);
        try
        {
            var configuration = new VLCConfiguration(obj);
            IEnumerable<(string, long, int)> inputs = BoolPatterns.SelectMany(p => new[] { 0, -1 }.Select(r => ("bool-opt", p, r)))
                .Concat(Scripts(50));
            foreach ((string name, long anyRaw, int result) in inputs)
            {
                long raw = CanonicalBool(anyRaw);
                ParityNative.ScriptSetup script = s => { s->InheritResult = result; s->InheritValue = raw; };

                var (c, cLog) = Harness.Run(script, () => ParityNative.parity_var_InheritBool(obj, name) != 0);
                var (managed, managedLog) = Harness.Run(script, () => configuration.GetBool(name));

                ParityAssert.Equal(c, managed, $"GetBool('{name}') raw 0x{raw:X}, result {result}");
                ParityAssert.Same(cLog, managedLog, $"GetBool('{name}') raw 0x{raw:X}, result {result}");
                Assert.Equal(VLCVarType.Bool, managedLog.InheritType);
            }
        }
        finally
        {
            ParityNative.parity_object_free(obj);
        }
    }

    [SkippableFact]
    public void GetInteger_matches_var_InheritInteger()
    {
        ParityLibrary.Require();

        nint obj = ParityNative.parity_object_new(0, 0);
        try
        {
            var configuration = new VLCConfiguration(obj);
            foreach ((string name, long raw, int result) in Scripts(51))
            {
                ParityNative.ScriptSetup script = s => { s->InheritResult = result; s->InheritValue = raw; };

                var (c, cLog) = Harness.Run(script, () => ParityNative.parity_var_InheritInteger(obj, name));
                var (managed, managedLog) = Harness.Run(script, () => configuration.GetInteger(name));

                ParityAssert.Equal(c, managed, $"GetInteger('{name}') raw {raw}, result {result}");
                ParityAssert.Same(cLog, managedLog, $"GetInteger('{name}') raw {raw}, result {result}");
                Assert.Equal(VLCVarType.Integer, managedLog.InheritType);
            }
        }
        finally
        {
            ParityNative.parity_object_free(obj);
        }
    }

    [SkippableFact]
    public void GetFloat_matches_var_InheritFloat()
    {
        ParityLibrary.Require();

        nint obj = ParityNative.parity_object_new(0, 0);
        try
        {
            var configuration = new VLCConfiguration(obj);
            long[] specials =
            [
                BitConverter.SingleToInt32Bits(0f), BitConverter.SingleToInt32Bits(-0f), BitConverter.SingleToInt32Bits(1.5f),
                BitConverter.SingleToInt32Bits(float.MaxValue), BitConverter.SingleToInt32Bits(float.Epsilon),
                BitConverter.SingleToInt32Bits(float.PositiveInfinity), BitConverter.SingleToInt32Bits(float.NegativeInfinity),
            ];
            IEnumerable<(string, long, int)> inputs = specials.Select(b => ("float-opt", b, 0)).Concat(Scripts(52));
            foreach ((string name, long raw, int result) in inputs)
            {
                ParityNative.ScriptSetup script = s => { s->InheritResult = result; s->InheritValue = raw; };

                var (c, cLog) = Harness.Run(script, () => ParityNative.parity_var_InheritFloat(obj, name));
                var (managed, managedLog) = Harness.Run(script, () => configuration.GetFloat(name));

                if (!(float.IsNaN(c) && float.IsNaN(managed)))
                {
                    ParityAssert.FloatBitsEqual(c, managed, $"GetFloat('{name}') raw 0x{raw:X}, result {result}");
                }

                ParityAssert.Same(cLog, managedLog, $"GetFloat('{name}') raw 0x{raw:X}, result {result}");
                Assert.Equal(VLCVarType.Float, managedLog.InheritType);
            }
        }
        finally
        {
            ParityNative.parity_object_free(obj);
        }
    }

    [SkippableFact]
    public void GetString_matches_var_InheritString_for_non_empty_values()
    {
        ParityLibrary.Require();

        nint obj = ParityNative.parity_object_new(0, 0);
        try
        {
            var configuration = new VLCConfiguration(obj);
            string[] names = Cases.Names(53).ToArray();
            int n = 0;
            foreach (string? value in Cases.Strings(53).Where(s => s.Length > 0).Cast<string?>().Append(null))
            {
                string name = names[n % names.Length];
                int result = Results[n++ % Results.Length];
                ParityNative.ScriptSetup script = s => { s->InheritResult = result; s->SetInheritString(value); };

                var (c, cLog) = Harness.Run(script, () => Harness.TakeCString(ParityNative.parity_var_InheritString(obj, name)));
                var (managed, managedLog) = Harness.Run(script, () => configuration.GetString(name));

                ParityAssert.Equal(c, managed, $"GetString('{name}') = '{value}', result {result}");
                ParityAssert.Same(cLog, managedLog, $"GetString('{name}') = '{value}', result {result}");
                Assert.Equal(VLCVarType.String, managedLog.InheritType);
            }
        }
        finally
        {
            ParityNative.parity_object_free(obj);
        }
    }

    [SkippableFact]
    public void Documented_difference_GetString_does_not_map_an_empty_string_to_null()
    {
        ParityLibrary.Require();

        nint obj = ParityNative.parity_object_new(0, 0);
        try
        {
            ParityNative.ScriptSetup script = s => { s->InheritResult = 0; s->SetInheritString(""); };

            var (c, _) = Harness.Run(script, () => Harness.TakeCString(ParityNative.parity_var_InheritString(obj, "text")));
            var (managed, _) = Harness.Run(script, () => new VLCConfiguration(obj).GetString("text"));
            var (managedWithFallback, _) = Harness.Run(script, () => new VLCConfiguration(obj).GetString("text", "fallback"));

            // var_InheritString frees an empty value and returns NULL ...
            Assert.Null(c);
            // ... VLCConfiguration returns the empty string itself, even when a fallback is given.
            Assert.Equal("", managed);
            Assert.Equal("", managedWithFallback);
        }
        finally
        {
            ParityNative.parity_object_free(obj);
        }
    }

    [SkippableFact]
    public void Fallback_is_returned_when_inheritance_fails_or_the_object_is_null()
    {
        ParityLibrary.Require();

        // Extension over the C helpers, which always fall back to false / 0 / 0.0 / NULL.
        nint obj = ParityNative.parity_object_new(0, 0);
        try
        {
            ParityNative.ScriptSetup fail = s => s->InheritResult = -1;
            var configuration = new VLCConfiguration(obj);

            Assert.True(Harness.Run(fail, () => configuration.GetBool("o", true)).Result);
            Assert.Equal(42L, Harness.Run(fail, () => configuration.GetInteger("o", 42)).Result);
            Assert.Equal(2.5f, Harness.Run(fail, () => configuration.GetFloat("o", 2.5f)).Result);
            Assert.Equal("fb", Harness.Run(fail, () => configuration.GetString("o", "fb")).Result);

            var none = new VLCConfiguration(0);
            var (value, log) = Harness.Run(null, () => none.GetInteger("o", 7));
            Assert.Equal(7L, value);
            Assert.Equal(0, log.InheritCalls);
        }
        finally
        {
            ParityNative.parity_object_free(obj);
        }
    }
}
