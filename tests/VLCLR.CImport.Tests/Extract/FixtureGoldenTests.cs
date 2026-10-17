using VLCLR.CImport.Model;

namespace VLCLR.CImport.Tests.Extract;

[Collection("libclang")]
public class FixtureGoldenTests
{
    public static IEnumerable<object[]> Cases()
        => from name in FixtureExtractor.FixtureNames
           from abi in Abis.All
           select new object[] { name, abi };

    [Theory]
    [MemberData(nameof(Cases))]
    public void SnapshotMatchesGolden(string name, string abi)
    {
        var actual = FixtureExtractor.ExtractJson(name, abi);
        var file = $"{name}.{abi}.json";

        if (Environment.GetEnvironmentVariable(FixtureExtractor.UpdateVariable) == "1")
        {
            File.WriteAllText(Path.Combine(FixtureExtractor.SourceFixtureDirectory(), file), actual, new System.Text.UTF8Encoding(false));
            return;
        }

        var goldenPath = Path.Combine(FixtureExtractor.FixtureDirectory, file);
        Assert.True(File.Exists(goldenPath), $"Missing golden {file}. Run with {FixtureExtractor.UpdateVariable}=1 to create it.");
        Assert.Equal(File.ReadAllText(goldenPath).ReplaceLineEndings("\n"), actual);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ExtractingTwiceIsByteIdentical(string name, string abi)
        => Assert.Equal(FixtureExtractor.ExtractJson(name, abi), FixtureExtractor.ExtractJson(name, abi));

    [Theory]
    [MemberData(nameof(Cases))]
    public void GoldenIsCanonicalJson(string name, string abi)
    {
        var goldenPath = Path.Combine(FixtureExtractor.FixtureDirectory, $"{name}.{abi}.json");
        if (!File.Exists(goldenPath))
            return;
        var text = File.ReadAllText(goldenPath).ReplaceLineEndings("\n");
        Assert.Equal(text, SnapshotJson.Serialize(SnapshotJson.Deserialize(text)));
    }
}
