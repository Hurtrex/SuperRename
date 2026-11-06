using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SuperRename;

public partial class MainWindow
{
    const double DefaultBrowserWidth = 330;

    int _browseVersion;
    string _browsePath = "";
    bool _isBrowsingZip;
    int _firstContentIndex;
    RenameItem? _selectedInnerItem;
    string? _selectedZipEntry;

    bool IsInsideRowSelected =>
        BrowserPanel.Visibility == Visibility.Visible && BrowserList.SelectedItem is BrowserRow { IsParent: false };

    bool IsLastRowSelected => BrowserList.SelectedIndex == BrowserList.Items.Count - 1;

    string RootBrowsePath => IsZip(CurrentItem) ? "" : CurrentItem.OriginalPath;

    bool IsAtBrowseRoot => _isBrowsingZip
        ? _browsePath.Length == 0
        : string.Equals(_browsePath, CurrentItem.OriginalPath, StringComparison.OrdinalIgnoreCase);

    static bool IsZip(RenameItem item) =>
        !item.IsDir && item.OriginalPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    static string ZipBreadcrumb(string zipPath) => zipPath.TrimEnd('/').Replace("/", " › ");

    void ResetBrowser()
    {
        ++_browseVersion;
        BrowserList.ItemsSource = null;
        _selectedInnerItem = null;
        _selectedZipEntry = null;
    }

    void SetBrowserVisible(bool visible)
    {
        if (!visible) BrowserColumn.Width = new GridLength(0);
        else if (BrowserColumn.Width.Value == 0) BrowserColumn.Width = new GridLength(DefaultBrowserWidth);
        BrowserPanel.Visibility = BrowserSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    async Task<bool> Browse(string path, bool isZip)
    {
        var version = ++_browseVersion;
        var root = CurrentItem;
        _browsePath = path;
        _isBrowsingZip = isZip;
        BreadcrumbText.Text = root.OriginalFullName + BreadcrumbSuffix(root, path, isZip);
        BrowserList.ItemsSource = null;

        var folderRows = FolderPathRows(root, path, isZip);
        var contentLevel = folderRows.Count + 1;
        List<BrowserRow> contentRows = [];
        string? readError = null;
        try
        {
            contentRows = await Task.Run(() => ListContentRows(root.OriginalPath, path, isZip, contentLevel));
        }
        catch (Exception ex)
        {
            readError = ex.Message;
        }
        if (version != _browseVersion) return false;

        if (readError != null) BreadcrumbText.Text += $" (can't read: {readError})";
        List<BrowserRow> rows = [ParentRow(root), .. folderRows, .. contentRows];
        _firstContentIndex = 1 + folderRows.Count;
        if (!isZip)
        {
            foreach (var row in rows)
                if (_itemsByPath.TryGetValue(row.FullPath, out var item)) row.Item = item;
        }
        BrowserList.ItemsSource = rows;
        return true;
    }

    string BreadcrumbSuffix(RenameItem root, string path, bool isZip)
    {
        if (IsAtBrowseRoot) return "";
        var relative = isZip ? ZipBreadcrumb(path) : Path.GetRelativePath(root.OriginalPath, path).Replace("\\", " › ");
        return " › " + relative;
    }

    static BrowserRow ParentRow(RenameItem root) =>
        new(new Preview.Entry(root.OriginalFullName, "", root.IsDir), root.OriginalPath) { IsParent = true, Item = root };

    static List<BrowserRow> FolderPathRows(RenameItem root, string path, bool isZip)
    {
        var rows = new List<BrowserRow>();
        var relative = isZip ? path.TrimEnd('/') : Path.GetRelativePath(root.OriginalPath, path);
        if (relative is "" or ".") return rows;

        var current = isZip ? "" : root.OriginalPath;
        foreach (var name in relative.Split(isZip ? '/' : '\\'))
        {
            current = isZip ? current + name + "/" : Path.Combine(current, name);
            rows.Add(new BrowserRow(new Preview.Entry(name, "", true), current) { Level = rows.Count + 1 });
        }
        return rows;
    }

    static List<BrowserRow> ListContentRows(string rootPath, string path, bool isZip, int level) => isZip
        ? Preview.ListZipFolder(rootPath, path)
            .Select(e => new BrowserRow(e, path + e.Name + (e.IsDir ? "/" : "")) { Level = level })
            .ToList()
        : Preview.ListFolder(path, Preview.MaxBrowserEntries).Shown
            .Select(e => new BrowserRow(e, Path.Combine(path, e.Name)) { Level = level })
            .ToList();

    async Task SelectParent()
    {
        bool isRootListShown = IsAtBrowseRoot && BrowserList.ItemsSource != null;
        if (isRootListShown || await Browse(RootBrowsePath, IsZip(CurrentItem))) SelectRow(0);
    }

    void SelectRow(int index)
    {
        if (index < 0 || index >= BrowserList.Items.Count) return;
        BrowserList.SelectedIndex = index;
        BrowserList.ScrollIntoView(BrowserList.SelectedItem);
    }

    void SelectRow(string fullPath)
    {
        var rows = (List<BrowserRow>?)BrowserList.ItemsSource ?? [];
        var index = rows.FindIndex(r => !r.IsParent && string.Equals(r.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
        SelectRow(index);
    }

    async void Up_Click(object sender, RoutedEventArgs e) => await SelectParent();

    async void Breadcrumb_Click(object sender, MouseButtonEventArgs e) => await SelectParent();

    void BrowserList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        switch (BrowserList.SelectedItem)
        {
            case BrowserRow { IsParent: false } row when _isBrowsingZip:
                SelectZipEntry(row);
                break;
            case BrowserRow { IsParent: false } row:
                SelectInnerItem(row);
                break;
            default:
                ShowCurrentItemInEditor();
                break;
        }
    }

    void ShowCurrentItemInEditor()
    {
        _selectedInnerItem = null;
        _selectedZipEntry = null;
        ShowEditor();
        if (BrowserPanel.Visibility == Visibility.Visible) ShowBrowseHint();
    }

    void SelectZipEntry(BrowserRow row)
    {
        var zipPath = CurrentItem.OriginalPath;
        _selectedInnerItem = null;
        _selectedZipEntry = row.FullPath;
        ShowEditor();

        if (row.IsDir)
        {
            LoadPreview(async () =>
            {
                var entries = await Task.Run(() => Preview.ListZipFolder(zipPath, row.FullPath));
                return new Preview.Result(null, null, Preview.Plural(entries.Count, "item"), entries);
            });
        }
        else
        {
            LoadPreview(async () =>
            {
                var extracted = await Task.Run(() => Preview.ExtractZipEntry(zipPath, row.FullPath));
                return await Preview.LoadAsync(extracted, false);
            });
        }
    }

    void SelectInnerItem(BrowserRow row)
    {
        _selectedZipEntry = null;
        try
        {
            _selectedInnerItem = GetOrCreateInnerItem(row.FullPath);
        }
        catch (Exception ex)
        {
            _selectedInnerItem = null;
            ShowEditor();
            ErrorText.Text = $"Can't open {row.Name}: {ex.Message}";
            return;
        }
        row.Item = _selectedInnerItem;
        ShowEditor();
        LoadPreview(() => Preview.LoadAsync(row.FullPath, row.IsDir));
    }

    async void BrowserRow_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not BrowserRow row) return;
        if (row.IsParent)
        {
            await SelectParent();
            return;
        }
        if (!row.IsDir) return;
        if (row.IsMarkedForDelete)
        {
            ErrorText.Text = "This folder is marked for delete. Undo delete to open it.";
            return;
        }

        bool isGoingDeeper = BrowserList.Items.IndexOf(row) >= _firstContentIndex;
        if (!await Browse(row.FullPath, _isBrowsingZip)) return;
        if (isGoingDeeper) SelectRow(Math.Min(_firstContentIndex, BrowserList.Items.Count - 1));
        else SelectRow(row.FullPath);
    }

    RenameItem GetOrCreateInnerItem(string path)
    {
        if (_itemsByPath.TryGetValue(path, out var existing)) return existing;
        var item = new RenameItem(path);
        Track(item);
        _innerItems.Add(item);
        return item;
    }
}
