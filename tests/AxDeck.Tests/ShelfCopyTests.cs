using AxDeck.Core;

// Copying a transfer page file to another folder on this PC: the copies take a free name, the originals stay, and one file that
// cannot be copied does not stop the rest.
static class ShelfCopyTests
{
    public static void Run(Action<string, bool> check)
    {
        Planning(check);
        Copying(check);
    }

    private static string Folder(string name)
    {
        var folder = Path.Combine(Path.GetTempPath(), "AxDeck-Tests-shelfcopy-" + Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string Write(string folder, string name, string text = "x")
    {
        var file = Path.Combine(folder, name);
        File.WriteAllText(file, text);
        return file;
    }

    private static void Planning(Action<string, bool> check)
    {
        var from = Folder("from");
        var to = Folder("to");
        var one = Write(from, "report.txt");
        var two = Write(from, "photo.png");
        // Nothing is written while the names are worked out.
        var plan = ShelfCopy.Plan([one, two], to);
        check("ShelfCopy: a plan names one destination per source", plan.Count == 2
            && plan[0] == Path.Combine(to, "report.txt") && plan[1] == Path.Combine(to, "photo.png"));
        check("ShelfCopy: a plan writes nothing", !File.Exists(plan[0]) && !File.Exists(plan[1]) && Directory.GetFiles(to).Length == 0);
        // A file of that name in the folder is kept, and the copy takes the next name.
        File.WriteAllText(Path.Combine(to, "report.txt"), "kept");
        var busy = ShelfCopy.Plan([one], to);
        check("ShelfCopy: a name already taken is not reused", busy.Count == 1 && Path.GetFileName(busy[0]) == "report (2).txt");
        // Two sources of one name in the same batch do not land on the same file.
        var first = Folder("first"); var second = Folder("second");
        var a = Write(first, "same.txt", "a"); var b = Write(second, "same.txt", "b");
        var twice = ShelfCopy.Plan([a, b], Folder("empty-to"));
        check("ShelfCopy: two files of one name get two names", twice.Count == 2 && Path.GetFileName(twice[0]) == "same.txt" && Path.GetFileName(twice[1]) == "same (2).txt");
        // A source that has gone, an empty folder, and a folder that lies inside the source are not planned.
        check("ShelfCopy: a source that has gone is left out", ShelfCopy.Plan([Path.Combine(from, "absent.txt")], to).Count == 0);
        check("ShelfCopy: copying to no folder plans nothing", ShelfCopy.Plan([one], "").Count == 0);
        check("ShelfCopy: copying to a folder that is not there plans nothing", ShelfCopy.Plan([one], Path.Combine(to, "absent")).Count == 0);
        var parent = Folder("parent");
        Directory.CreateDirectory(Path.Combine(parent, "child"));
        check("ShelfCopy: a folder cannot be copied into itself", ShelfCopy.Plan([parent], Path.Combine(parent, "child")).Count == 0);
    }

    private static void Copying(Action<string, bool> check)
    {
        var from = Folder("copy-from");
        var to = Folder("copy-to");
        var file = Write(from, "notes.txt", "original");
        var result = ShelfCopy.Copy([file], to);
        check("ShelfCopy: a file is copied into the folder", result.Copied.Count == 1 && result.Failed.Count == 0
            && File.ReadAllText(Path.Combine(to, "notes.txt")) == "original" && File.Exists(file));
        // The original is still there, which is what makes this a copy and not a move.
        check("ShelfCopy: the original stays where it was", File.ReadAllText(file) == "original");
        // The file already in the folder keeps its content; the copy goes beside it under the next name.
        File.WriteAllText(Path.Combine(to, "notes.txt"), "already here");
        var again = ShelfCopy.Copy([file], to);
        check("ShelfCopy: a file of that name is kept, and the copy renamed",
            again.Copied.Count == 1 && Path.GetFileName(again.Copied[0]) == "notes (2).txt" && File.ReadAllText(Path.Combine(to, "notes.txt")) == "already here");
        // A folder is copied with what is in it, one level down and further.
        var tree = Folder("tree");
        Write(tree, "top.txt");
        Directory.CreateDirectory(Path.Combine(tree, "inner"));
        Write(Path.Combine(tree, "inner"), "deep.txt");
        var folderCopy = ShelfCopy.Copy([tree], Folder("tree-to"));
        check("ShelfCopy: a folder is copied with everything in it", folderCopy.Copied.Count == 1
            && File.Exists(Path.Combine(folderCopy.Copied[0], "top.txt")) && File.Exists(Path.Combine(folderCopy.Copied[0], "inner", "deep.txt")));
        // A batch with a file that is gone still copies the rest, and names the one it could not.
        var good = Write(from, "good.txt");
        var mixed = ShelfCopy.Copy([Path.Combine(from, "absent.txt"), good], Folder("mixed-to"));
        check("ShelfCopy: one missing file does not stop the rest", mixed.Copied.Count == 1 && mixed.Failed.Count == 1);
    }
}