namespace AxDeck.Core;

// The plan for a batch of files dragged into or out of a box: what each file becomes, where it lands, and whether the file that is
// already there has to be kept aside first. Ported from Dasktop (https://github.com/AugSakura/Dasktop, MIT).

// What one file becomes.
public enum BoxTransferKind { Copy, Move, Shortcut }

// One file of a batch, and where it goes.
public sealed record BoxTransferPlan(string Source, string Destination, BoxTransferKind Kind, bool ReplacesExisting)
{
    // Whether this move can be handed to the shell together with the other moves of its batch.
    public bool CanBatchShellMove => Kind == BoxTransferKind.Move;
}

// The plan for a batch of files, and the names it handed out.
public sealed record BoxTransferPlanSet(List<BoxTransferPlan> Plans, HashSet<string> Reserved);

// Makes the plan for the files dragged into a folder.
public static class BoxTransferPlanner
{
    // The plan for the given paths. A file that is skipped by the rules is left out of it.
    public static BoxTransferPlanSet Plan(IReadOnlyList<string> paths, string destinationFolder, BoxDropAction action, BoxConflictPolicy conflict)
    {
        var reserved = new HashSet<string>(StringComparer.Ordinal);
        var plans = new List<BoxTransferPlan>();
        foreach (var source in paths)
        {
            // A file that is already in the target folder is left alone: moving it there changes nothing.
            if (action == BoxDropAction.Move && BoxNaming.IsDirectChildOf(source, destinationFolder)) continue;
            // Windows does not allow a folder to be moved inside itself; catching it here gives a clearer answer than the move itself.
            if (action == BoxDropAction.Move && BoxNaming.IsInside(source, destinationFolder)) continue;

            var (kind, desired) = ResolveDestination(source, destinationFolder, action);
            var destination = ResolveConflict(source, desired, conflict, reserved);
            if (destination is null) continue;
            plans.Add(new BoxTransferPlan(source, destination, kind, conflict == BoxConflictPolicy.Replace && Exists(destination)));
        }
        return new BoxTransferPlanSet(plans, reserved);
    }

    // What the file becomes and where it wants to go. A mapped file becomes a shortcut to where it already is.
    private static (BoxTransferKind Kind, string Desired) ResolveDestination(string source, string destinationFolder, BoxDropAction action)
    {
        if (action == BoxDropAction.Map && !BoxNaming.IsShortcut(source))
            return (BoxTransferKind.Shortcut, BoxNaming.DesiredShortcutPath(source, destinationFolder));
        var name = Path.GetFileName(source);
        var kind = action == BoxDropAction.Move ? BoxTransferKind.Move : BoxTransferKind.Copy;
        return (kind, Path.Combine(destinationFolder, name));
    }

    // Where the file lands once the conflict rule is applied. Null means the rule skips this file.
    private static string? ResolveConflict(string source, string desired, BoxConflictPolicy conflict, HashSet<string> reserved)
    {
        // A file that would replace itself changes nothing, so it is left out.
        if (conflict == BoxConflictPolicy.Replace && BoxNaming.ReferToSameEntry(source, desired)) return null;
        switch (conflict)
        {
            case BoxConflictPolicy.Rename: return BoxNaming.ResolveRenamedDestination(desired, reserved);
            case BoxConflictPolicy.Skip:
                // A file of that name is already there, or another file of this batch has taken the name.
                if (Exists(desired)) return null;
                return reserved.Add(BoxNaming.PathKey(desired)) ? desired : null;
            default:
                // Two files of one batch landing on the same name would have the second replace the first, so the second is left out.
                return reserved.Add(BoxNaming.PathKey(desired)) ? desired : null;
        }
    }

    // Whether a path is taken.
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
