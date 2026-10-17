using System.Text;

namespace VLCLR.CImport.Emit;

/// <summary>Indenting line writer with LF line endings.</summary>
internal sealed class CSharpWriter
{
    private readonly StringBuilder text = new();
    private int indent;

    public void Line(string line = "")
    {
        if (line.Length > 0)
            text.Append(' ', indent * 4).Append(line);
        text.Append('\n');
    }

    public void Lines(IEnumerable<string> lines)
    {
        foreach (var line in lines)
            Line(line);
    }

    public void Open(string header)
    {
        Line(header);
        Line("{");
        indent++;
    }

    public void Close()
    {
        indent--;
        Line("}");
    }

    /// <summary>Writes blocks of lines separated by one blank line.</summary>
    public void Blocks(IEnumerable<IReadOnlyList<string>> blocks)
    {
        var first = true;
        foreach (var block in blocks)
        {
            if (!first)
                Line();
            first = false;
            Lines(block);
        }
    }

    public override string ToString() => text.ToString();
}
