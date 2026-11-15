using System.IO;
using AxDeck.Core;

namespace AxDeck;

// The desktop organizer: the boxes, what they hold, and the rules that decide what happens to the real files behind them.
// Ported from Dasktop (https://github.com/AugSakura/Dasktop, MIT): its boxes, its file policies, its rollback on a failed
// transfer and its handling of the native desktop icons.
internal sealed class BoxService
{
    // The data as it is saved.
    public DasktopData Data { get; private set; } = new();
    // The boxes as they are now.
    public IReadOnlyList<DesktopBox> Boxes => Data.Boxes;
    // The settings of the organizer.
    public DasktopSettings Settings => Data.Settings;
    // Where the real folders of the boxes are kept.
    public string CollectionRoot => string.IsNullOrWhiteSpace(Data.Settings.CollectionRoot)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AxDeck", "Boxes")
        : Data.Settings.CollectionRoot;

    // Raised when the boxes or their contents changed, so the windows and the page show it again.
    public event Action? Changed;

    // Reads the saved data and puts the native desktop icons in the state the boxes ask for: an icon a box holds stays hidden,
    // an icon no box holds goes back the way the organizer found it.
    public void Load(DasktopData data)
    {
        Data = data;
        SyncShellIcons();
        Changed?.Invoke();
    }

    // The data as it is saved.
    public DasktopData Snapshot() => Data;

    // ---- The boxes ----

    // The box with this id, or null.
    public DesktopBox? Find(string id) => Data.Boxes.FirstOrDefault(box => string.Equals(box.Id, id, StringComparison.Ordinal));

    // Makes a new box, with its real folder, at a place where it does not lie on the one before it.
    public DesktopBox Create(string title, double? x = null, double? y = null)
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        var folder = Path.Combine(CollectionRoot, BoxNaming.BoxFolderName(id));
        Directory.CreateDirectory(folder);
        var count = Data.Boxes.Count;
        var box = new DesktopBox
        {
            Id = id,
            Title = title.Length > 0 ? title : Loc.T("新建分组", "New box"),
            FolderPath = folder,
            X = x ?? (BoxWindowSize.InitialX + count * BoxWindowSize.CascadeStep),
            Y = y ?? (BoxWindowSize.InitialY + count * BoxWindowSize.CascadeStep),
            Width = BoxWindowSize.DefaultWidth,
            Height = BoxWindowSize.DefaultHeight
        };
        Data.Boxes.Add(box);
        Changed?.Invoke();
        return box;
    }

    // Saves what changed about a box.
    public void Save(DesktopBox box)
    {
        var index = Data.Boxes.FindIndex(item => string.Equals(item.Id, box.Id, StringComparison.Ordinal));
        if (index >= 0) Data.Boxes[index] = box;
        Changed?.Invoke();
    }

    // Deletes a box. Its real folder is handled by the rule the user chose, and the native icons it held are put back.
    public void Delete(DesktopBox box, string desktopPath)
    {
        // The icons the box held go back to the desktop first: the rule below may need the desktop. An icon another box still
        // holds stays hidden.
        var shellIds = VirtualItems(box.Id).ToList();
        foreach (var shellId in shellIds)
        {
            var held = Data.VirtualItems.Where(pair => !string.Equals(pair.Key, box.Id, StringComparison.Ordinal))
                .Any(pair => pair.Value.Contains(shellId, StringComparer.Ordinal));
            if (!held) RestoreShellIcon(shellId);
        }

        switch (Settings.Delete)
        {
            case BoxDeletePolicy.KeepFolder:
                break;
            case BoxDeletePolicy.RecycleFolder:
                if (Directory.Exists(box.FolderPath)) Native.ShellFileOperations.Recycle([box.FolderPath]);
                break;
            default:
                MoveContentsToDesktop(box.FolderPath, desktopPath);
                break;
        }

        Data.Boxes.RemoveAll(item => string.Equals(item.Id, box.Id, StringComparison.Ordinal));
        Data.ItemOrders.Remove(box.Id);
        Data.VirtualItems.Remove(box.Id);
        Changed?.Invoke();
    }

    // Moves what is in the folder to the desktop, keeping the names it had.
    private void MoveContentsToDesktop(string folder, string desktopPath)
    {
        if (!Directory.Exists(folder)) return;
        Directory.CreateDirectory(desktopPath);
        var moved = new List<(string Source, string Target)>();
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
        {
            var name = Path.GetFileName(entry);
            var target = Path.Combine(desktopPath, name);
            // A file of the same name is already on the desktop: it is kept, and the one from the box is renamed.
            if (File.Exists(target) || Directory.Exists(target))
            {
                var renamed = BoxNaming.ResolveRenamedDestination(target, reserved);
                if (renamed is null) continue;
                target = renamed;
            }
            reserved.Add(BoxNaming.PathKey(target));
            moved.Add((entry, target));
        }
        if (moved.Count == 0) return;
        try { Native.ShellFileOperations.Move(moved); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    // ---- What a box holds ----

    // What a box holds, in the order it is shown: the real files and the system entries are in one list, so they can lie next to
    // each other. A system entry is written as "shell::" and its id.
    public IReadOnlyList<string> ItemKeys(string boxId)
    {
        var folder = Find(boxId)?.FolderPath ?? "";
        var saved = Data.ItemOrders.TryGetValue(boxId, out var order) ? order : [];
        var present = new List<string>();
        if (Directory.Exists(folder))
        {
            var onDisk = Directory.EnumerateFileSystemEntries(folder).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var key in saved)
            {
                // A system entry is a reference, not a file: it stays as long as the box holds it.
                if (BoxItemKey.IsShell(key)) { if (!present.Contains(key, StringComparer.Ordinal)) present.Add(key); continue; }
                if (onDisk.Contains(key)) present.Add(key);
            }
            // The files that are on the disk but not in the saved order are shown after them, so a file added elsewhere is not lost.
            foreach (var path in onDisk) if (!present.Contains(path, StringComparer.OrdinalIgnoreCase)) present.Add(path);
        }
        else
        {
            // Without a folder there are no real files, but the system entries the box holds are still there.
            foreach (var key in saved) if (BoxItemKey.IsShell(key) && !present.Contains(key, StringComparer.Ordinal)) present.Add(key);
        }
        return present;
    }

    // The real files in the box, in the order they are shown.
    public IReadOnlyList<string> ItemPaths(string boxId) => [.. ItemKeys(boxId).Where(key => !BoxItemKey.IsShell(key))];

    // The system entries in the box, in the order they are shown.
    public IReadOnlyList<string> VirtualItems(string boxId) => [.. ItemKeys(boxId).Where(BoxItemKey.IsShell).Select(BoxItemKey.ShellId)];

    // Saves the order of what a box holds.
    public void SaveItemOrder(string boxId, IReadOnlyList<string> keys)
    {
        Data.ItemOrders[boxId] = [.. keys];
        Changed?.Invoke();
    }

    // Puts a key at a place in the order of a box: the item that is there now moves out of the way.
    public void SaveItemOrderAt(string boxId, IReadOnlyList<string> keys, string targetKey, bool after)
    {
        var current = ItemKeys(boxId).ToList();
        var moving = current.Where(key => keys.Contains(key, StringComparer.Ordinal)).ToList();
        if (moving.Count == 0) return;
        var rest = current.Where(key => !moving.Contains(key, StringComparer.Ordinal)).ToList();
        var index = rest.FindIndex(key => string.Equals(key, targetKey, StringComparison.Ordinal));
        if (index < 0) index = rest.Count;
        else if (after) index++;
        rest.InsertRange(index, moving);
        SaveItemOrder(boxId, rest);
    }

    // Puts an item at the place it was dropped: the item that was there moves out of the way, and the real file does not move.
    public void MoveItemTo(string boxId, string key, int index)
    {
        var current = ItemKeys(boxId).ToList();
        var at = current.FindIndex(item => string.Equals(item, key, StringComparison.Ordinal));
        if (at < 0) return;
        current.RemoveAt(at);
        current.Insert(Math.Clamp(index, 0, current.Count), key);
        SaveItemOrder(boxId, current);
    }

    // Renames a file in a box and keeps the order in step with the new name. False when the name is one Windows will not take,
    // or when a file of that name is already there.
    public bool Rename(string boxId, string path, string newName)
    {
        var name = Core.BoxNaming.SanitizeUserFileName(newName);
        if (name is null) return false;
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent)) return false;
        var target = Path.Combine(parent, name);
        if (Core.BoxNaming.PathKey(target) == Core.BoxNaming.PathKey(path)) return true;
        if (File.Exists(target) || Directory.Exists(target)) return false;
        try
        {
            if (Directory.Exists(path)) Directory.Move(path, target);
            else if (File.Exists(path)) File.Move(path, target);
            else return false;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        // The order keeps the place of the file, but under its new name.
        if (Data.ItemOrders.TryGetValue(boxId, out var order))
        {
            var key = Core.BoxNaming.PathKey(path);
            Data.ItemOrders[boxId] = [.. order.Select(item => BoxItemKey.IsShell(item) ? item : Core.BoxNaming.PathKey(item) == key ? target : item)];
        }
        Changed?.Invoke();
        return true;
    }
    // Takes a file out of a box: it goes to the recycle bin, so it can be brought back, and the order forgets it.
    public void RemoveItem(string boxId, string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            try { Native.ShellFileOperations.Recycle([path]); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
        if (Data.ItemOrders.TryGetValue(boxId, out var order))
        {
            var key = Core.BoxNaming.PathKey(path);
            Data.ItemOrders[boxId] = [.. order.Where(item => !BoxItemKey.IsShell(item) && string.Equals(Core.BoxNaming.PathKey(item), key, StringComparison.Ordinal))];
        }
        Changed?.Invoke();
    }

    // Puts a file out of the box and onto the desktop, as the drag-out rule says: it is moved, copied, or left where it is with a
    // shortcut to it on the desktop. The paths that land on the desktop are returned.
    public IReadOnlyList<string> DragOut(string boxId, string path, string desktopPath)
    {
        Directory.CreateDirectory(desktopPath);
        var landed = BoxTransferExecutor.Execute([path], desktopPath, Settings.DragOut, Settings.Conflict);
        // A file that was moved away is no longer in the box; a copy and a shortcut leave it where it was.
        if (!File.Exists(path) && !Directory.Exists(path)) ForgetItem(boxId, path);
        return landed;
    }

    // Puts a whole set of items out of the box and onto the desktop, as the drag-out rule says. A system entry has no file of its
    // own: it only leaves the box, and its native icon goes back to the desktop.
    public IReadOnlyList<string> DragOut(string boxId, IReadOnlyList<string> keys, string desktopPath)
    {
        var shellIds = keys.Where(BoxItemKey.IsShell).Select(BoxItemKey.ShellId).ToList();
        var paths = keys.Where(key => !BoxItemKey.IsShell(key)).ToList();
        foreach (var shellId in shellIds) RemoveVirtualItem(boxId, shellId);
        if (paths.Count == 0) return [];
        Directory.CreateDirectory(desktopPath);
        var landed = BoxTransferExecutor.Execute(paths, desktopPath, Settings.DragOut, Settings.Conflict);
        foreach (var path in paths) if (!File.Exists(path) && !Directory.Exists(path)) ForgetItem(boxId, path);
        return landed;
    }

    // Moves items from one box into another: the real files are moved, copied or left as shortcuts as the drop rule says, and the
    // system entries are only referred to. The items that arrive land at the place they were dropped.
    public IReadOnlyList<string> MoveBetweenBoxes(string sourceBoxId, string targetBoxId, IReadOnlyList<string> keys, string? targetKey, bool after)
    {
        var shellIds = keys.Where(BoxItemKey.IsShell).Select(BoxItemKey.ShellId).ToList();
        var paths = keys.Where(key => !BoxItemKey.IsShell(key)).ToList();

        foreach (var shellId in shellIds)
        {
            RemoveVirtualItem(sourceBoxId, shellId);
            AddVirtualItem(targetBoxId, shellId);
        }

        if (paths.Count == 0) return [];

        var target = Find(targetBoxId);
        if (target is null) return [];
        var landed = BoxTransferExecutor.Execute(paths, target.FolderPath, Settings.Drop, Settings.Conflict);
        // A file that was moved away is no longer in the box it came from; a copy and a shortcut leave it where it was.
        foreach (var path in paths) if (!File.Exists(path) && !Directory.Exists(path)) ForgetItem(sourceBoxId, path);
        if (landed.Count > 0 && targetKey is not null) SaveItemOrderAt(targetBoxId, landed, targetKey, after);
        return landed;
    }

    // Forgets an item that is no longer in the box.
    private void ForgetItem(string boxId, string path)
    {
        if (!Data.ItemOrders.TryGetValue(boxId, out var order)) return;
        var key = Core.BoxNaming.PathKey(path);
        Data.ItemOrders[boxId] = [.. order.Where(item => !string.Equals(Core.BoxNaming.PathKey(item), key, StringComparison.Ordinal))];
        Changed?.Invoke();
    }

    // Puts a system entry in a box, and hides its native icon when the setting says so.
    public void AddVirtualItem(string boxId, string shellId)
    {
        // Which system entries the box holds: this is what tells the native icons to stay hidden.
        var items = Data.VirtualItems.TryGetValue(boxId, out var list) ? list : [];
        if (!items.Contains(shellId, StringComparer.Ordinal))
        {
            Data.VirtualItems[boxId] = [.. items, shellId];
            // The order table holds the system entry too, so it can lie next to the real files.
            var order = ItemKeys(boxId).ToList();
            if (!order.Contains(BoxItemKey.ForShell(shellId), StringComparer.Ordinal)) order.Add(BoxItemKey.ForShell(shellId));
            Data.ItemOrders[boxId] = order;
        }
        if (Settings.AutoHideShellIcons) HideShellIcon(shellId);
        Changed?.Invoke();
    }

    // Takes a system entry out of a box, and puts its native icon back when no other box holds it.
    public void RemoveVirtualItem(string boxId, string shellId)
    {
        var items = Data.VirtualItems.TryGetValue(boxId, out var list) ? list : [];
        Data.VirtualItems[boxId] = [.. items.Where(item => !string.Equals(item, shellId, StringComparison.Ordinal))];
        var key = BoxItemKey.ForShell(shellId);
        if (Data.ItemOrders.TryGetValue(boxId, out var order))
            Data.ItemOrders[boxId] = [.. order.Where(item => !string.Equals(item, key, StringComparison.Ordinal))];
        if (!Data.VirtualItems.Values.Any(list => list.Contains(shellId, StringComparer.Ordinal))) RestoreShellIcon(shellId);
        Changed?.Invoke();
    }

    // ---- The native desktop icons ----

    // Puts the native icons in the state the boxes ask for: an icon a box holds is hidden, an icon no box holds goes back the way
    // the organizer found it. This is what the program does when it starts, so a box that holds an icon keeps it hidden.
    public void SyncShellIcons()
    {
        // The system entries every box holds, without repeats.
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var list in Data.VirtualItems.Values) foreach (var shellId in list) referenced.Add(shellId);

        // The icons the organizer has touched, plus the ones a box holds now.
        var shellIds = Data.IconRecords.Keys.ToList();
        foreach (var shellId in referenced) if (!shellIds.Contains(shellId, StringComparer.Ordinal)) shellIds.Add(shellId);

        foreach (var shellId in shellIds)
        {
            var clsid = Native.ShellDesktopIcons.ClsidOf(shellId);
            // An entry without a class id of its own has no native icon to manage.
            if (clsid.Length == 0) { Data.IconRecords.Remove(shellId); continue; }
            if (!Settings.AutoHideShellIcons)
            {
                // The switch is off: every icon the organizer hid goes back the way it was.
                if (Data.IconRecords.TryGetValue(shellId, out var record))
                {
                    Native.ShellDesktopIcons.SetVisible(clsid, record.PreviousVisible);
                    Data.IconRecords.Remove(shellId);
                }
                continue;
            }
            if (referenced.Contains(shellId)) HideShellIcon(shellId);
            else if (Data.IconRecords.TryGetValue(shellId, out var record))
            {
                Native.ShellDesktopIcons.SetVisible(clsid, record.PreviousVisible);
                Data.IconRecords.Remove(shellId);
            }
        }
    }

    // Hides the native icon of a system entry and remembers how it was.
    private void HideShellIcon(string shellId)
    {
        var clsid = Native.ShellDesktopIcons.ClsidOf(shellId);
        if (clsid.Length == 0) return;
        var item = Core.BoxShellItems.FindDesktopIcon(shellId);
        var visible = Native.ShellDesktopIcons.IsVisible(clsid, item?.DefaultVisible ?? true);
        // The first time the organizer takes an icon, the state it found is remembered; later turns do not overwrite it, so the
        // icon goes back the way the user had it.
        if (!Data.IconRecords.ContainsKey(shellId)) Data.IconRecords[shellId] = new ShellIconRecord { PreviousVisible = visible, ManagedHidden = true };
        else Data.IconRecords[shellId].ManagedHidden = true;
        Native.ShellDesktopIcons.SetVisible(clsid, false);
    }

    // Puts the native icon of a system entry back the way it was.
    private void RestoreShellIcon(string shellId)
    {
        var clsid = Native.ShellDesktopIcons.ClsidOf(shellId);
        if (clsid.Length == 0) return;
        // Without a record there is nothing to put back: the organizer never touched this icon, so the registry is left as it
        // was. Writing the default here would put a value there that was not there before, and hide icons the user shows.
        if (!Data.IconRecords.TryGetValue(shellId, out var record)) return;
        Native.ShellDesktopIcons.SetVisible(clsid, record.PreviousVisible);
        Data.IconRecords.Remove(shellId);
    }

    // Puts every native icon the organizer touched back the way it was. This is what the program does when it goes: the icons
    // the boxes held come back, so Windows is not left without them.
    public void RestoreShellIcons()
    {
        foreach (var shellId in Data.IconRecords.Keys.ToList())
        {
            var clsid = Native.ShellDesktopIcons.ClsidOf(shellId);
            if (clsid.Length == 0) { Data.IconRecords.Remove(shellId); continue; }
            Native.ShellDesktopIcons.SetVisible(clsid, Data.IconRecords[shellId].PreviousVisible);
            Data.IconRecords.Remove(shellId);
        }
    }

    // The state of the native icons, for the page to show.
    public bool ShellIconVisible(string shellId)
    {
        var clsid = Native.ShellDesktopIcons.ClsidOf(shellId);
        if (clsid.Length == 0) return true;
        var item = Core.BoxShellItems.FindDesktopIcon(shellId);
        return Native.ShellDesktopIcons.IsVisible(clsid, item?.DefaultVisible ?? true);
    }

    // Shows or hides the native icon of a system entry by hand.
    public void SetShellIconVisible(string shellId, bool visible)
    {
        var clsid = Native.ShellDesktopIcons.ClsidOf(shellId);
        if (clsid.Length == 0) return;
        Native.ShellDesktopIcons.SetVisible(clsid, visible);
        if (visible) Data.IconRecords.Remove(shellId);
        Changed?.Invoke();
    }
}
