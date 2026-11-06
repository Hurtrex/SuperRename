using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SuperRename;

public sealed class RenameItem : INotifyPropertyChanged
{
    static readonly string[] DerivedProperties =
    [
        nameof(NewFullName), nameof(IsRenamed), nameof(IsChanged), nameof(HasError),
        nameof(Status), nameof(DeleteLabel), nameof(DisplayName),
    ];

    string _newName;
    string _newExt;
    string? _error;
    bool _isMarkedForDelete;
    int _indent;

    public RenameItem(string path)
    {
        OriginalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        IsDir = Directory.Exists(OriginalPath);
        Folder = Path.GetDirectoryName(OriginalPath)
                 ?? throw new ArgumentException("Drive roots can't be renamed.", nameof(path));
        OriginalFullName = Path.GetFileName(OriginalPath);
        (OriginalName, OriginalExt) = SplitExtension(OriginalFullName, IsDir);
        _newName = OriginalName;
        _newExt = OriginalExt;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string OriginalPath { get; }
    public string Folder { get; }
    public bool IsDir { get; }
    public string OriginalFullName { get; }
    public string OriginalName { get; }
    public string OriginalExt { get; }

    public string NewName { get => _newName; set => Set(ref _newName, value ?? ""); }
    public string NewExt { get => _newExt; set => Set(ref _newExt, IsDir ? "" : value ?? ""); }
    public string? Error { get => _error; set => Set(ref _error, value); }
    public bool IsMarkedForDelete { get => _isMarkedForDelete; set => Set(ref _isMarkedForDelete, value); }
    public int Indent { get => _indent; set => Set(ref _indent, value); }

    public string NewFullName
    {
        get
        {
            var ext = NewExt.TrimStart('.');
            return ext.Length == 0 ? NewName : $"{NewName}.{ext}";
        }
    }

    public string NewPath => Path.Combine(Folder, NewFullName);
    public bool IsRenamed => !IsMarkedForDelete && NewFullName != OriginalFullName;
    public bool IsChanged => IsMarkedForDelete || IsRenamed;
    public bool HasError => Error != null;
    public string Status => IsMarkedForDelete ? "Will be deleted" : Error ?? (IsRenamed ? "Will rename" : "Unchanged");
    public string DeleteLabel => IsMarkedForDelete ? "Undo delete" : "Delete";
    public int Depth => OriginalPath.Count(c => c == Path.DirectorySeparatorChar);

    public bool IsInside(RenameItem folder) =>
        folder.IsDir && OriginalPath.StartsWith(folder.OriginalPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public string DisplayName => Indent == 0
        ? OriginalFullName
        : new string('\u2003', Indent - 1) + "└ " + OriginalFullName;

    public void Revert()
    {
        NewName = OriginalName;
        NewExt = OriginalExt;
        IsMarkedForDelete = false;
    }

    static (string Name, string Ext) SplitExtension(string fullName, bool isDir)
    {
        var ext = isDir ? "" : Path.GetExtension(fullName);
        var name = fullName[..^ext.Length];
        return name.Length == 0 ? (fullName, "") : (name, ext.TrimStart('.'));
    }

    void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        foreach (var derived in DerivedProperties)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(derived));
    }
}
