namespace AxDeck.Core;

// The rules for the names of the real files behind the boxes: which names Windows will not take, how a name that already exists
// is made unique, and how two paths are compared. Ported from Dasktop (https://github.com/AugSakura/Dasktop, MIT), where the
// same rules guard the real file operations from an unchecked path fragment.

// The names and the paths of the files behind the boxes.
public static class BoxNaming
{
    // The prefix of the real folder of a box; the folder is named after the box's id, never after its title.
    public const string BoxFolderPrefix = "box_";
    // The extension of a Windows shortcut.
    public const string ShortcutExtension = ".lnk";
    // The characters Windows does not allow in a file name.
    private static readonly char[] ForbiddenNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    // The device names Windows keeps for itself, even with an extension added.
    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];
    // The name a mapped shortcut takes when its target has none of its own.
    private const string ShortcutFallbackStem = "mapped";
    // The name a copied file takes when it has none of its own.
    private const string DuplicateFallbackStem = "file";
    // How many copies of a name are tried before giving up, so an odd folder cannot make an endless loop.
    private const int DuplicateNameLimit = 10_000;

    // The real folder name of a box, checked against the rule that it comes from the organizer itself and not from a title the
    // user typed: a folder name takes part in a real path, so it is not taken on trust.
    public static bool IsValidBoxFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var trimmed = name.Trim();
        if (!trimmed.StartsWith(BoxFolderPrefix, StringComparison.Ordinal)) return false;
        if (trimmed.Length == BoxFolderPrefix.Length) return false;
        return trimmed.All(value => char.IsAsciiLetterOrDigit(value) || value == '_' || value == '-');
    }

    // The name of the real folder of a box: the prefix and the box's id, which is a number and a letter and stays the same.
    public static string BoxFolderName(string id) => BoxFolderPrefix + id;

    // A name the user typed, checked against what Windows accepts. An empty result means the name cannot be used.
    public static string? SanitizeUserFileName(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed is "." or "..") return null;
        if (trimmed.Any(char.IsControl)) return null;
        if (trimmed.Any(value => ForbiddenNameChars.Contains(value))) return null;
        var normalized = trimmed.TrimEnd('.', ' ');
        if (normalized.Length == 0) return null;
        if (IsReservedDeviceName(normalized)) return null;
        return normalized;
    }

    // The path a mapped shortcut takes: the name of its target with the shortcut extension added.
    public static string DesiredShortcutPath(string source, string destinationFolder)
    {
        var stem = Path.GetFileNameWithoutExtension(source);
        if (string.IsNullOrWhiteSpace(stem)) stem = Path.GetFileName(source);
        if (string.IsNullOrWhiteSpace(stem)) stem = ShortcutFallbackStem;
        return Path.Combine(destinationFolder, stem + ShortcutExtension);
    }

    // The path a file takes when the one it wants is taken already: the name Explorer uses, "name (2).ext". The names already
    // handed out in this batch are kept, so two files of one name do not land on the same one.
    public static string? ResolveRenamedDestination(string desiredDestination, HashSet<string> reserved)
    {
        if (!File.Exists(desiredDestination) && !Directory.Exists(desiredDestination) && reserved.Add(PathKey(desiredDestination))) return desiredDestination;
        var parent = Path.GetDirectoryName(desiredDestination);
        if (string.IsNullOrEmpty(parent)) return null;
        var (stem, extension) = SplitDuplicateName(desiredDestination);
        for (int index = 2; index < DuplicateNameLimit; index++)
        {
            var candidate = Path.Combine(parent, $"{stem} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate) && reserved.Add(PathKey(candidate))) return candidate;
        }
        return null;
    }

    // Whether the two paths are the same file, both of which must exist.
    public static bool ReferToSameEntry(string left, string right)
    {
        try { return File.Exists(left) && File.Exists(right) && PathKey(left) == PathKey(right); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    // Whether the source is already a direct child of the destination, so moving it there changes nothing.
    public static bool IsDirectChildOf(string source, string destination)
    {
        var parent = Path.GetDirectoryName(source);
        return parent is not null && string.Equals(PathKey(parent), PathKey(destination), StringComparison.Ordinal);
    }

    // Whether the destination lies inside the source, which Windows does not allow for a folder.
    public static bool IsInside(string source, string destination)
    {
        if (!Directory.Exists(source)) return false;
        var prefix = PathKey(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return PathKey(destination).StartsWith(prefix, StringComparison.Ordinal);
    }

    // Whether the path is a Windows shortcut.
    public static bool IsShortcut(string path)
        => string.Equals(Path.GetExtension(path), ShortcutExtension, StringComparison.OrdinalIgnoreCase);

    // The name and the extension of a path, split the way a duplicate name is made.
    public static (string Stem, string Extension) SplitDuplicateName(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(stem)) stem = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(stem)) stem = DuplicateFallbackStem;
        var extension = Path.GetExtension(path);
        return (stem, string.IsNullOrEmpty(extension) ? "" : extension);
    }

    // The path as it is compared: absolute, and with its case folded, as Windows treats it.
    public static string PathKey(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return full.ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return path.ToLowerInvariant();
        }
    }

    // Whether the name is one of the device names Windows keeps, even with an extension added.
    private static bool IsReservedDeviceName(string name)
    {
        var stem = name.Split('.')[0];
        return ReservedDeviceNames.Any(reserved => string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase));
    }
}
