using Microsoft.VisualBasic.FileIO;

namespace SuperRename;

public static class Renamer
{
    const string TempNamePrefix = "~superrename_";

    static readonly char[] InvalidNameChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    readonly record struct Move(string From, string To, bool IsDir);

    public static string? CheckName(RenameItem item)
    {
        var fullName = item.NewFullName;
        if (item.NewName.Length == 0) return "Name can't be empty.";
        if (fullName.IndexOfAny(InvalidNameChars) >= 0 || fullName.Any(char.IsControl))
            return "Name can't contain \\ / : * ? \" < > |";
        if (fullName.EndsWith('.') || fullName.EndsWith(' ')) return "Name can't end with a dot or a space.";
        if (ReservedNames.Contains(fullName.Split('.')[0].TrimEnd())) return "This name is reserved by Windows.";
        return null;
    }

    public static bool Validate(IReadOnlyList<RenameItem> items)
    {
        var deleted = items.Where(i => i.IsMarkedForDelete).ToList();
        var originalPaths = new HashSet<string>(items.Select(i => i.OriginalPath), StringComparer.OrdinalIgnoreCase);
        var targetCounts = items
            .Where(i => !i.IsMarkedForDelete)
            .GroupBy(i => i.NewPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            item.Error = item.IsRenamed && !IsInsideAny(item, deleted)
                ? FindProblem(item, targetCounts, originalPaths)
                : null;
        }
        return items.All(i => i.Error == null);
    }

    public static string? Commit(IReadOnlyList<RenameItem> items, Action<string, bool>? delete = null)
    {
        var deleted = items.Where(i => i.IsMarkedForDelete).ToList();
        var recycled = new List<string>();

        var deleteError = DeleteAll(deleted, delete ?? MoveToRecycleBin, recycled);
        if (deleteError != null) return deleteError;

        var toRename = items.Where(i => i.IsRenamed && !IsInsideAny(i, deleted)).ToList();
        var renameError = RenameAll(toRename);
        return renameError == null ? null : renameError + RecycledNote(recycled);
    }

    static string? FindProblem(RenameItem item, Dictionary<string, int> targetCounts, HashSet<string> originalPaths)
    {
        if (CheckName(item) is { } nameProblem) return nameProblem;
        if (targetCounts[item.NewPath] > 1) return "Same name as another item in this list.";
        if (!originalPaths.Contains(item.NewPath) && Exists(item.NewPath))
            return "A file or folder with this name already exists here.";
        return null;
    }

    static string? DeleteAll(List<RenameItem> deleted, Action<string, bool> delete, List<string> recycled)
    {
        foreach (var item in deleted.Where(d => !IsInsideAny(d, deleted) && Exists(d.OriginalPath)))
        {
            try
            {
                delete(item.OriginalPath, item.IsDir);
                recycled.Add(item.OriginalPath);
            }
            catch (Exception ex)
            {
                return $"Could not delete:\n{item.OriginalPath}\n\nNothing was renamed.{RecycledNote(recycled)}\n\n{ex.Message}";
            }
        }
        return null;
    }

    static string? RenameAll(List<RenameItem> items)
    {
        var done = new List<Move>();
        try
        {
            foreach (var sameDepth in items.GroupBy(i => i.Depth).OrderByDescending(g => g.Key))
                RenameThroughTempNames(sameDepth.ToList(), done);
            return null;
        }
        catch (Exception ex)
        {
            var stuck = Undo(done);
            return stuck.Count == 0
                ? $"Nothing was renamed. All files keep their original names.\n\n{ex.Message}"
                : $"Rename failed and these items could not be put back. Rename them by hand:\n\n{string.Join("\n", stuck)}\n\n{ex.Message}";
        }
    }

    static void RenameThroughTempNames(List<RenameItem> items, List<Move> done)
    {
        var tempPaths = items.Select(i => Path.Combine(i.Folder, TempNamePrefix + Guid.NewGuid().ToString("N"))).ToList();
        for (int i = 0; i < items.Count; i++)
            MoveAndRecord(new Move(items[i].OriginalPath, tempPaths[i], items[i].IsDir), done);
        for (int i = 0; i < items.Count; i++)
            MoveAndRecord(new Move(tempPaths[i], items[i].NewPath, items[i].IsDir), done);
    }

    static List<string> Undo(List<Move> done)
    {
        var stuck = new List<string>();
        foreach (var move in Enumerable.Reverse(done))
        {
            try { MovePath(move.To, move.From, move.IsDir); }
            catch { stuck.Add($"{move.To}  (should be {move.From})"); }
        }
        return stuck;
    }

    static string RecycledNote(List<string> recycled) => recycled.Count == 0
        ? ""
        : $"\n\nThese were already moved to the Recycle Bin (restore them from there if needed):\n{string.Join("\n", recycled)}";

    static bool IsInsideAny(RenameItem item, List<RenameItem> folders) => folders.Any(item.IsInside);

    static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    static void MoveToRecycleBin(string path, bool isDir)
    {
        if (isDir)
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        else
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
    }

    static void MoveAndRecord(Move move, List<Move> done)
    {
        MovePath(move.From, move.To, move.IsDir);
        done.Add(move);
    }

    static void MovePath(string from, string to, bool isDir)
    {
        if (isDir) Directory.Move(from, to);
        else File.Move(from, to);
    }
}
