using AxDeck.Core;

internal static class BoxTests
{
    public static void Run(string root, Action<string, bool> check)
    {
        // The keys of the items a box holds: a real file is its path, a system entry is "shell::" and its id.
        check("a system entry is told apart from a real file",
            BoxItemKey.IsShell(BoxItemKey.ForShell("recycle-bin")) && !BoxItemKey.IsShell(@"C:\a.txt")
            && BoxItemKey.ShellId(BoxItemKey.ForShell("recycle-bin")) == "recycle-bin" && BoxItemKey.ShellId(@"C:\a.txt") == "");

        // The place a dropped item lands: the row the pointer is in, then the item before or after it.
        var row = new List<BoxItemRect>
        {
            new("a", 0, 0, 80, 80),
            new("b", 80, 0, 80, 80),
            new("c", 160, 0, 80, 80)
        };
        check("an item lands before the one whose middle it is left of",
            BoxInsertionResolver.Resolve(row, 10, 10, new HashSet<string>(), 4) == new BoxInsertion("a", false));
        check("an item lands after the one whose middle it is right of",
            BoxInsertionResolver.Resolve(row, 90, 10, new HashSet<string>(), 4) == new BoxInsertion("b", false));
        check("an item past the last one of a row lands after it",
            BoxInsertionResolver.Resolve(row, 900, 10, new HashSet<string>(), 4) == new BoxInsertion("c", true));
        check("an item being dragged does not take part in the calculation",
            BoxInsertionResolver.Resolve(row, 10, 10, new HashSet<string> { "a" }, 4) == new BoxInsertion("b", false));

        // A second row: the pointer is below the first one, so the second row is the one that counts.
        var twoRows = new List<BoxItemRect>
        {
            new("a", 0, 0, 80, 80),
            new("b", 80, 0, 80, 80),
            new("c", 0, 84, 80, 80),
            new("d", 80, 84, 80, 80)
        };
        check("a pointer in the second row lands among the second row",
            BoxInsertionResolver.Resolve(twoRows, 10, 120, new HashSet<string>(), 4) == new BoxInsertion("c", false));
        check("a pointer just under a row still counts as that row",
            BoxInsertionResolver.Resolve(twoRows, 10, 82, new HashSet<string>(), 4) == new BoxInsertion("a", false));
        check("a pointer past the gap of the rows lands in the next one",
            BoxInsertionResolver.Resolve(twoRows, 10, 90, new HashSet<string>(), 4) == new BoxInsertion("c", false));

        // A box with nothing but the item being dragged has no place to fall.
        check("a box with only the dragged item has no place to fall",
            !BoxInsertionResolver.Resolve(row, 10, 10, new HashSet<string> { "a", "b", "c" }, 4).IsValid);

        // The names Windows will not take.
        check("Windows keeps its own names for itself",
            BoxNaming.SanitizeUserFileName("CON") is null && BoxNaming.SanitizeUserFileName("con.txt") is null
            && BoxNaming.SanitizeUserFileName("LPT1") is null && BoxNaming.SanitizeUserFileName("..") is null
            && BoxNaming.SanitizeUserFileName("") is null);
        check("a name with a character Windows does not take is refused",
            BoxNaming.SanitizeUserFileName("a:b") is null && BoxNaming.SanitizeUserFileName("a/b") is null
            && BoxNaming.SanitizeUserFileName("a*b") is null && BoxNaming.SanitizeUserFileName("a\"b") is null);
        check("a name Windows does take is kept, without its trailing dots and spaces",
            BoxNaming.SanitizeUserFileName("  a.txt  ") == "a.txt" && BoxNaming.SanitizeUserFileName("a.txt...") == "a.txt");

        // The folder of a box is named after its id, never after a title the user typed.
        check("the real folder of a box is named after its id",
            BoxNaming.IsValidBoxFolderName(BoxNaming.BoxFolderName("abc123")) && !BoxNaming.IsValidBoxFolderName("..\\evil")
            && !BoxNaming.IsValidBoxFolderName("box_") && !BoxNaming.IsValidBoxFolderName("other_name"));

        // The plan for a file dragged into a folder.
        var folder = Path.Combine(root, "box-target");
        Directory.CreateDirectory(folder);
        var source = Path.Combine(root, "note.txt");
        File.WriteAllText(source, "hello");
        var copy = BoxTransferPlanner.Plan([source], folder, BoxDropAction.Copy, BoxConflictPolicy.Rename);
        check("a file dragged in is copied to the folder under its own name",
            copy.Plans.Count == 1 && copy.Plans[0].Kind == BoxTransferKind.Copy
            && Path.GetFileName(copy.Plans[0].Destination) == "note.txt");
        var mapped = BoxTransferPlanner.Plan([source], folder, BoxDropAction.Map, BoxConflictPolicy.Rename);
        check("a mapped file becomes a shortcut to where it is",
            mapped.Plans.Count == 1 && mapped.Plans[0].Kind == BoxTransferKind.Shortcut
            && Path.GetFileName(mapped.Plans[0].Destination) == "note.lnk");
        var moved = BoxTransferPlanner.Plan([source], folder, BoxDropAction.Move, BoxConflictPolicy.Rename);
        check("a file dragged in is moved to the folder",
            moved.Plans.Count == 1 && moved.Plans[0].Kind == BoxTransferKind.Move);

        // The same name twice: the rule decides what happens.
        File.WriteAllText(Path.Combine(folder, "note.txt"), "old");
        var skip = BoxTransferPlanner.Plan([source], folder, BoxDropAction.Copy, BoxConflictPolicy.Skip);
        check("a file of the same name is skipped when the rule says so", skip.Plans.Count == 0);
        var replace = BoxTransferPlanner.Plan([source], folder, BoxDropAction.Copy, BoxConflictPolicy.Replace);
        check("a file of the same name replaces the old one when the rule says so",
            replace.Plans.Count == 1 && replace.Plans[0].ReplacesExisting);
        var renamed = BoxTransferPlanner.Plan([source], folder, BoxDropAction.Copy, BoxConflictPolicy.Rename);
        check("a file of the same name is renamed when the rule says so",
            renamed.Plans.Count == 1 && Path.GetFileName(renamed.Plans[0].Destination) == "note (2).txt");

        // The place and the size of a box.
        var settings = new DasktopSettings();
        check("a box that is not moved stays where it is",
            BoxLayout.SnapPosition(300, 300, 320, 320, [], null, new BoxWorkArea(0, 0, 1920, 1040), settings) == (300, 300));
        check("a box near the edge of the screen snaps to it",
            BoxLayout.SnapPosition(15, 15, 320, 320, [], null, new BoxWorkArea(0, 0, 1920, 1040), settings) == (0, 0));
        check("a box that is told not to snap stays where it is",
            BoxLayout.SnapPosition(15, 15, 320, 320, [], null, new BoxWorkArea(0, 0, 1920, 1040),
                new DasktopSettings { SnapToEdges = false }) == (15, 15));
        check("a box that is dragged past the edge of the screen stops at its edge",
            BoxLayout.KeepInside(new BoxBounds(-200, -200, 320, 320), new BoxWorkArea(0, 0, 1920, 1040))
                == new BoxBounds(0, 0, 320, 320));

        // The size of a box snaps to a whole number of icon cells.
        var snapped = BoxLayout.SnapToGrid(new BoxBounds(100, 100, 350, 350), settings, null);
        var column = BoxLayout.ColumnWidth(settings);
        var rowHeight = BoxLayout.RowHeight(settings);
        check("the size of a box snaps to the grid of its icons",
            snapped.Width >= BoxWindowSize.MinWidth && snapped.Height >= BoxWindowSize.MinHeight
            // 去掉两边的留白后，剩下的正好是「格数 x 格宽 + 格数之间的间距」。
            && (snapped.Width - BoxWindowSize.GridPadding * 2 + settings.IconGapX) % (column + settings.IconGapX) < 0.01
            && (snapped.Height - BoxWindowSize.TitleHeight - BoxWindowSize.GridPadding * 2 + settings.IconGapY) % (rowHeight + settings.IconGapY) < 0.01);

        // The system entries a box can hold.
        check("the system entries a box can hold are the five Windows shows",
            BoxShellItems.All.Count == 5 && BoxShellItems.Find("this-pc") is not null
            && BoxShellItems.FindDesktopIcon("this-pc")?.DesktopIconClsid == "{20D04FE0-3AEA-1069-A2D8-08002B30309D}");
        check("a parsing name is told apart from the entry it stands for",
            BoxShellItems.FromParsingName("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}") == "this-pc"
            && BoxShellItems.FromParsingName(@"C:\Windows\System32") is null);

        // The selection in the file area: the way Explorer behaves.
        var point = new BoxSelection.Point(10, 20);
        var other = new BoxSelection.Point(110, 60);
        check("a rectangle drawn in either direction is the same rectangle",
            BoxSelection.Normalize(point, other) == BoxSelection.Normalize(other, point)
            && BoxSelection.Normalize(point, other) == new BoxSelection.Rect(10, 20, 100, 40));
        check("a rectangle that only touches another one does not count as crossing it",
            !BoxSelection.Intersects(new BoxSelection.Rect(0, 0, 10, 10), new BoxSelection.Rect(10, 0, 10, 10))
            && BoxSelection.Intersects(new BoxSelection.Rect(0, 0, 10, 10), new BoxSelection.Rect(9, 9, 10, 10)));
        var items = new List<(string Key, BoxSelection.Rect Rect)>
        {
            ("a", new BoxSelection.Rect(0, 0, 80, 80)),
            ("b", new BoxSelection.Rect(80, 0, 80, 80)),
            ("c", new BoxSelection.Rect(0, 80, 80, 80))
        };
        check("a dragged-out rectangle picks the items it crosses",
            BoxSelection.ItemsIn(new BoxSelection.Rect(0, 0, 100, 100), items).SequenceEqual(["a", "b", "c"]));
        check("a dragged-out rectangle picks only what it really touches",
            BoxSelection.ItemsIn(new BoxSelection.Rect(85, 5, 10, 10), items).SequenceEqual(["b"]));
        check("a plain click leaves only the item that was clicked",
            BoxSelection.AfterClick(["a", "b"], "c", false).SequenceEqual(["c"]));
        check("a click with Ctrl takes the item out of the selection",
            BoxSelection.AfterClick(["a", "b"], "a", true).SequenceEqual(["b"]));
        check("a click with Ctrl puts an item that was not chosen into the selection",
            BoxSelection.AfterClick(["a"], "b", true).SequenceEqual(["a", "b"]));
        check("clicking an item that is already the only one chosen starts the renaming",
            BoxSelection.ShouldRenameOnClick(["a"], "a", true));
        check("clicking one of several items does not start the renaming",
            !BoxSelection.ShouldRenameOnClick(["a", "b"], "a", true));
        check("clicking an item does not start the renaming when a single click opens it",
            !BoxSelection.ShouldRenameOnClick(["a"], "a", false));

        // The keys of the file area: the same ones Explorer answers to, and the same ones the original answers to
        // (Dasktop's useBoxFileSelection.ts, handleFileViewKeydown).
        check("Ctrl+A selects everything in the box",
            BoxKeyboard.For("A", true) == BoxKeyboard.Command.SelectAll);
        check("Ctrl+C copies, Ctrl+X cuts and Ctrl+V pastes",
            BoxKeyboard.For("C", true) == BoxKeyboard.Command.Copy
            && BoxKeyboard.For("X", true) == BoxKeyboard.Command.Cut
            && BoxKeyboard.For("V", true) == BoxKeyboard.Command.Paste);
        check("F2 renames, Delete removes, Enter opens and Escape clears",
            BoxKeyboard.For("F2", false) == BoxKeyboard.Command.Rename
            && BoxKeyboard.For("Delete", false) == BoxKeyboard.Command.Delete
            && BoxKeyboard.For("Enter", false) == BoxKeyboard.Command.Open
            && BoxKeyboard.For("Escape", false) == BoxKeyboard.Command.Clear);
        check("the letters without Ctrl do nothing",
            BoxKeyboard.For("A", false) == BoxKeyboard.Command.None
            && BoxKeyboard.For("C", false) == BoxKeyboard.Command.None);
        check("a key the box does not use does nothing",
            BoxKeyboard.For("Q", true) == BoxKeyboard.Command.None
            && BoxKeyboard.For("F5", false) == BoxKeyboard.Command.None);

        // The folding of a box: a folded box keeps only its title, and unfolds while the mouse is on it.
        // The original's list of what holds a box open, one for one (useBoxCollapsePreview.ts,
        // shouldKeepCollapsedPreviewOpen).
        check("a folded box is only as tall as its title",
            BoxCollapse.FoldedHeight == BoxWindowSize.TitleHeight);
        check("a box that is not folded keeps the height the user gave it",
            BoxCollapse.HeightFor(false, 320) == 320);
        check("a folded box is only as tall as its title",
            BoxCollapse.HeightFor(true, 320) == BoxWindowSize.TitleHeight);
        check("a folded box shows no contents",
            !BoxCollapse.ContentsVisible(true) && BoxCollapse.ContentsVisible(false));

        var nothing = new BoxCollapse.State();
        check("a folded box folds again when nothing is happening to it",
            BoxCollapse.ShouldFold(true, false, nothing));
        check("a box that is not set to fold never folds",
            !BoxCollapse.ShouldFold(false, false, nothing));
        check("a box that is momentarily unfolded stays unfolded",
            !BoxCollapse.ShouldFold(true, true, nothing));

        // Everything that happens to a box holds it open.
        foreach (var (name, state) in new (string, BoxCollapse.State)[]
        {
            ("the mouse is over it", nothing with { BoxHovered = true }),
            ("the mouse is over its title", nothing with { TitleHovered = true }),
            ("a file is dragged over it", nothing with { DragHovering = true }),
            ("the system's menu is open", nothing with { NativeMenuOpen = true }),
            ("its own menu is open", nothing with { BoxMenuOpen = true }),
            ("its title is being edited", nothing with { EditingTitle = true }),
            ("it is being dragged", nothing with { Dragging = true }),
            ("the mouse is on a resize handle", nothing with { ResizeHandleHovered = true }),
            ("it is being resized", nothing with { Resizing = true })
        })
        {
            check($"a box that is {name} stays open", BoxCollapse.KeepsOpen(state));
            check($"a box that is {name} is not folded away", !BoxCollapse.ShouldFold(true, false, state));
        }

        // The title of a box, while it is being written.
        check("a new title is saved",
            BoxTitleEditing.Commit("  新名字  ", "旧名字") == "新名字");
        check("a title that was not changed is not saved again",
            BoxTitleEditing.Commit("  一样的  ", "一样的") is null);
        check("a title may be emptied",
            BoxTitleEditing.Commit("   ", "旧名字") == "");
        check("cancelling the title keeps the old one",
            BoxTitleEditing.Cancel("旧名字") == "旧名字");
        check("the second click of a double click starts the editing",
            BoxTitleEditing.IsDoubleClick(2) && !BoxTitleEditing.IsDoubleClick(1));

        // The space between two icons: the two sliders set it, and it is room between the places, not part of them.
        check("the gap between two icons of a row is the one the user set",
            BoxLayout.ColumnGap(new DasktopSettings { IconGapX = 12 }) == 12
            && BoxLayout.ColumnGap(new DasktopSettings { IconGapX = 6 }) == 6);
        check("the gap between two rows is the one the user set",
            BoxLayout.RowGap(new DasktopSettings { IconGapY = 10 }) == 10);
        check("a place an icon takes does not grow with the gap",
            BoxLayout.ColumnWidth(new DasktopSettings { IconGapX = 30, IconSize = 48, FilenameWidth = 82 })
            == BoxLayout.ColumnWidth(new DasktopSettings { IconGapX = 0, IconSize = 48, FilenameWidth = 82 }));
        check("the step of one column is the place plus the gap",
            BoxLayout.ColumnStride(new DasktopSettings { IconGapX = 6 }) == BoxLayout.ColumnWidth(new DasktopSettings { IconGapX = 6 }) + 6);
        check("the step of one row is the height of a row plus the gap",
            BoxLayout.RowStride(new DasktopSettings { IconGapY = 4 }) == BoxLayout.RowHeight(new DasktopSettings { IconGapY = 4 }) + 4);
    }
}
