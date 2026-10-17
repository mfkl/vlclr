using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace VLCLR.CImport.Model;

/// <summary>
/// Deterministic JSON for <see cref="Snapshot"/>: camelCase names, enums as
/// camelCase strings, nulls and false booleans omitted, two-space indent, LF line
/// endings, trailing newline, canonical top-level ordering. Two snapshots with
/// equal content always produce byte-identical files.
/// </summary>
public static class SnapshotJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(Snapshot snapshot)
        => JsonSerializer.Serialize(snapshot.Canonicalize(), Options) + "\n";

    public static Snapshot Deserialize(string json)
        => JsonSerializer.Deserialize<Snapshot>(json, Options)
           ?? throw new InvalidDataException("Snapshot JSON is null.");

    public static Snapshot Load(string path) => Deserialize(File.ReadAllText(path, Encoding.UTF8));

    public static void Save(Snapshot snapshot, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, Serialize(snapshot), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            IndentSize = 2,
            NewLine = "\n",
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OmitFalseBooleans } },
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.MakeReadOnly();
        return options;
    }

    private static void OmitFalseBooleans(JsonTypeInfo typeInfo)
    {
        foreach (var property in typeInfo.Properties)
        {
            if (property.PropertyType == typeof(bool))
                property.ShouldSerialize = static (_, value) => value is true;
        }
    }
}
