// VLC module descriptor constants from vlc_plugin.h
// VLC 4.x API version: 4.0.6

using Interop = VLCLR.Native.Interop;

namespace VLCLR.Module;

/// <summary>
/// Module property constants from enum vlc_module_properties in vlc_plugin.h
/// </summary>
public static class VLCModuleConstants
{
    // Core module operations
    public const int VLC_MODULE_CREATE = (int)Interop.ModuleProperties.ModuleCreate;
    public const int VLC_CONFIG_CREATE = (int)Interop.ModuleProperties.ConfigCreate;

    // Module metadata (0x100 range)
    public const int VLC_MODULE_CPU_REQUIREMENT = (int)Interop.ModuleProperties.ModuleCpuRequirement;
    public const int VLC_MODULE_SHORTCUT = (int)Interop.ModuleProperties.ModuleShortcut;
    public const int VLC_MODULE_CAPABILITY = (int)Interop.ModuleProperties.ModuleCapability;
    public const int VLC_MODULE_SCORE = (int)Interop.ModuleProperties.ModuleScore;
    public const int VLC_MODULE_CB_OPEN = (int)Interop.ModuleProperties.ModuleCbOpen;
    public const int VLC_MODULE_CB_CLOSE = (int)Interop.ModuleProperties.ModuleCbClose;
    public const int VLC_MODULE_NO_UNLOAD = (int)Interop.ModuleProperties.ModuleNoUnload;
    public const int VLC_MODULE_NAME = (int)Interop.ModuleProperties.ModuleName;
    public const int VLC_MODULE_SHORTNAME = (int)Interop.ModuleProperties.ModuleShortname;
    public const int VLC_MODULE_DESCRIPTION = (int)Interop.ModuleProperties.ModuleDescription;
    public const int VLC_MODULE_HELP = (int)Interop.ModuleProperties.ModuleHelp;
    public const int VLC_MODULE_TEXTDOMAIN = (int)Interop.ModuleProperties.ModuleTextdomain;
    public const int VLC_MODULE_HELP_HTML = (int)Interop.ModuleProperties.ModuleHelpHtml;

    // Config property constants (0x1000 range)
    public const int VLC_CONFIG_NAME = (int)Interop.ModuleProperties.ConfigName;
    public const int VLC_CONFIG_VALUE = (int)Interop.ModuleProperties.ConfigValue;
    public const int VLC_CONFIG_RANGE = (int)Interop.ModuleProperties.ConfigRange;
    public const int VLC_CONFIG_ADVANCED_RESERVED = (int)Interop.ModuleProperties.ConfigAdvancedReserved;
    public const int VLC_CONFIG_VOLATILE = (int)Interop.ModuleProperties.ConfigVolatile;
    public const int VLC_CONFIG_PERSISTENT_OBSOLETE = (int)Interop.ModuleProperties.ConfigPersistentObsolete;
    public const int VLC_CONFIG_PRIVATE = (int)Interop.ModuleProperties.ConfigPrivate;
    public const int VLC_CONFIG_REMOVED = (int)Interop.ModuleProperties.ConfigRemoved;
    public const int VLC_CONFIG_CAPABILITY = (int)Interop.ModuleProperties.ConfigCapability;
    public const int VLC_CONFIG_SHORTCUT = (int)Interop.ModuleProperties.ConfigShortcut;
    public const int VLC_CONFIG_OLDNAME_OBSOLETE = (int)Interop.ModuleProperties.ConfigOldnameObsolete;
    public const int VLC_CONFIG_SAFE = (int)Interop.ModuleProperties.ConfigSafe;
    public const int VLC_CONFIG_DESC = (int)Interop.ModuleProperties.ConfigDesc;
    public const int VLC_CONFIG_LIST_OBSOLETE = (int)Interop.ModuleProperties.ConfigListObsolete;
    public const int VLC_CONFIG_ADD_ACTION_OBSOLETE = (int)Interop.ModuleProperties.ConfigAddActionObsolete;
    public const int VLC_CONFIG_LIST = (int)Interop.ModuleProperties.ConfigList;
    public const int VLC_CONFIG_LIST_CB_OBSOLETE = (int)Interop.ModuleProperties.ConfigListCbObsolete;
}

/// <summary>
/// Configuration hint and item types from vlc_plugin.h
/// </summary>
public static class VLCConfigTypes
{
    // Configuration hint types
    public const int CONFIG_HINT_CATEGORY = Interop.VlcPlugin.ConfigHintCategory;
    public const int CONFIG_SUBCATEGORY = Interop.VlcPlugin.ConfigSubcategory;
    public const int CONFIG_SECTION = Interop.VlcPlugin.ConfigSection;

    // Configuration item types
    public const int CONFIG_ITEM_FLOAT = Interop.VlcPlugin.ConfigItemFloat;
    public const int CONFIG_ITEM_INTEGER = Interop.VlcPlugin.ConfigItemInteger;
    public const int CONFIG_ITEM_RGB = Interop.VlcPlugin.ConfigItemRgb;
    public const int CONFIG_ITEM_BOOL = Interop.VlcPlugin.ConfigItemBool;
    public const int CONFIG_ITEM_STRING = Interop.VlcPlugin.ConfigItemString;
    public const int CONFIG_ITEM_PASSWORD = Interop.VlcPlugin.ConfigItemPassword;
    public const int CONFIG_ITEM_KEY = Interop.VlcPlugin.ConfigItemKey;
    public const int CONFIG_ITEM_MODULE = Interop.VlcPlugin.ConfigItemModule;
    public const int CONFIG_ITEM_MODULE_CAT = Interop.VlcPlugin.ConfigItemModuleCat;
    public const int CONFIG_ITEM_MODULE_LIST = Interop.VlcPlugin.ConfigItemModuleList;
    public const int CONFIG_ITEM_MODULE_LIST_CAT = Interop.VlcPlugin.ConfigItemModuleListCat;
    public const int CONFIG_ITEM_LOADFILE = Interop.VlcPlugin.ConfigItemLoadfile;
    public const int CONFIG_ITEM_SAVEFILE = Interop.VlcPlugin.ConfigItemSavefile;
    public const int CONFIG_ITEM_DIRECTORY = Interop.VlcPlugin.ConfigItemDirectory;
    public const int CONFIG_ITEM_FONT = Interop.VlcPlugin.ConfigItemFont;
}

/// <summary>
/// Config categories from enum vlc_config_cat
/// </summary>
public static class VLCConfigCategory
{
    public const int CAT_HIDDEN = (int)Interop.ConfigCat.Hidden;
    public const int CAT_UNKNOWN = (int)Interop.ConfigCat.Unknown;
    public const int CAT_INTERFACE = (int)Interop.ConfigCat.Interface;
    public const int CAT_AUDIO = (int)Interop.ConfigCat.Audio;
    public const int CAT_VIDEO = (int)Interop.ConfigCat.Video;
    public const int CAT_INPUT = (int)Interop.ConfigCat.Input;
    public const int CAT_SOUT = (int)Interop.ConfigCat.Sout;
    public const int CAT_ADVANCED = (int)Interop.ConfigCat.Advanced;
    public const int CAT_PLAYLIST = (int)Interop.ConfigCat.Playlist;
}

/// <summary>
/// Config subcategories from enum vlc_config_subcat
/// </summary>
public static class VLCConfigSubcategory
{
    public const int SUBCAT_HIDDEN = (int)Interop.ConfigSubcat.Hidden;
    public const int SUBCAT_UNKNOWN = (int)Interop.ConfigSubcat.Unknown;

    // Interface
    public const int SUBCAT_INTERFACE_GENERAL = (int)Interop.ConfigSubcat.InterfaceGeneral;
    public const int SUBCAT_INTERFACE_MAIN = (int)Interop.ConfigSubcat.InterfaceMain;
    public const int SUBCAT_INTERFACE_CONTROL = (int)Interop.ConfigSubcat.InterfaceControl;
    public const int SUBCAT_INTERFACE_HOTKEYS = (int)Interop.ConfigSubcat.InterfaceHotkeys;

    // Audio
    public const int SUBCAT_AUDIO_GENERAL = (int)Interop.ConfigSubcat.AudioGeneral;
    public const int SUBCAT_AUDIO_AOUT = (int)Interop.ConfigSubcat.AudioAout;
    public const int SUBCAT_AUDIO_AFILTER = (int)Interop.ConfigSubcat.AudioAfilter;
    public const int SUBCAT_AUDIO_VISUAL = (int)Interop.ConfigSubcat.AudioVisual;
    public const int SUBCAT_AUDIO_RESAMPLER = (int)Interop.ConfigSubcat.AudioResampler;

    // Video
    public const int SUBCAT_VIDEO_GENERAL = (int)Interop.ConfigSubcat.VideoGeneral;
    public const int SUBCAT_VIDEO_VOUT = (int)Interop.ConfigSubcat.VideoVout;
    public const int SUBCAT_VIDEO_VFILTER = (int)Interop.ConfigSubcat.VideoVfilter;
    public const int SUBCAT_VIDEO_SUBPIC = (int)Interop.ConfigSubcat.VideoSubpic;
    public const int SUBCAT_VIDEO_SPLITTER = (int)Interop.ConfigSubcat.VideoSplitter;

    // Input
    public const int SUBCAT_INPUT_GENERAL = (int)Interop.ConfigSubcat.InputGeneral;
    public const int SUBCAT_INPUT_ACCESS = (int)Interop.ConfigSubcat.InputAccess;
    public const int SUBCAT_INPUT_DEMUX = (int)Interop.ConfigSubcat.InputDemux;
    public const int SUBCAT_INPUT_VCODEC = (int)Interop.ConfigSubcat.InputVcodec;
    public const int SUBCAT_INPUT_ACODEC = (int)Interop.ConfigSubcat.InputAcodec;
    public const int SUBCAT_INPUT_SCODEC = (int)Interop.ConfigSubcat.InputScodec;
    public const int SUBCAT_INPUT_STREAM_FILTER = (int)Interop.ConfigSubcat.InputStreamFilter;

    // Stream output
    public const int SUBCAT_SOUT_GENERAL = (int)Interop.ConfigSubcat.SoutGeneral;
    public const int SUBCAT_SOUT_STREAM = (int)Interop.ConfigSubcat.SoutStream;
    public const int SUBCAT_SOUT_MUX = (int)Interop.ConfigSubcat.SoutMux;
    public const int SUBCAT_SOUT_ACO = (int)Interop.ConfigSubcat.SoutAco;
    public const int SUBCAT_SOUT_PACKETIZER = (int)Interop.ConfigSubcat.SoutPacketizer;
    public const int SUBCAT_SOUT_VOD = (int)Interop.ConfigSubcat.SoutVod;
    public const int SUBCAT_SOUT_RENDERER = (int)Interop.ConfigSubcat.SoutRenderer;

    // Advanced
    public const int SUBCAT_ADVANCED_MISC = (int)Interop.ConfigSubcat.AdvancedMisc;
    public const int SUBCAT_ADVANCED_NETWORK = (int)Interop.ConfigSubcat.AdvancedNetwork;

    // Playlist
    public const int SUBCAT_PLAYLIST_GENERAL = (int)Interop.ConfigSubcat.PlaylistGeneral;
    public const int SUBCAT_PLAYLIST_SD = (int)Interop.ConfigSubcat.PlaylistSd;
    public const int SUBCAT_PLAYLIST_EXPORT = (int)Interop.ConfigSubcat.PlaylistExport;
}

/// <summary>
/// VLC API version string
/// </summary>
public static class VLCApi
{
    public const string VLC_API_VERSION_STRING = Interop.VlcPlugin.ApiVersionString;

    // License strings (decoded from vlc_plugin.h hex values)
    public const string VLC_COPYRIGHT_VIDEOLAN = Interop.VlcPlugin.CopyrightVideolan;
    public const string VLC_LICENSE_LGPL_2_1_PLUS = Interop.VlcPlugin.LicenseLgpl21Plus;
    public const string VLC_LICENSE_GPL_2_PLUS = Interop.VlcPlugin.LicenseGpl2Plus;
}
