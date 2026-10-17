using VLCLR.CImport.Model;

namespace VLCLR.CImport.Tests.Model;

public class SnapshotJsonTests
{
    [Fact]
    public void RoundTripIsLossless()
    {
        var json = SnapshotJson.Serialize(SampleSnapshots.Windows);

        var again = SnapshotJson.Serialize(SnapshotJson.Deserialize(json));

        Assert.Equal(json, again);
    }

    [Fact]
    public void SerialisationIsCanonical()
    {
        var snapshot = SampleSnapshots.SysV;
        var shuffled = snapshot with
        {
            Types = [.. snapshot.Types.Reverse()],
            Enums = [.. snapshot.Enums.Reverse()],
            Functions = [.. snapshot.Functions.Reverse()],
            Constants = [.. snapshot.Constants.Reverse()],
            Typedefs = [.. snapshot.Typedefs.Reverse()],
        };

        Assert.Equal(SnapshotJson.Serialize(snapshot), SnapshotJson.Serialize(shuffled));
    }

    [Fact]
    public void TopLevelListsAreSortedAndFieldsKeepDeclarationOrder()
    {
        var snapshot = SnapshotJson.Deserialize(SnapshotJson.Serialize(SampleSnapshots.Windows));

        Assert.Equal(snapshot.Types.Select(t => t.Name).Order(StringComparer.Ordinal), snapshot.Types.Select(t => t.Name));
        var format = snapshot.Types.Single(t => t.Name == "video_format_t");
        Assert.Equal(["i_chroma", "dovi", "i_cubemap_padding", "i_size"], format.Fields.Select(f => f.Name));
        var category = snapshot.Enums.Single(e => e.Name == "es_format_category_e");
        Assert.Equal(["UNKNOWN_ES", "VIDEO_ES", "NEGATIVE_ES"], category.Members.Select(m => m.Name));
    }

    [Fact]
    public void OutputUsesLfAndEndsWithNewlineAndOmitsDefaults()
    {
        var json = SnapshotJson.Serialize(SampleSnapshots.Windows);

        Assert.DoesNotContain("\r", json);
        Assert.EndsWith("}\n", json);
        Assert.DoesNotContain("null", json);
        Assert.DoesNotContain("false", json);
        Assert.DoesNotContain("isBitfield", json);
        Assert.Contains("\"kind\": \"struct\"", json);
        Assert.Contains("\"offset\": 0", json);
    }

    [Fact]
    public void ValuesSurviveRoundTrip()
    {
        var snapshot = SnapshotJson.Deserialize(SnapshotJson.Serialize(SampleSnapshots.Windows));

        var dovi = snapshot.Types.Single(t => t.Name == "video_format_t::dovi");
        Assert.True(dovi.IsAnonymous);
        Assert.Equal("video_format_t", dovi.Parent);
        var level = dovi.Fields.Single(f => f.Name == "level");
        Assert.Equal((39L, 6), (level.BitOffset!.Value, level.BitWidth!.Value));
        Assert.True(level.IsBitfield);

        var wide = snapshot.Enums.Single(e => e.Name == "wide_e");
        Assert.Equal(ulong.MaxValue, unchecked((ulong)wide.Members[0].Value));
        Assert.True(wide.HasFixedUnderlyingType);

        var opaque = snapshot.Types.Single(t => t.Name == "vlc_object_internals");
        Assert.True(opaque.IsOpaque);
        Assert.Null(opaque.Size);

        var close = snapshot.Types.Single(t => t.Name == "vlc_filter_operations").Fields[0];
        Assert.Equal(TypeRefKind.Function, close.Type.Element!.Kind);
        Assert.Equal("filter_t", close.Type.Element.Function!.Parameters[0].Type.Element!.Name);

        var log = snapshot.Functions.Single(f => f.Name == "vlc_object_Log");
        Assert.True(log.IsVariadic);
        Assert.True(log.Parameters[1].Type.Element!.IsConst);

        var sep = snapshot.Constants.Single(c => c.Name == "DIR_SEP");
        Assert.Equal("\\", sep.String);
    }

    [Fact]
    public void UnknownPropertiesAreRejected()
    {
        var json = SnapshotJson.Serialize(SampleSnapshots.Windows).Replace("\"pointerSize\"", "\"pointerSize2\"");

        Assert.ThrowsAny<System.Text.Json.JsonException>(() => SnapshotJson.Deserialize(json));
    }

    [Fact]
    public void SaveAndLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cimport-{Guid.NewGuid():N}", "windows.json");
        try
        {
            SnapshotJson.Save(SampleSnapshots.Windows, path);

            var bytes = File.ReadAllBytes(path);
            Assert.NotEqual(0xEF, bytes[0]); // no BOM
            Assert.Equal(SnapshotJson.Serialize(SampleSnapshots.Windows), SnapshotJson.Serialize(SnapshotJson.Load(path)));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
