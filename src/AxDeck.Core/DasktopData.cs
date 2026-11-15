using System.Text.Json.Serialization;

namespace AxDeck.Core;

// The desktop organizer: the boxes that group what lies on the desktop, and the rules that decide what happens to the real files
// behind them. Ported from Dasktop (https://github.com/AugSakura/Dasktop, MIT): its boxes, its file policies and its handling of
// the Windows system icons, rewritten for this project.

// What a box's item is: a file, a folder, a shortcut, or one of the Windows system entries that have no path of their own.
public enum BoxItemKind { File, Folder, Shortcut, Shell, Unknown }
// Where an item comes from: the file system, or the Windows shell (a system entry).
public enum BoxItemSource { FileSystem, Shell }
// How a box's title is placed in its window.
public enum BoxTitlePosition { Top, Bottom }

// What happens to a box's real folder when the box is deleted.
public enum BoxDeletePolicy { MoveContentsToDesktop, KeepFolder, RecycleFolder }
// What happens to a file when it is dragged into a box, or out of one.
public enum BoxDropAction { Copy, Move, Map }
// What happens when the target already holds a file of the same name.
public enum BoxConflictPolicy { Rename, Skip, Replace }
// How a file's name is shown under its icon.
public enum BoxNameDisplay { Full, HideShortcutExtension, HideAllExtensions }

// The policies as they are written in the settings file, so a saved value stays readable across versions.
public static class BoxPolicyCodes
{
    // The delete policy codes.
    public const string MoveContentsToDesktop = "moveContentsToDesktop", KeepFolder = "keepFolder", RecycleFolder = "recycleFolder";
    // The drop action codes.
    public const string Copy = "copy", Move = "move", Map = "map";
    // The conflict policy codes.
    public const string Rename = "rename", Skip = "skip", Replace = "replace";
    // The name display codes.
    public const string Full = "full", HideShortcutExtension = "hideShortcutExtension", HideAllExtensions = "hideAllExtensions";
}

// The codes of the policies, as they are written to the settings file.
public static class BoxPolicy
{
    public static string Code(BoxDeletePolicy policy) => policy switch
    {
        BoxDeletePolicy.MoveContentsToDesktop => BoxPolicyCodes.MoveContentsToDesktop,
        BoxDeletePolicy.KeepFolder => BoxPolicyCodes.KeepFolder,
        _ => BoxPolicyCodes.RecycleFolder
    };
    public static string Code(BoxDropAction action) => action switch
    {
        BoxDropAction.Copy => BoxPolicyCodes.Copy, BoxDropAction.Move => BoxPolicyCodes.Move, _ => BoxPolicyCodes.Map
    };
    public static string Code(BoxConflictPolicy policy) => policy switch
    {
        BoxConflictPolicy.Rename => BoxPolicyCodes.Rename, BoxConflictPolicy.Skip => BoxPolicyCodes.Skip, _ => BoxPolicyCodes.Replace
    };
    public static string Code(BoxNameDisplay display) => display switch
    {
        BoxNameDisplay.Full => BoxPolicyCodes.Full, BoxNameDisplay.HideAllExtensions => BoxPolicyCodes.HideAllExtensions, _ => BoxPolicyCodes.HideShortcutExtension
    };

    // The policies as they are read back. An unknown or missing value takes the default, so a file written by another version
    // cannot make the file operations act on an unknown rule.
    public static BoxDeletePolicy Delete(string? code) => code switch
    {
        BoxPolicyCodes.MoveContentsToDesktop => BoxDeletePolicy.MoveContentsToDesktop,
        BoxPolicyCodes.KeepFolder => BoxDeletePolicy.KeepFolder,
        BoxPolicyCodes.RecycleFolder => BoxDeletePolicy.RecycleFolder,
        _ => BoxDeletePolicy.MoveContentsToDesktop
    };
    public static BoxDropAction Drop(string? code) => code switch
    {
        BoxPolicyCodes.Copy => BoxDropAction.Copy, BoxPolicyCodes.Move => BoxDropAction.Move, BoxPolicyCodes.Map => BoxDropAction.Map,
        _ => BoxDropAction.Move
    };
    public static BoxDropAction DragOut(string? code) => Drop(code);
    public static BoxConflictPolicy Conflict(string? code) => code switch
    {
        BoxPolicyCodes.Rename => BoxConflictPolicy.Rename, BoxPolicyCodes.Skip => BoxConflictPolicy.Skip, BoxPolicyCodes.Replace => BoxConflictPolicy.Replace,
        _ => BoxConflictPolicy.Rename
    };
    public static BoxNameDisplay Display(string? code) => code switch
    {
        BoxPolicyCodes.Full => BoxNameDisplay.Full, BoxPolicyCodes.HideAllExtensions => BoxNameDisplay.HideAllExtensions, _ => BoxNameDisplay.HideShortcutExtension
    };
}

// A box: a window on the desktop that groups what lies on it. Its title is only shown; the real folder it keeps its files in is
// named after its id, so renaming a box never moves anything.
public sealed class DesktopBox
{
    // The business id, also the stable part of the real folder's name.
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    // The real folder the box keeps its files in; created with the box and kept until the box is deleted.
    public string FolderPath { get; set; } = "";
    // Whether the box folds down to its title when the mouse leaves it.
    public bool Collapsed { get; set; }
    // Whether the box is locked: it cannot be moved or resized, but its files can still be worked with.
    public bool Locked { get; set; }
    // How visible the box is when the mouse is elsewhere, in percent; 0 means it is entirely unseen.
    public int TitleOpacity { get; set; } = 100;
    public BoxTitlePosition TitlePosition { get; set; } = BoxTitlePosition.Top;
    // The box's place and size on the screen, in DIPs.
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

// The whole desktop organizer as it is saved: the boxes, what each of them holds, and the settings that shape them.
public sealed class DasktopData
{
    public List<DesktopBox> Boxes { get; set; } = [];
    // What each box holds, in the order it is shown: the real paths first, then the system entries.
    public Dictionary<string, List<string>> ItemOrders { get; set; } = [];
    // The system entries each box holds, by their id (BoxShellItems).
    public Dictionary<string, List<string>> VirtualItems { get; set; } = [];
    // What the organizer did to the native desktop icons, so it can put them back the way it found them.
    public Dictionary<string, ShellIconRecord> IconRecords { get; set; } = [];
    // The settings of the organizer.
    public DasktopSettings Settings { get; set; } = new();
}

// What the organizer did to one native desktop icon: whether it was shown before, and whether it is hidden now.
public sealed class ShellIconRecord
{
    public bool PreviousVisible { get; set; } = true;
    public bool ManagedHidden { get; set; }
}

// The settings of the desktop organizer. The codes of the policies are the strings written to the settings file.
public sealed class DasktopSettings
{
    // The theme of the box windows and of the settings page: system, light or dark.
    public string Theme { get; set; } = "system";
    // Where the real folders of the boxes are kept; empty means the default under the user's data.
    public string CollectionRoot { get; set; } = "";
    // The policies, as their codes.
    public string DeletePolicy { get; set; } = BoxPolicyCodes.MoveContentsToDesktop;
    public string DropAction { get; set; } = BoxPolicyCodes.Move;
    public string DragOutAction { get; set; } = BoxPolicyCodes.Move;
    public string ConflictPolicy { get; set; } = BoxPolicyCodes.Rename;
    public string NameDisplayMode { get; set; } = BoxPolicyCodes.HideShortcutExtension;
    // Whether the native desktop icons of the system entries are hidden once they are in a box.
    public bool AutoHideShellIcons { get; set; } = true;
    // Whether an item is opened with a double click; otherwise one click opens it.
    public bool DoubleClickOpen { get; set; } = true;
    // Whether the boxes are shown with their icons' names under them.
    public bool ShowItemLabels { get; set; } = true;
    // Whether a shortcut shows the small arrow that marks it as one.
    public bool ShowShortcutArrow { get; set; } = true;
    // Whether a box snaps to the edge of the screen or of another box.
    public bool SnapToEdges { get; set; } = true;
    // Whether a box's size snaps to the grid of its icons.
    public bool ResizeGridEnabled { get; set; } = true;
    // How close a box must come to an edge before it snaps to it, in DIPs.
    public double SnapThreshold { get; set; } = 20;
    // The look of the boxes.
    public int BackgroundOpacity { get; set; } = 70;
    public int CornerRadius { get; set; } = 8;
    public int IconSize { get; set; } = 48;
    public int LabelTextSize { get; set; } = 12;
    public int IconGapX { get; set; } = 6;
    public int IconGapY { get; set; } = 4;
    // How wide the name under an icon may grow before it is cut.
    public int FilenameWidth { get; set; } = 82;
    // The animations of a box, in milliseconds.
    public int CollapseAnimationMs { get; set; } = 420;
    public int CollapseDelayMs { get; set; } = 420;
    public int IdleOpacityHideMs { get; set; } = 420;
    public int IdleOpacityShowMs { get; set; } = 420;

    // The policies as they are read.
    [JsonIgnore] public BoxDeletePolicy Delete => BoxPolicy.Delete(DeletePolicy);
    [JsonIgnore] public BoxDropAction Drop => BoxPolicy.Drop(DropAction);
    [JsonIgnore] public BoxDropAction DragOut => BoxPolicy.DragOut(DragOutAction);
    [JsonIgnore] public BoxConflictPolicy Conflict => BoxPolicy.Conflict(ConflictPolicy);
    [JsonIgnore] public BoxNameDisplay NameDisplay => BoxPolicy.Display(NameDisplayMode);
}
