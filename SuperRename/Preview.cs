using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SuperRename;

public static class Preview
{
    public const int MaxBrowserEntries = 10000;

    const int MaxPreviewEntries = 500;
    const int CountLimit = 10000;
    const long MaxTextBytes = 5_000_000;
    const long MaxPdfBytes = 200_000_000;
    const long MaxZipEntryBytes = 200_000_000;
    const int MaxTextLines = 200;
    const int MaxTextChars = 50_000;
    const int MaxImageWidth = 1600;
    const int PdfRenderWidth = 1200;
    const int ThumbnailSize = 512;

    public static readonly string TempRoot =
        Path.Combine(Path.GetTempPath(), "SuperRename", Environment.ProcessId.ToString());

    static readonly IComparer<string> Natural = Comparer<string>.Create(App.StrCmpLogicalW);

    static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jfif", ".png", ".gif", ".bmp", ".tif", ".tiff", ".ico", ".webp",
        ".heic", ".heif", ".avif", ".jxr", ".wdp",
    };

    static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".csv", ".tsv", ".json", ".xml", ".yml", ".yaml", ".ini", ".cfg", ".conf", ".toml",
        ".html", ".htm", ".css", ".js", ".ts", ".jsx", ".tsx", ".py", ".cs", ".java", ".c", ".cpp", ".h", ".hpp",
        ".go", ".rs", ".rb", ".php", ".sh", ".ps1", ".bat", ".cmd", ".sql", ".srt", ".vtt", ".nfo",
    };

    public record Result(ImageSource? Image, string? Text, string Info, IReadOnlyList<Entry>? Entries = null);

    public record Entry(string Name, string Size, bool IsDir)
    {
        public ImageSource? Icon => ShellIcons.For(Name, IsDir);
    }

    public static string Plural(long count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    public static async Task<Result> LoadAsync(string path, bool isDir)
    {
        if (isDir) return await LoadFolder(path);

        var file = new FileInfo(path);
        return await TryLoadImage(file)
            ?? await TryLoadText(file)
            ?? await TryLoadPdf(file)
            ?? await TryLoadZip(file)
            ?? await LoadThumbnail(file);
    }

    public static (List<Entry> Shown, int Total) ListFolder(string path, int max)
    {
        var all = new DirectoryInfo(path).EnumerateFileSystemInfos().Take(CountLimit + 1).ToList();
        var shown = all
            .OrderBy(i => i is FileInfo)
            .ThenBy(i => i.Name, Natural)
            .Take(max)
            .Select(i => i is FileInfo f ? new Entry(f.Name, FormatSize(f.Length), false) : new Entry(i.Name, "", true))
            .ToList();
        return (shown, all.Count);
    }

    public static List<Entry> ListZipFolder(string zipPath, string folder)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var subfolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<Entry>();
        foreach (var entry in zip.Entries)
        {
            var path = ZipPathOf(entry);
            if (path.Length <= folder.Length || !path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) continue;

            var rest = path[folder.Length..];
            var slash = rest.IndexOf('/');
            if (slash >= 0) subfolders.Add(rest[..slash]);
            else files.Add(new Entry(rest, FormatSize(entry.Length), false));
        }
        return subfolders.Order(Natural).Select(name => new Entry(name, "", true))
            .Concat(files.OrderBy(f => f.Name, Natural))
            .ToList();
    }

    public static string ExtractZipEntry(string zipPath, string entryPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.First(e => string.Equals(ZipPathOf(e), entryPath, StringComparison.OrdinalIgnoreCase));
        if (entry.Length > MaxZipEntryBytes) throw new IOException("Too big to preview.");

        var folder = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, Path.GetFileName(entryPath));
        entry.ExtractToFile(destination);
        return destination;
    }

    static async Task<Result> LoadFolder(string path)
    {
        try
        {
            var (entries, total) = await Task.Run(() => ListFolder(path, MaxPreviewEntries));
            return new(null, null, $"Folder · {DescribeCount(total)}", entries);
        }
        catch
        {
            return new(await RunOnStaThread(() => LoadShellThumbnail(path)), null, "Folder (contents can't be read)");
        }
    }

    static async Task<Result?> TryLoadImage(FileInfo file)
    {
        if (!ImageExtensions.Contains(file.Extension)) return null;
        try
        {
            var (image, width, height) = await RunOnStaThread(() => LoadImage(file.FullName));
            return new(image, null, $"{width} × {height} · {FormatSize(file.Length)}");
        }
        catch { return null; }
    }

    static async Task<Result?> TryLoadText(FileInfo file)
    {
        if (!TextExtensions.Contains(file.Extension) || file.Length > MaxTextBytes) return null;
        try
        {
            var text = await Task.Run(() => ReadText(file.FullName));
            return new(null, text, $"{ExtensionLabel(file)} · {FormatSize(file.Length)}");
        }
        catch { return null; }
    }

    static async Task<Result?> TryLoadPdf(FileInfo file)
    {
        if (!HasExtension(file, ".pdf") || file.Length > MaxPdfBytes) return null;
        try
        {
            var (image, pages) = await Task.Run(() => RenderFirstPdfPage(file.FullName));
            return new(image, null, $"PDF · {Plural(pages, "page")} · {FormatSize(file.Length)}");
        }
        catch { return null; }
    }

    static async Task<Result?> TryLoadZip(FileInfo file)
    {
        if (!HasExtension(file, ".zip")) return null;
        try
        {
            var (entries, total) = await Task.Run(() => ListZip(file.FullName));
            return new(null, null, $"ZIP · {DescribeCount(total)} · {FormatSize(file.Length)}", entries);
        }
        catch { return null; }
    }

    static async Task<Result> LoadThumbnail(FileInfo file)
    {
        var label = file.Extension.Length > 1 ? ExtensionLabel(file) + " file" : "File";
        var thumbnail = await RunOnStaThread(() => LoadShellThumbnail(file.FullName));
        return new(thumbnail, null, $"{label} · {FormatSize(file.Length)}");
    }

    static (List<Entry> Shown, int Total) ListZip(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var shown = zip.Entries
            .OrderBy(ZipPathOf, Natural)
            .Take(MaxPreviewEntries)
            .Select(ToListEntry)
            .ToList();
        return (shown, zip.Entries.Count);
    }

    static Entry ToListEntry(ZipArchiveEntry entry)
    {
        var path = ZipPathOf(entry);
        return path.EndsWith('/')
            ? new Entry(path.TrimEnd('/'), "", true)
            : new Entry(path, FormatSize(entry.Length), false);
    }

    static (BitmapSource Image, int Width, int Height) LoadImage(string path)
    {
        using var stream = OpenShared(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        int width = frame.PixelWidth, height = frame.PixelHeight;
        var angle = RotationFromExif(frame);

        stream.Position = 0;
        var image = LoadFrozenBitmap(stream, width > MaxImageWidth ? MaxImageWidth : null);
        if (angle == 0) return (image, width, height);

        var rotated = new TransformedBitmap(image, new RotateTransform(angle));
        rotated.Freeze();
        return angle == 180 ? (rotated, width, height) : (rotated, height, width);
    }

    static int RotationFromExif(BitmapFrame frame)
    {
        try
        {
            var orientation = frame.Metadata is BitmapMetadata metadata
                              && metadata.GetQuery("System.Photo.Orientation") is ushort value ? value : 1;
            return orientation switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
        }
        catch { return 0; }
    }

    static BitmapImage LoadFrozenBitmap(Stream stream, int? decodeWidth = null)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        if (decodeWidth is { } w) bitmap.DecodePixelWidth = w;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    static string ReadText(string path)
    {
        using var reader = new StreamReader(OpenShared(path));
        var text = new StringBuilder();
        for (int n = 0; n < MaxTextLines && text.Length < MaxTextChars && reader.ReadLine() is { } line; n++)
            text.AppendLine(line);
        return text.ToString();
    }

    static async Task<(BitmapSource Image, uint Pages)> RenderFirstPdfPage(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        using var input = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await input.WriteAsync(bytes.AsBuffer());
        input.Seek(0);

        var document = await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(input);
        using var page = document.GetPage(0);
        using var output = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(output, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = PdfRenderWidth });
        return (LoadFrozenBitmap(output.AsStream()), document.PageCount);
    }

    static BitmapSource? LoadShellThumbnail(string path)
    {
        IShellItemImageFactory? factory = null;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
            if (factory.GetImage(new SIZE { cx = ThumbnailSize, cy = ThumbnailSize }, 0, out var bitmapHandle) != 0) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    bitmapHandle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally { DeleteObject(bitmapHandle); }
        }
        catch { return null; }
        finally { if (factory != null) Marshal.ReleaseComObject(factory); }
    }

    static Task<T> RunOnStaThread<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>();
        var thread = new Thread(() =>
        {
            try { completion.SetResult(work()); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    static string DescribeCount(int total) =>
        (total > CountLimit ? $"{CountLimit}+ items" : Plural(total, "item"))
        + (total > MaxPreviewEntries ? $" · showing first {MaxPreviewEntries}" : "");

    static string ZipPathOf(ZipArchiveEntry entry) => entry.FullName.Replace('\\', '/');

    static bool HasExtension(FileInfo file, string extension) =>
        file.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase);

    static string ExtensionLabel(FileInfo file) => file.Extension.TrimStart('.').ToUpperInvariant();

    static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    static string FormatSize(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} bytes" : $"{value:0.#} {units[unit]}";
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int cx, cy; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);
}
