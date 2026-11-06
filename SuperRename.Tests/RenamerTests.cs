using SuperRename;
using Xunit;

public sealed class RenamerTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("superrename_test_").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    string Touch(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, name);
        return p;
    }

    RenameItem Item(string name) => new(Path.Combine(_dir, name));

    [Theory]
    [InlineData("", "txt")]
    [InlineData("a?b", "txt")]
    [InlineData("a/b", "txt")]
    [InlineData("name.", "")]
    [InlineData("name ", "")]
    [InlineData("CON", "txt")]
    public void RejectsInvalidNames(string name, string ext)
    {
        Touch("a.txt");
        var it = Item("a.txt");
        it.NewName = name;
        it.NewExt = ext;
        Assert.False(Renamer.Validate([it]));
        Assert.NotNull(it.Error);
    }

    [Fact]
    public void SplitsNameAndExtension()
    {
        Touch("photo.final.jpg");
        Touch(".gitignore");
        var a = Item("photo.final.jpg");
        var b = Item(".gitignore");
        Assert.Equal(("photo.final", "jpg"), (a.OriginalName, a.OriginalExt));
        Assert.Equal((".gitignore", ""), (b.OriginalName, b.OriginalExt));
        a.NewExt = ".png";
        Assert.Equal("photo.final.png", a.NewFullName);
    }

    [Fact]
    public void BlocksDuplicatesAndExistingFiles()
    {
        Touch("a.txt"); Touch("b.txt"); Touch("c.txt"); Touch("outside.txt");
        var a = Item("a.txt"); var b = Item("b.txt"); var c = Item("c.txt");
        a.NewName = "x"; b.NewName = "x";
        c.NewName = "outside";
        Assert.False(Renamer.Validate([a, b, c]));
        Assert.All(new[] { a, b, c }, i => Assert.NotNull(i.Error));

        a.NewName = "b";
        b.Revert(); c.Revert();
        Assert.False(Renamer.Validate([a, b, c]));
        Assert.NotNull(a.Error);
    }

    [Fact]
    public void SwapsTwoNames()
    {
        Touch("a.txt"); Touch("b.txt");
        var a = Item("a.txt"); var b = Item("b.txt");
        a.NewName = "b"; b.NewName = "a";
        Assert.True(Renamer.Validate([a, b]));
        Assert.Null(Renamer.Commit([a, b]));
        Assert.Equal("a.txt", File.ReadAllText(Path.Combine(_dir, "b.txt")));
        Assert.Equal("b.txt", File.ReadAllText(Path.Combine(_dir, "a.txt")));
    }

    [Fact]
    public void CaseOnlyRename()
    {
        Touch("a.txt");
        var a = Item("a.txt");
        a.NewName = "A";
        Assert.True(Renamer.Validate([a]));
        Assert.Null(Renamer.Commit([a]));
        Assert.Equal("A.txt", Path.GetFileName(Directory.GetFiles(_dir).Single()));
    }

    [Fact]
    public void RenamesFileInsideRenamedFolder()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "F"));
        Touch(Path.Combine("F", "x.txt"));
        var folder = Item("F");
        var file = Item(Path.Combine("F", "x.txt"));
        folder.NewName = "G";
        file.NewName = "y";
        Assert.True(Renamer.Validate([folder, file]));
        Assert.Null(Renamer.Commit([folder, file]));
        Assert.True(File.Exists(Path.Combine(_dir, "G", "y.txt")));
        Assert.False(Directory.Exists(Path.Combine(_dir, "F")));
    }

    [Fact]
    public void RollsBackWhenAFileIsLocked()
    {
        Touch("a.txt");
        var bPath = Touch("b.txt");
        var a = Item("a.txt"); var b = Item("b.txt");
        a.NewName = "c"; b.NewName = "d";
        Assert.True(Renamer.Validate([a, b]));

        using (new FileStream(bPath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.NotNull(Renamer.Commit([a, b]));

        Assert.Equal(new[] { "a.txt", "b.txt" }, Directory.GetFiles(_dir).Select(Path.GetFileName).Order());
    }

    static void Remove(string path, bool isDir)
    {
        if (isDir) Directory.Delete(path, true); else File.Delete(path);
    }

    [Fact]
    public void DeleteFreesNameForRename()
    {
        Touch("a.txt"); Touch("b.txt");
        var a = Item("a.txt"); var b = Item("b.txt");
        a.IsMarkedForDelete = true;
        b.NewName = "a";
        Assert.True(Renamer.Validate([a, b]));
        Assert.Null(Renamer.Commit([a, b], Remove));
        Assert.Equal("b.txt", File.ReadAllText(Path.Combine(_dir, "a.txt")));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void DeletedFolderTakesItsSelectedContentsWithIt()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "F"));
        Touch(Path.Combine("F", "x.txt"));
        Touch("keep.txt");
        var folder = Item("F");
        var inside = Item(Path.Combine("F", "x.txt"));
        var keep = Item("keep.txt");
        folder.IsMarkedForDelete = true;
        inside.IsMarkedForDelete = true;
        keep.NewName = "kept";
        Assert.True(Renamer.Validate([folder, inside, keep]));
        Assert.Null(Renamer.Commit([folder, inside, keep], Remove));
        Assert.False(Directory.Exists(Path.Combine(_dir, "F")));
        Assert.True(File.Exists(Path.Combine(_dir, "kept.txt")));
    }

    [Fact]
    public void RevertClearsDelete()
    {
        Touch("a.txt");
        var a = Item("a.txt");
        a.IsMarkedForDelete = true;
        Assert.True(a.IsChanged);
        a.Revert();
        Assert.False(a.IsChanged);
    }

    [Fact]
    public void UnchangedItemsAreLeftAlone()
    {
        Touch("a.txt");
        var a = Item("a.txt");
        Assert.True(Renamer.Validate([a]));
        Assert.Null(Renamer.Commit([a]));
        Assert.True(File.Exists(Path.Combine(_dir, "a.txt")));
    }
}
