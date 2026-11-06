using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SuperRename;

public partial class MainWindow : Window
{
    readonly ObservableCollection<RenameItem> _items;
    readonly List<RenameItem> _innerItems = [];
    readonly Dictionary<string, RenameItem> _itemsByPath = new(StringComparer.OrdinalIgnoreCase);
    int _index;
    int _previewVersion;
    bool _isFillingEditor;
    bool _isFinished;

    public MainWindow(IEnumerable<RenameItem> items)
    {
        InitializeComponent();
        _items = new ObservableCollection<RenameItem>(items);
        foreach (var item in _items) Track(item);
        ReviewGrid.ItemsSource = _reviewRows;
        ThemeBox.SelectedIndex = App.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        Revalidate();
        ShowItem(0);
    }

    RenameItem CurrentItem => _items[_index];
    RenameItem EditedItem => _selectedInnerItem ?? CurrentItem;
    List<RenameItem> AllItems => [.. _items, .. _innerItems];

    void Track(RenameItem item)
    {
        item.PropertyChanged += Item_PropertyChanged;
        _itemsByPath[item.OriginalPath] = item;
    }

    void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RenameItem.NewName) or nameof(RenameItem.NewExt) or nameof(RenameItem.IsMarkedForDelete))
            Revalidate();
    }

    void Revalidate()
    {
        var all = AllItems;
        bool isValid = Renamer.Validate(all);
        FinishButton.IsEnabled = isValid;

        int renamed = all.Count(i => i.IsRenamed);
        int deleted = all.Count(i => i.IsMarkedForDelete);
        var summary = isValid
            ? $"{renamed} will be renamed" + (deleted > 0 ? $" · {deleted} will be deleted" : "")
            : $"{all.Count(i => i.HasError)} problem(s) to fix before Finish";
        SummaryText.Text = summary;
        ReviewSummary.Text = summary;
        ErrorText.Text = EditorMessage();
    }

    string EditorMessage() =>
        _selectedZipEntry != null ? "Files inside a zip can only be previewed."
        : EditedItem.IsMarkedForDelete ? "Will be deleted (moved to the Recycle Bin) when you press Finish."
        : EditedItem.Error ?? "";

    void ShowItem(int index)
    {
        _index = index;
        var item = CurrentItem;
        CounterText.Text = $"{index + 1} / {_items.Count}";
        ResetBrowser();

        bool isBrowsable = item.IsDir || IsZip(item);
        SetBrowserVisible(isBrowsable);
        ShowEditor();

        if (isBrowsable)
        {
            ShowBrowseHint();
            _ = SelectParent();
        }
        else
        {
            LoadPreview(() => Preview.LoadAsync(item.OriginalPath, item.IsDir));
        }
    }

    void ShowEditor()
    {
        _isFillingEditor = true;
        if (_selectedZipEntry != null) FillEditorForZipEntry(_selectedZipEntry);
        else FillEditor(EditedItem);
        _isFillingEditor = false;

        ErrorText.Text = EditorMessage();
        BrowserList.IsEnabled = !CurrentItem.IsMarkedForDelete;
        BackButton.IsEnabled = IsInsideRowSelected || _index > 0;
        NextButton.IsEnabled = !(IsInsideRowSelected && IsLastRowSelected && !IsAtBrowseRoot);
        bool nextLeavesItem = !IsInsideRowSelected || (IsLastRowSelected && IsAtBrowseRoot);
        NextButton.Content = nextLeavesItem && _index == _items.Count - 1 ? "Review" : "Next";
        Dispatcher.BeginInvoke(() => FocusNameBox(), DispatcherPriority.Input);
    }

    void FillEditor(RenameItem item)
    {
        OriginalText.Text = item.OriginalFullName;
        FolderText.Text = item.Folder;
        NameBox.Text = item.NewName;
        ExtBox.Text = item.NewExt;
        NameBox.IsEnabled = !item.IsMarkedForDelete;
        ExtBox.IsEnabled = !item.IsDir && !item.IsMarkedForDelete;
        DeleteButton.IsEnabled = true;
        DeleteButton.Content = item.DeleteLabel;
    }

    void FillEditorForZipEntry(string entryPath)
    {
        var name = entryPath.TrimEnd('/').Split('/')[^1];
        OriginalText.Text = name;
        FolderText.Text = CurrentItem.OriginalPath + " › " + ZipBreadcrumb(entryPath);
        NameBox.Text = name;
        ExtBox.Text = "";
        NameBox.IsEnabled = ExtBox.IsEnabled = DeleteButton.IsEnabled = false;
        DeleteButton.Content = "Delete";
    }

    void FocusNameBox()
    {
        NameBox.Focus();
        NameBox.CaretIndex = NameBox.Text.Length;
    }

    async void LoadPreview(Func<Task<Preview.Result>> load)
    {
        var version = ++_previewVersion;
        ClearPreview();
        PreviewInfo.Text = "Loading preview…";

        Preview.Result result;
        try { result = await load(); }
        catch (Exception ex) { result = new Preview.Result(null, null, "No preview: " + ex.Message); }
        if (version != _previewVersion) return;

        PreviewImage.Source = result.Image;
        PreviewText.Text = result.Text ?? "";
        PreviewText.Visibility = result.Text != null ? Visibility.Visible : Visibility.Collapsed;
        PreviewList.ItemsSource = result.Entries;
        PreviewList.Visibility = result.Entries != null ? Visibility.Visible : Visibility.Collapsed;
        PreviewInfo.Text = result.Info;
    }

    void ShowBrowseHint()
    {
        ++_previewVersion;
        ClearPreview();
        PreviewHint.Visibility = Visibility.Visible;
    }

    void ClearPreview()
    {
        PreviewImage.Source = null;
        PreviewText.Visibility = Visibility.Collapsed;
        PreviewList.ItemsSource = null;
        PreviewList.Visibility = Visibility.Collapsed;
        PreviewHint.Visibility = Visibility.Collapsed;
        PreviewInfo.Text = "";
    }

    void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isFillingEditor && _selectedZipEntry == null) EditedItem.NewName = NameBox.Text;
    }

    void ExtBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isFillingEditor && _selectedZipEntry == null) EditedItem.NewExt = ExtBox.Text;
    }

    void Box_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Next_Click(sender, e);
    }

    void Next_Click(object sender, RoutedEventArgs e)
    {
        if (!NextButton.IsEnabled) return;
        if (IsInsideRowSelected && !IsLastRowSelected) SelectRow(BrowserList.SelectedIndex + 1);
        else if (_index < _items.Count - 1) ShowItem(_index + 1);
        else ShowReview();
    }

    void Back_Click(object sender, RoutedEventArgs e)
    {
        if (IsInsideRowSelected) SelectRow(BrowserList.SelectedIndex - 1);
        else if (_index > 0) ShowItem(_index - 1);
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        var item = EditedItem;
        item.IsMarkedForDelete = !item.IsMarkedForDelete;
        ShowEditor();
        if (item.IsMarkedForDelete) Next_Click(sender, e);
    }

    void Review_Click(object sender, RoutedEventArgs e) => ShowReview();

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeBox.SelectedItem is ComboBoxItem { Content: string name }) App.SetTheme(name);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isFinished && AllItems.Any(i => i.IsChanged) &&
            MessageBox.Show(this, "Close without renaming? Your changes will be lost.", "SuperRename",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        DeleteTempFolders();
        base.OnClosed(e);
    }

    static void DeleteTempFolders()
    {
        var parent = Path.GetDirectoryName(Preview.TempRoot)!;
        if (!Directory.Exists(parent)) return;

        foreach (var folder in Directory.EnumerateDirectories(parent))
        {
            bool isOwnedByOtherRunningWindow = folder != Preview.TempRoot
                && int.TryParse(Path.GetFileName(folder), out var processId)
                && IsRunning(processId);
            if (isOwnedByOtherRunningWindow) continue;

            try { Directory.Delete(folder, true); }
            catch { }
        }
    }

    static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch { return false; }
    }
}
