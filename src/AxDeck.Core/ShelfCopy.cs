namespace AxDeck.Core;

// Copying the files of the transfer page somewhere on this PC: the desktop, or a folder the user picks. The originals stay where
// they are. A file of the same name already in the folder is kept, and the copy takes the next name Explorer would give it
// ("name (2).ext"), so copying to a place that already holds one of them never overwrites anything.
public static class ShelfCopy
{
    // What the batch did: where the copies landed, and the sources that could not be copied — a file that has gone, a folder
    // that was aimed at itself, a disk that is full.
    public sealed record Result(IReadOnlyList<string> Copied, IReadOnlyList<string> Failed);

    // The names the copies would take in the folder, without writing anything: one per source still there, in the order given.
    // A source that is gone is left out, and so is one that cannot be copied into that folder at all.
    public static IReadOnlyList<string> Plan(IEnumerable<string> paths, string folder)
    {
        var reserved = new HashSet<string>(StringComparer.Ordinal);
        var planned = new List<string>();
        foreach (var source in paths)
            if (Destination(source, folder, reserved) is { } destination) planned.Add(destination);
        return planned;
    }

    // Copies the sources into the folder, one at a time, so one that fails does not stop the rest.
    public static Result Copy(IEnumerable<string> paths, string folder)
    {
        var reserved = new HashSet<string>(StringComparer.Ordinal);
        var copied = new List<string>();
        var failed = new List<string>();
        foreach (var source in paths)
        {
            var destination = Destination(source, folder, reserved);
            if (destination is null) { failed.Add(source); continue; }
            try
            {
                CopyOne(source, destination);
                copied.Add(destination);
            }
            // A file in use, a file without permission to be read: the copy is not made and the rest still are.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add(source); }
        }
        return new Result(copied, failed);
    }

    // Where the copy of one source goes, or null when it cannot be copied there: the source is gone, the folder is not there,
    // or the folder lies inside the source, which Windows does not allow.
    private static string? Destination(string source, string folder, HashSet<string> reserved)
    {
        if (folder.Length == 0 || source.Length == 0) return null;
        try
        {
            bool isFile = File.Exists(source), isFolder = Directory.Exists(source);
            if (!isFile && !isFolder) return null;
            if (!Directory.Exists(folder)) return null;
            if (isFolder && BoxNaming.IsInside(source, folder)) return null;
            // A file of the same name is kept, so the copy is always the one renamed.
            var desired = Path.Combine(folder, Path.GetFileName(source.TrimEnd('\\', '/')));
            return BoxNaming.ResolveRenamedDestination(desired, reserved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    // A file, or a folder with everything in it. The destination is one the plan found free, so nothing is replaced.
    private static void CopyOne(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
            return;
        }
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var folder in Directory.EnumerateDirectories(source)) CopyOne(folder, Path.Combine(destination, Path.GetFileName(folder)));
    }
}