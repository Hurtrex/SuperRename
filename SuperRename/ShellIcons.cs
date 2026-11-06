using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SuperRename;

public static class ShellIcons
{
    const uint SHGFI_ICON = 0x100;
    const uint SHGFI_SMALLICON = 0x1;
    const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    const string FolderKey = "<folder>";

    static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? For(string name, bool isDir)
    {
        var key = isDir ? FolderKey : Path.GetExtension(name);
        if (!Cache.TryGetValue(key, out var icon))
        {
            icon = Load(isDir ? "folder" : "file" + key, isDir);
            Cache[key] = icon;
        }
        return icon;
    }

    static ImageSource? Load(string sampleName, bool isDir)
    {
        var info = new SHFILEINFO();
        var attributes = isDir ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        var flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;
        if (SHGetFileInfo(sampleName, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags) == IntPtr.Zero
            || info.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally { DestroyIcon(info.hIcon); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);
}
