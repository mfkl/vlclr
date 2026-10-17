using VLCLR.Module;
using VLCLR.Native;
using VLCLR.Types;
using Xunit;

namespace VLCLR.Tests.Migration;

/// <summary>
/// Pins the public constants that are defined from the generated interop layer
/// to their literal values, so a VLC header bump that changes one is visible.
/// </summary>
public class ConstantValueTests
{
    [Fact]
    public void FourCC_values_are_unchanged()
    {
        Assert.Equal(0x32335652u, VLCFourCC.RV32);
        Assert.Equal(0x34325652u, VLCFourCC.RV24);
        Assert.Equal(0x41424752u, VLCFourCC.RGBA);
        Assert.Equal(0x41524742u, VLCFourCC.BGRA);
        Assert.Equal(0x30323449u, VLCFourCC.I420);
        Assert.Equal(0x32315659u, VLCFourCC.YV12);
        Assert.Equal(0x31315844u, VLCFourCC.D3D11Opaque);
        Assert.Equal(0x30315844u, VLCFourCC.D3D11Opaque10Bit);
        Assert.Equal(0x47525844u, VLCFourCC.D3D11OpaqueRgba);
        Assert.Equal(0x52474144u, VLCFourCC.D3D11OpaqueBgra);
        Assert.Equal(0x31314144u, VLCFourCC.D3D11OpaqueAlpha);
        Assert.Equal(0x6C323366u, VLCFourCC.F32L);
        Assert.Equal(0x6C363173u, VLCFourCC.S16L);
        Assert.Equal(5, VLCFilterConstants.PICTURE_PLANE_MAX);
    }

    [Fact]
    public void Module_constants_are_unchanged()
    {
        Assert.Equal(0, VLCModuleConstants.VLC_MODULE_CREATE);
        Assert.Equal(1, VLCModuleConstants.VLC_CONFIG_CREATE);
        Assert.Equal(0x104, VLCModuleConstants.VLC_MODULE_CB_OPEN);
        Assert.Equal(0x10C, VLCModuleConstants.VLC_MODULE_HELP_HTML);
        Assert.Equal(0x1000, VLCModuleConstants.VLC_CONFIG_NAME);
        Assert.Equal(0x100F, VLCModuleConstants.VLC_CONFIG_LIST);
        Assert.Equal(0x1010, VLCModuleConstants.VLC_CONFIG_LIST_CB_OBSOLETE);
        Assert.Equal(0x02, VLCConfigTypes.CONFIG_HINT_CATEGORY);
        Assert.Equal(0x07, VLCConfigTypes.CONFIG_SUBCATEGORY);
        Assert.Equal(0x08, VLCConfigTypes.CONFIG_SECTION);
        Assert.Equal(0x20, VLCConfigTypes.CONFIG_ITEM_FLOAT);
        Assert.Equal(0x40, VLCConfigTypes.CONFIG_ITEM_INTEGER);
        Assert.Equal(0x41, VLCConfigTypes.CONFIG_ITEM_RGB);
        Assert.Equal(0x60, VLCConfigTypes.CONFIG_ITEM_BOOL);
        Assert.Equal(0x80, VLCConfigTypes.CONFIG_ITEM_STRING);
        Assert.Equal(0x81, VLCConfigTypes.CONFIG_ITEM_PASSWORD);
        Assert.Equal(0x82, VLCConfigTypes.CONFIG_ITEM_KEY);
        Assert.Equal(0x84, VLCConfigTypes.CONFIG_ITEM_MODULE);
        Assert.Equal(0x87, VLCConfigTypes.CONFIG_ITEM_MODULE_LIST_CAT);
        Assert.Equal(0x8C, VLCConfigTypes.CONFIG_ITEM_LOADFILE);
        Assert.Equal(0x8F, VLCConfigTypes.CONFIG_ITEM_FONT);
        Assert.Equal(-1, VLCConfigCategory.CAT_HIDDEN);
        Assert.Equal(7, VLCConfigCategory.CAT_PLAYLIST);
        Assert.Equal(-1, VLCConfigSubcategory.SUBCAT_HIDDEN);
        Assert.Equal(101, VLCConfigSubcategory.SUBCAT_INTERFACE_GENERAL);
        Assert.Equal(206, VLCConfigSubcategory.SUBCAT_AUDIO_RESAMPLER);
        Assert.Equal(407, VLCConfigSubcategory.SUBCAT_INPUT_STREAM_FILTER);
        Assert.Equal(508, VLCConfigSubcategory.SUBCAT_SOUT_RENDERER);
        Assert.Equal(703, VLCConfigSubcategory.SUBCAT_PLAYLIST_EXPORT);
        Assert.Equal("4.0.6", VLCApi.VLC_API_VERSION_STRING);
        Assert.Equal("Copyright (C) the VideoLAN VLC media player developers", VLCApi.VLC_COPYRIGHT_VIDEOLAN);
        Assert.Equal("Licensed under the terms of the GNU Lesser General Public License, version 2.1 or later.", VLCApi.VLC_LICENSE_LGPL_2_1_PLUS);
        Assert.Equal("Licensed under the terms of the GNU General Public License, version 2 or later.", VLCApi.VLC_LICENSE_GPL_2_PLUS);
    }

    [Fact]
    public void Variable_and_playlist_constants_are_unchanged()
    {
        Assert.Equal(0x00ff, VLCVarType.TypeMask);
        Assert.Equal(0x00f0, VLCVarType.ClassMask);
        Assert.Equal(0xff00, VLCVarType.FlagsMask);
        Assert.Equal(0x0010, VLCVarType.Void);
        Assert.Equal(0x0020, VLCVarType.Bool);
        Assert.Equal(0x0030, VLCVarType.Integer);
        Assert.Equal(0x0040, VLCVarType.String);
        Assert.Equal(0x0050, VLCVarType.Float);
        Assert.Equal(0x0070, VLCVarType.Address);
        Assert.Equal(0x00A0, VLCVarType.Coords);
        Assert.Equal(0x0100, VLCVarType.HasChoice);
        Assert.Equal(0x2000, VLCVarType.IsCommand);
        Assert.Equal(0x8000, VLCVarType.DoInherit);
        Assert.Equal(0x0012, VLCVarAction.SetStep);
        Assert.Equal(0x0013, VLCVarAction.SetValue);
        Assert.Equal(0x0014, VLCVarAction.SetText);
        Assert.Equal(0x0015, VLCVarAction.GetText);
        Assert.Equal(0x0016, VLCVarAction.GetMin);
        Assert.Equal(0x0017, VLCVarAction.GetMax);
        Assert.Equal(0x0018, VLCVarAction.GetStep);
        Assert.Equal(0x0020, VLCVarAction.AddChoice);
        Assert.Equal(0x0021, VLCVarAction.DelChoice);
        Assert.Equal(0x0022, VLCVarAction.ClearChoices);
        Assert.Equal(0x0024, VLCVarAction.GetChoices);
        Assert.Equal(0x0026, VLCVarAction.ChoicesCount);
        Assert.Equal(0x0027, VLCVarAction.SetMinMax);
        Assert.Equal(0, VLCPlaylistOrder.Normal);
        Assert.Equal(1, VLCPlaylistOrder.Random);
        Assert.Equal(0, VLCPlaylistRepeat.None);
        Assert.Equal(1, VLCPlaylistRepeat.One);
        Assert.Equal(2, VLCPlaylistRepeat.All);
    }

    [Fact]
    public void Enum_values_are_unchanged()
    {
        Assert.Equal(0, (int)SeekSpeed.Precise);
        Assert.Equal(1, (int)SeekSpeed.Fast);
        Assert.Equal(0, (int)SeekWhence.Absolute);
        Assert.Equal(1, (int)SeekWhence.Relative);
        Assert.Equal(0, (int)VLCLogType.Info);
        Assert.Equal(1, (int)VLCLogType.Error);
        Assert.Equal(2, (int)VLCLogType.Warning);
        Assert.Equal(3, (int)VLCLogType.Debug);
        Assert.Equal(0, (int)VLCPlayerState.Stopped);
        Assert.Equal(1, (int)VLCPlayerState.Started);
        Assert.Equal(2, (int)VLCPlayerState.Playing);
        Assert.Equal(3, (int)VLCPlayerState.Paused);
        Assert.Equal(4, (int)VLCPlayerState.Stopping);
        Assert.Equal(0, (int)VLCVarAtomicOp.BoolToggle);
        Assert.Equal(1, (int)VLCVarAtomicOp.IntegerAdd);
        Assert.Equal(2, (int)VLCVarAtomicOp.IntegerOr);
        Assert.Equal(3, (int)VLCVarAtomicOp.IntegerNand);
    }
}
