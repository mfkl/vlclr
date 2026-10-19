using VLCLR.CImport.Merge;

namespace VLCLR.CImport.Emit;

/// <summary>
/// C# text that may pick a per-ABI value with <c>OperatingSystem.IsWindows()</c>.
/// Native AOT only folds that call to a constant where the accessor around it
/// is inlined, so this text has no string conversion: it reaches the output
/// only through <see cref="CSharpGeneration.Getter"/> and
/// <see cref="CSharpGeneration.Accessor"/>, which mark the accessor
/// AggressiveInlining when the text is per-ABI. Interpolating it into a
/// string throws.
/// </summary>
internal readonly struct AbiText
{
    private readonly string _text;

    private AbiText(string text, bool perAbi)
    {
        _text = text;
        PerAbi = perAbi;
    }

    /// <summary>True when the text holds an <c>OperatingSystem.IsWindows()</c> conditional.</summary>
    public bool PerAbi { get; }

    /// <summary>The C# text, for the accessor writers.</summary>
    public string Text => _text ?? throw new InvalidOperationException("Default AbiText.");

    /// <summary>One C# expression for a per-ABI value: the literal when the ABIs agree, else a platform conditional.</summary>
    public static AbiText Choice<T>(PerAbi<T> values, Func<T, string> format, bool parenthesize = false)
    {
        if (values.IsUniform)
            return new(format(values[0]), perAbi: false);
        var expression = $"OperatingSystem.IsWindows() ? {format(values.Windows)} : {format(values.SysV)}";
        return new(parenthesize ? $"({expression})" : expression, perAbi: true);
    }

    /// <summary>Text built around this one, per-ABI when this one is.</summary>
    public AbiText Map(Func<string, string> build) => new(build(Text), PerAbi);

    public override string ToString()
        => throw new InvalidOperationException($"AbiText '{_text}' was converted to a string: write it with Getter or Accessor so per-ABI accessors are inlined.");
}
