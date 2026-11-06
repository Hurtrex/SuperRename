using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Windows;

namespace SuperRename;

public partial class App : Application
{
    const int CollectAttempts = 3;
    const int SendTimeoutMs = 5000;
    static readonly TimeSpan CollectQuietPeriod = TimeSpan.FromMilliseconds(700);
    static readonly string PipeName = "SuperRename-" + Environment.UserName;
    static readonly string ThemeFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SuperRename", "theme.txt");

    public static string Theme { get; private set; } = "System";

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    internal static extern int StrCmpLogicalW(string a, string b);

    public static void SetTheme(string name)
    {
        ApplyTheme(name);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ThemeFile)!);
            File.WriteAllText(ThemeFile, Theme);
        }
        catch { }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyTheme(ReadSavedTheme());

        var paths = await CollectPathsFromAllInstances(e.Args);
        if (paths == null)
        {
            Shutdown();
            return;
        }

        var items = LoadItems(paths);
        if (items.Count == 0)
        {
            ShowUsageHint();
            Shutdown();
            return;
        }
        new MainWindow(items).Show();
    }

    static void ApplyTheme(string name)
    {
        Theme = name is "Light" or "Dark" ? name : "System";
        Current.ThemeMode = new ThemeMode(Theme);
    }

    static string ReadSavedTheme()
    {
        try { return File.ReadAllText(ThemeFile).Trim(); }
        catch { return "System"; }
    }

    static void ShowUsageHint() => MessageBox.Show(
        "Select files or folders in File Explorer, right-click, and choose SuperRename.\n\n" +
        "On Windows 11 it is under \"Show more options\" (or Shift + right-click).",
        "SuperRename", MessageBoxButton.OK, MessageBoxImage.Information);

    static List<RenameItem> LoadItems(IEnumerable<string> paths)
    {
        var items = new List<RenameItem>();
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path) || Directory.Exists(path)) items.Add(new RenameItem(path));
            }
            catch { }
        }
        return items
            .DistinctBy(i => i.OriginalPath, StringComparer.OrdinalIgnoreCase)
            .Order(Comparer<RenameItem>.Create(CompareLikeExplorer))
            .ToList();
    }

    static int CompareLikeExplorer(RenameItem a, RenameItem b)
    {
        var byName = StrCmpLogicalW(a.OriginalFullName, b.OriginalFullName);
        return byName != 0 ? byName : StrCmpLogicalW(a.OriginalPath, b.OriginalPath);
    }

    static async Task<List<string>?> CollectPathsFromAllInstances(string[] ownPaths)
    {
        for (int attempt = 0; attempt < CollectAttempts; attempt++)
        {
            var server = TryBecomeCollector();
            if (server == null)
            {
                if (TrySendToCollector(ownPaths)) return null;
                continue;
            }
            using (server) return await ReceivePaths(server, ownPaths);
        }
        return [.. ownPaths];
    }

    static NamedPipeServerStream? TryBecomeCollector()
    {
        try
        {
            return new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static bool TrySendToCollector(string[] paths)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(SendTimeoutMs);
            using var writer = new StreamWriter(client);
            foreach (var path in paths) writer.WriteLine(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static async Task<List<string>> ReceivePaths(NamedPipeServerStream server, string[] ownPaths)
    {
        var paths = new List<string>(ownPaths);
        while (true)
        {
            using var quietPeriod = new CancellationTokenSource(CollectQuietPeriod);
            try { await server.WaitForConnectionAsync(quietPeriod.Token); }
            catch (OperationCanceledException) { return paths; }

            await ReadPaths(server, paths);
            server.Disconnect();
        }
    }

    static async Task ReadPaths(Stream stream, List<string> paths)
    {
        try
        {
            using var reader = new StreamReader(stream, leaveOpen: true);
            while (await reader.ReadLineAsync() is { } line)
                if (line.Length > 0) paths.Add(line);
        }
        catch (IOException) { }
    }
}
