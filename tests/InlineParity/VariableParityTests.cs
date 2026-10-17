using InlineParity.Native;
using VLCLR;
using VLCLR.Types;

namespace InlineParity;

/// <summary>VLCVariable against var_SetInteger, var_GetInteger, var_SetString, var_GetString and var_Create (vlc_variables.h).</summary>
public unsafe class VariableParityTests
{
    private static readonly int[] Results = [0, -1, -2, 1, 12345];

    private static IEnumerable<(string Name, long Value)> NamedIntegers(int salt)
    {
        string[] names = Cases.Names(salt).ToArray();
        int i = 0;
        foreach (long value in Cases.AnyInt64(salt))
        {
            yield return (names[i++ % names.Length], value);
        }
    }

    [SkippableFact]
    public void SetInteger_matches_var_SetInteger()
    {
        ParityLibrary.Require();

        nint obj = ParityNative.parity_object_new(0, 0);
        try
        {
            var variable = new VLCVariable(obj);
            int n = 0;
            foreach ((string name, long value) in NamedIntegers(40))
            {
                int result = Results[n++ % Results.Length];
                ParityNative.ScriptSetup script = s => s->SetResult = result;

                var (c, cLog) = Harness.Run(script, () => ParityNative.parity_var_SetInteger(obj, name, value));
                var (managed, managedLog) = Harness.Run(script, () => variable.SetInteger(name, value));

                ParityAssert.Equal(c == 0, managed, $"success for '{name}'={value}, result {result}");
                ParityAssert.Same(cLog, managedLog, $"SetInteger('{name}', {value}), result {result}");
                Assert.Equal(VLCVarType.Integer, managedLog.SetType);
                Assert.Equal(value, managedLog.SetInt);
            }
        }
        finally
        {
            ParityNative.parity_object_free(obj);
        }
    }

    [SkippableFact]
    public void GetInteger_matches_var_GetInteger()
    {
        ParityLibrary.Require();

        nint obj = ParityNative.parity_object_new(0, 0);
        try
        {
            var variable = new VLCVariable(obj);
            int n = 0;
            foreach ((string name, long value) in NamedIntegers(41))
            {
                int result = Results[n++ % Results.Length];
                ParityNative.ScriptSetup script = s => { s->GetResult = result; s->GetValue = value; };

                var (c, cLog) = Harness.Run(script, () => ParityNative.parity_var_GetInteger(obj, name));
                var (managed, managedLog) = Harness.Run(script, () => variable.GetInteger(name));

                ParityAssert.Equal(c, managed, $"GetInteger('{name}') = {value}, result {result}");
                ParityAssert.Same(cLog, managedLog, $"GetInteger('{name}')");
                ParityAssert.Equal(result == 0 ? value : 0, managed, "failure maps to 0");
                Assert.Equal(VLCVarType.Integer, managedLog.GetVarType);
            }
        }
        finally
        {
            ParityNative.parity_object_free(obj);
        }
    }

    [SkippableFact]
    public void SetString_matches_var_SetString()
    {
        ParityLibrary.Require();

        nint obj = ParityNative.parity_object_new(0, 0);
        try
        {
            var variable = new VLCVariable(obj);
            string[] names = Cases.Names(42).ToArray();
            int n = 0;
            foreach (string? value in Cases.Strings(42).Cast<string?>().Append(null))
            {
                string name = names[n % names.Length];
                int result = Results[n++ % Results.Length];
                ParityNative.ScriptSetup script = s => s->SetResult = result;

                var (c, cLog) = Harness.Run(script, () => ParityNative.parity_var_SetString(obj, name, value));
                var (managed, managedLog) = Harness.Run(script, () => variable.SetString(name, value));

                ParityAssert.Equal(c == 0, managed, $"success for '{name}'='{value}', result {result}");
                ParityAssert.Same(cLog, managedLog, $"SetString('{name}', '{value}'), result {result}");
                Assert.Equal(VLCVarType.String, managedLog.SetType);
                Assert.Equal(value is null ? 1 : 0, managedLog.SetStrIsNull);
            }
        }
        finally
        {
            ParityNative.parity_object_free(obj);
        }
    }

    [SkippableFact]
    public void GetString_matches_var_GetString()
    {
        ParityLibrary.Require();

        nint obj = ParityNative.parity_object_new(0, 0);
        try
        {
            var variable = new VLCVariable(obj);
            string[] names = Cases.Names(43).ToArray();
            int n = 0;
            foreach (string? value in Cases.Strings(43).Cast<string?>().Append(null))
            {
                string name = names[n % names.Length];
                int result = Results[n++ % Results.Length];
                ParityNative.ScriptSetup script = s => { s->GetResult = result; s->SetGetString(value); };

                var (c, cLog) = Harness.Run(script, () => Harness.TakeCString(ParityNative.parity_var_GetString(obj, name)));
                var (managed, managedLog) = Harness.Run(script, () => variable.GetString(name));

                ParityAssert.Equal(c, managed, $"GetString('{name}') = '{value}', result {result}");
                ParityAssert.Same(cLog, managedLog, $"GetString('{name}')");
                Assert.Equal(VLCVarType.String, managedLog.GetVarType);
                if (result == 0)
                {
                    // An empty string is a value, not "unset": both sides pass it through.
                    ParityAssert.Equal(value, managed, "value passed through");
                }
                else
                {
                    Assert.Null(managed);
                }
            }
        }
        finally
        {
            ParityNative.parity_object_free(obj);
        }
    }

    [SkippableFact]
    public void CreateInteger_matches_var_Create_followed_by_var_SetInteger()
    {
        ParityLibrary.Require();

        nint obj = ParityNative.parity_object_new(0, 0);
        try
        {
            var variable = new VLCVariable(obj);
            int n = 0;
            foreach ((string name, long value) in NamedIntegers(44))
            {
                int createResult = Results[n++ % Results.Length];
                ParityNative.ScriptSetup script = s => { s->CreateResult = createResult; s->SetResult = (int)(value & 1) - 1; };

                var (c, cLog) = Harness.Run(script, () => ParityNative.parity_create_integer(obj, name, value));
                var (managed, managedLog) = Harness.Run(script, () => variable.CreateInteger(name, value));

                ParityAssert.Equal(c == 0, managed, $"CreateInteger('{name}', {value}), create result {createResult}");
                ParityAssert.Same(cLog, managedLog, $"CreateInteger('{name}', {value}), create result {createResult}");
                Assert.Equal(VLCVarType.Integer, managedLog.CreateType);
                Assert.Equal(createResult == 0 ? 1 : 0, managedLog.SetCalls);
            }
        }
        finally
        {
            ParityNative.parity_object_free(obj);
        }
    }

    [SkippableFact]
    public void CreateInteger_default_initial_value_is_zero()
    {
        ParityLibrary.Require();

        nint obj = ParityNative.parity_object_new(0, 0);
        try
        {
            var (_, log) = Harness.Run(null, () => new VLCVariable(obj).CreateInteger("count"));

            Assert.Equal(0L, log.SetInt);
            Assert.Equal("count", log.SetName);
        }
        finally
        {
            ParityNative.parity_object_free(obj);
        }
    }

    [SkippableFact]
    public void CreateString_matches_var_Create_followed_by_var_SetString()
    {
        ParityLibrary.Require();

        nint obj = ParityNative.parity_object_new(0, 0);
        try
        {
            var variable = new VLCVariable(obj);
            string[] names = Cases.Names(45).ToArray();
            int n = 0;
            foreach (string? value in Cases.Strings(45).Cast<string?>().Append(null))
            {
                string name = names[n % names.Length];
                int createResult = Results[n++ % Results.Length];
                ParityNative.ScriptSetup script = s => s->CreateResult = createResult;

                var (c, cLog) = Harness.Run(script, () => ParityNative.parity_create_string(obj, name, value));
                var (managed, managedLog) = Harness.Run(script, () => variable.CreateString(name, value));

                ParityAssert.Equal(c == 0, managed, $"CreateString('{name}', '{value}'), create result {createResult}");
                ParityAssert.Same(cLog, managedLog, $"CreateString('{name}', '{value}'), create result {createResult}");
                Assert.Equal(VLCVarType.String, managedLog.CreateType);
            }
        }
        finally
        {
            ParityNative.parity_object_free(obj);
        }
    }

    [SkippableFact]
    public void Documented_difference_CreateInteger_ignores_a_failing_initial_set()
    {
        ParityLibrary.Require();

        // A caller composing var_Create and var_SetInteger sees the set failure;
        // CreateInteger reports success once var_Create succeeded.
        nint obj = ParityNative.parity_object_new(0, 0);
        try
        {
            ParityNative.ScriptSetup script = s => { s->CreateResult = 0; s->SetResult = -1; };
            var (created, log) = Harness.Run(script, () => new VLCVariable(obj).CreateInteger("v", 5));

            Assert.True(created);
            Assert.Equal(1, log.SetCalls);
        }
        finally
        {
            ParityNative.parity_object_free(obj);
        }
    }
}
