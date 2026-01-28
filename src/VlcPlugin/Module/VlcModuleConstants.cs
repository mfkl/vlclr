// VLC module descriptor constants from vlc_plugin.h
// VLC 4.x API version: 4.0.6

namespace VlcPlugin.Module;

/// <summary>
/// Module property constants from enum vlc_module_properties in vlc_plugin.h
/// </summary>
public static class VlcModuleProperties
{
    // Core module operations
    public const int VLC_MODULE_CREATE = 0;
    public const int VLC_CONFIG_CREATE = 1;

    // Module metadata (0x100 range)
    public const int VLC_MODULE_CPU_REQUIREMENT = 0x100;
    public const int VLC_MODULE_SHORTCUT = 0x101;
    public const int VLC_MODULE_CAPABILITY = 0x102;
    public const int VLC_MODULE_SCORE = 0x103;
    public const int VLC_MODULE_CB_OPEN = 0x104;
    public const int VLC_MODULE_CB_CLOSE = 0x105;
    public const int VLC_MODULE_NO_UNLOAD = 0x106;
    public const int VLC_MODULE_NAME = 0x107;
    public const int VLC_MODULE_SHORTNAME = 0x108;
    public const int VLC_MODULE_DESCRIPTION = 0x109;
    public const int VLC_MODULE_HELP = 0x10A;
    public const int VLC_MODULE_TEXTDOMAIN = 0x10B;
    public const int VLC_MODULE_HELP_HTML = 0x10C;

    // Config property constants (0x1000 range)
    public const int VLC_CONFIG_NAME = 0x1000;
    public const int VLC_CONFIG_VALUE = 0x1001;
    public const int VLC_CONFIG_RANGE = 0x1002;
    public const int VLC_CONFIG_ADVANCED_RESERVED = 0x1003;
    public const int VLC_CONFIG_VOLATILE = 0x1004;
    public const int VLC_CONFIG_PERSISTENT_OBSOLETE = 0x1005;
    public const int VLC_CONFIG_PRIVATE = 0x1006;
    public const int VLC_CONFIG_REMOVED = 0x1007;
    public const int VLC_CONFIG_CAPABILITY = 0x1008;
    public const int VLC_CONFIG_SHORTCUT = 0x1009;
    public const int VLC_CONFIG_OLDNAME_OBSOLETE = 0x100A;
    public const int VLC_CONFIG_SAFE = 0x100B;
    public const int VLC_CONFIG_DESC = 0x100C;
    public const int VLC_CONFIG_LIST_OBSOLETE = 0x100D;
    public const int VLC_CONFIG_ADD_ACTION_OBSOLETE = 0x100E;
    public const int VLC_CONFIG_LIST = 0x100F;
    public const int VLC_CONFIG_LIST_CB_OBSOLETE = 0x1010;
}

/// <summary>
/// Configuration hint and item types from vlc_plugin.h
/// </summary>
public static class VlcConfigTypes
{
    // Configuration hint types
    public const int CONFIG_HINT_CATEGORY = 0x02;
    public const int CONFIG_SUBCATEGORY = 0x07;
    public const int CONFIG_SECTION = 0x08;

    // Configuration item types
    public const int CONFIG_ITEM_FLOAT = 1 << 5;      // 0x20
    public const int CONFIG_ITEM_INTEGER = 2 << 5;    // 0x40
    public const int CONFIG_ITEM_RGB = CONFIG_ITEM_INTEGER | 0x01;
    public const int CONFIG_ITEM_BOOL = 3 << 5;       // 0x60
    public const int CONFIG_ITEM_STRING = 4 << 5;     // 0x80
    public const int CONFIG_ITEM_PASSWORD = CONFIG_ITEM_STRING | 0x01;
    public const int CONFIG_ITEM_KEY = CONFIG_ITEM_STRING | 0x02;
    public const int CONFIG_ITEM_MODULE = CONFIG_ITEM_STRING | 0x04;
    public const int CONFIG_ITEM_MODULE_CAT = CONFIG_ITEM_STRING | 0x05;
    public const int CONFIG_ITEM_MODULE_LIST = CONFIG_ITEM_STRING | 0x06;
    public const int CONFIG_ITEM_MODULE_LIST_CAT = CONFIG_ITEM_STRING | 0x07;
    public const int CONFIG_ITEM_LOADFILE = CONFIG_ITEM_STRING | 0x0C;
    public const int CONFIG_ITEM_SAVEFILE = CONFIG_ITEM_STRING | 0x0D;
    public const int CONFIG_ITEM_DIRECTORY = CONFIG_ITEM_STRING | 0x0E;
    public const int CONFIG_ITEM_FONT = CONFIG_ITEM_STRING | 0x0F;
}

/// <summary>
/// Config categories from enum vlc_config_cat
/// </summary>
public static class VlcConfigCategory
{
    public const int CAT_HIDDEN = -1;
    public const int CAT_UNKNOWN = 0;
    public const int CAT_INTERFACE = 1;
    public const int CAT_AUDIO = 2;
    public const int CAT_VIDEO = 3;
    public const int CAT_INPUT = 4;
    public const int CAT_SOUT = 5;
    public const int CAT_ADVANCED = 6;
    public const int CAT_PLAYLIST = 7;
}

/// <summary>
/// Config subcategories from enum vlc_config_subcat
/// </summary>
public static class VlcConfigSubcategory
{
    public const int SUBCAT_HIDDEN = -1;
    public const int SUBCAT_UNKNOWN = 0;

    // Interface
    public const int SUBCAT_INTERFACE_GENERAL = 101;
    public const int SUBCAT_INTERFACE_MAIN = 102;
    public const int SUBCAT_INTERFACE_CONTROL = 103;
    public const int SUBCAT_INTERFACE_HOTKEYS = 104;

    // Audio
    public const int SUBCAT_AUDIO_GENERAL = 201;
    public const int SUBCAT_AUDIO_AOUT = 202;
    public const int SUBCAT_AUDIO_AFILTER = 203;
    public const int SUBCAT_AUDIO_VISUAL = 204;
    public const int SUBCAT_AUDIO_RESAMPLER = 206;

    // Video
    public const int SUBCAT_VIDEO_GENERAL = 301;
    public const int SUBCAT_VIDEO_VOUT = 302;
    public const int SUBCAT_VIDEO_VFILTER = 303;
    public const int SUBCAT_VIDEO_SUBPIC = 305;
    public const int SUBCAT_VIDEO_SPLITTER = 306;

    // Input
    public const int SUBCAT_INPUT_GENERAL = 401;
    public const int SUBCAT_INPUT_ACCESS = 402;
    public const int SUBCAT_INPUT_DEMUX = 403;
    public const int SUBCAT_INPUT_VCODEC = 404;
    public const int SUBCAT_INPUT_ACODEC = 405;
    public const int SUBCAT_INPUT_SCODEC = 406;
    public const int SUBCAT_INPUT_STREAM_FILTER = 407;

    // Stream output
    public const int SUBCAT_SOUT_GENERAL = 501;
    public const int SUBCAT_SOUT_STREAM = 502;
    public const int SUBCAT_SOUT_MUX = 503;
    public const int SUBCAT_SOUT_ACO = 504;
    public const int SUBCAT_SOUT_PACKETIZER = 505;
    public const int SUBCAT_SOUT_VOD = 507;
    public const int SUBCAT_SOUT_RENDERER = 508;

    // Advanced
    public const int SUBCAT_ADVANCED_MISC = 602;
    public const int SUBCAT_ADVANCED_NETWORK = 603;

    // Playlist
    public const int SUBCAT_PLAYLIST_GENERAL = 701;
    public const int SUBCAT_PLAYLIST_SD = 702;
    public const int SUBCAT_PLAYLIST_EXPORT = 703;
}

/// <summary>
/// VLC API version string
/// </summary>
public static class VlcApi
{
    public const string VLC_API_VERSION_STRING = "4.0.6";

    // License strings (decoded from vlc_plugin.h hex values)
    public const string VLC_COPYRIGHT_VIDEOLAN = "Copyright (C) the VideoLAN VLC media player developers";
    public const string VLC_LICENSE_LGPL_2_1_PLUS = "Licensed under the terms of the GNU Lesser General Public License, version 2.1 or later.";
    public const string VLC_LICENSE_GPL_2_PLUS = "Licensed under the terms of the GNU General Public License, version 2 or later.";
}
