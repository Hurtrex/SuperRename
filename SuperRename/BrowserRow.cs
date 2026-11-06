using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace SuperRename;

public sealed class BrowserRow(Preview.Entry entry, string fullPath) : INotifyPropertyChanged
{
    const double IndentPerLevel = 18;

    RenameItem? _item;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => entry.Name;
    public string Size => entry.Size;
    public bool IsDir => entry.IsDir;
    public ImageSource? Icon => entry.Icon;
    public string FullPath { get; } = fullPath;
    public bool IsParent { get; init; }
    public int Level { get; init; }
    public Thickness Indent => new(Level * IndentPerLevel, 0, 0, 0);

    public RenameItem? Item
    {
        get => _item;
        set
        {
            if (_item == value) return;
            if (_item != null) _item.PropertyChanged -= OnItemChanged;
            _item = value;
            if (_item != null) _item.PropertyChanged += OnItemChanged;
            OnItemChanged(null, new PropertyChangedEventArgs(null));
        }
    }

    public string Display => _item is { IsRenamed: true } ? $"{Name}  →  {_item.NewFullName}" : Name;
    public bool IsMarkedForDelete => _item?.IsMarkedForDelete == true;

    void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMarkedForDelete)));
    }
}
