using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SuperRename;

public partial class MainWindow
{
    const int MaxNamesInDeleteConfirm = 15;

    static readonly IComparer<string> FolderByFolder = Comparer<string>.Create(CompareFolderByFolder);

    readonly ObservableCollection<RenameItem> _reviewRows = [];

    void ShowReview()
    {
        BuildReviewRows();
        OnePanel.Visibility = Visibility.Collapsed;
        ReviewPanel.Visibility = Visibility.Visible;
        ReviewGrid.SelectedItem = _reviewRows.Contains(EditedItem) ? EditedItem : CurrentItem;
        ReviewGrid.ScrollIntoView(ReviewGrid.SelectedItem);
        ReviewGrid.Focus();
    }

    void BuildReviewRows()
    {
        _reviewRows.Clear();
        foreach (var item in _items)
        {
            item.Indent = 0;
            _reviewRows.Add(item);
            if (!item.IsDir) continue;

            var changedInside = _innerItems
                .Where(i => i.IsChanged && OwnerOf(i) == item)
                .OrderBy(i => i.OriginalPath, FolderByFolder);
            foreach (var inner in changedInside)
            {
                inner.Indent = inner.Depth - item.Depth;
                _reviewRows.Add(inner);
            }
        }
    }

    RenameItem? OwnerOf(RenameItem inner) => _items.Where(inner.IsInside).MaxBy(i => i.OriginalPath.Length);

    static int CompareFolderByFolder(string a, string b)
    {
        var aParts = a.Split('\\');
        var bParts = b.Split('\\');
        for (int i = 0; i < Math.Min(aParts.Length, bParts.Length); i++)
        {
            var result = App.StrCmpLogicalW(aParts[i], bParts[i]);
            if (result != 0) return result;
        }
        return aParts.Length.CompareTo(bParts.Length);
    }

    void BackToOne_Click(object sender, RoutedEventArgs e) =>
        OpenOneByOne(ReviewGrid.SelectedItem as RenameItem ?? CurrentItem);

    void Original_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is RenameItem item) OpenOneByOne(item);
    }

    async void OpenOneByOne(RenameItem item)
    {
        ReviewGrid.CommitEdit(DataGridEditingUnit.Row, true);
        ReviewPanel.Visibility = Visibility.Collapsed;
        OnePanel.Visibility = Visibility.Visible;

        var index = _items.IndexOf(item);
        if (index >= 0)
        {
            ShowItem(index);
            return;
        }
        if (OwnerOf(item) is not { } owner) return;
        ShowItem(_items.IndexOf(owner));
        if (await Browse(item.Folder, isZip: false)) SelectRow(item.OriginalPath);
    }

    void RowDelete_Click(object sender, RoutedEventArgs e)
    {
        ReviewGrid.CancelEdit(DataGridEditingUnit.Row);
        if (((FrameworkElement)sender).DataContext is RenameItem item) item.IsMarkedForDelete = !item.IsMarkedForDelete;
    }

    void Revert_Click(object sender, RoutedEventArgs e)
    {
        ReviewGrid.CancelEdit(DataGridEditingUnit.Row);
        if (((FrameworkElement)sender).DataContext is RenameItem item) item.Revert();
    }

    void Finish_Click(object sender, RoutedEventArgs e)
    {
        ReviewGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var all = AllItems;
        if (!Renamer.Validate(all))
        {
            Revalidate();
            return;
        }
        if (!ConfirmDeletes(all)) return;

        ClearPreview();
        var error = Renamer.Commit(all);
        if (error != null)
        {
            MessageBox.Show(this, error, "SuperRename", MessageBoxButton.OK, MessageBoxImage.Error);
            Revalidate();
            return;
        }
        _isFinished = true;
        Close();
    }

    bool ConfirmDeletes(List<RenameItem> all)
    {
        var toDelete = all.Where(i => i.IsMarkedForDelete).ToList();
        if (toDelete.Count == 0) return true;

        var names = string.Join("\n", toDelete.Take(MaxNamesInDeleteConfirm).Select(i => "• " + DisplayPath(i)));
        if (toDelete.Count > MaxNamesInDeleteConfirm) names += $"\n…and {toDelete.Count - MaxNamesInDeleteConfirm} more";

        var renameCount = all.Count(i => i.IsRenamed);
        var message = $"Move {toDelete.Count} item(s) to the Recycle Bin?\n\n{names}"
                      + (renameCount > 0 ? $"\n\nThen {renameCount} item(s) will be renamed." : "");
        return MessageBox.Show(this, message, "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    string DisplayPath(RenameItem item) => !_items.Contains(item) && OwnerOf(item) is { } owner
        ? Path.GetRelativePath(owner.Folder, item.OriginalPath)
        : item.OriginalFullName;
}
