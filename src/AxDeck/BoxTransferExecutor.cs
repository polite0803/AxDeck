using System.IO;
using AxDeck.Core;

namespace AxDeck;

// The transfer of real files into and out of a box, and what happens when it goes wrong. A transfer that fails halfway is undone:
// the files that were moved are moved back, the copies that were made are removed, and a file that was put aside is put back.
// Ported from Dasktop (https://github.com/AugSakura/Dasktop, MIT), which rolls back the same way.

internal static class BoxTransferExecutor
{
    // What one file of the batch became, so it can be undone.
    private sealed record Completed(BoxTransferPlan Plan, string? Backup);

    // The files as they end up in the target folder. The order is the order they were given in.
    public static IReadOnlyList<string> Execute(IReadOnlyList<string> paths, string destinationFolder, BoxDropAction action, BoxConflictPolicy conflict)
    {
        var plan = BoxTransferPlanner.Plan(paths, destinationFolder, action, conflict);
        if (plan.Plans.Count == 0) return [];
        var completed = new List<Completed>();
        var index = 0;

        while (index < plan.Plans.Count)
        {
            var current = plan.Plans[index];
            // The moves that follow each other are handed to the shell in one call, so the desktop does not redraw for each file.
            if (current.CanBatchShellMove)
            {
                var next = index + 1;
                while (next < plan.Plans.Count && plan.Plans[next].CanBatchShellMove) next++;
                var batch = plan.Plans.GetRange(index, next - index);
                try { Native.ShellFileOperations.Move(batch.Select(item => (item.Source, item.Destination))); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                    RollbackMoves(batch);
                    Rollback(completed);
                    throw;
                }
                completed.AddRange(batch.Select(item => new Completed(item, null)));
                index = next;
                continue;
            }

            // The file that is put aside when it is replaced: it is only removed once the whole batch has succeeded, so a failure
            // can put it back.
            string? backup = null;
            if (current.ReplacesExisting) backup = PrepareBackup(current.Destination);
            try
            {
                ExecuteOne(current);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                Cleanup(current);
                if (backup is not null) RestoreBackup(backup, current.Destination);
                Rollback(completed);
                throw;
            }
            completed.Add(new Completed(current, backup));
            index++;
        }

        // The whole batch has succeeded: the files that were put aside are no longer needed.
        foreach (var item in completed) if (item.Backup is { } backup) DeletePath(backup);
        return [.. plan.Plans.Select(item => item.Destination)];
    }

    // One file: a copy, a move, or a shortcut to where the file already is.
    private static void ExecuteOne(BoxTransferPlan plan)
    {
        switch (plan.Kind)
        {
            case BoxTransferKind.Move:
                if (File.Exists(plan.Destination) || Directory.Exists(plan.Destination))
                    throw new IOException(Loc.T("目标文件已经存在，已停止移动。", "The target file already exists; the move was stopped."));
                Native.ShellFileOperations.Move([(plan.Source, plan.Destination)]);
                break;
            case BoxTransferKind.Shortcut:
                Native.ShellShortcuts.Create(plan.Source, plan.Destination);
                break;
            default:
                CopyPath(plan.Source, plan.Destination);
                break;
        }
    }

    // Copies a file or a folder.
    private static void CopyPath(string source, string destination)
    {
        if (Directory.Exists(source))
        {
            CopyDirectory(source, destination);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }

    // Copies a folder and what is in it.
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var folder in Directory.EnumerateDirectories(source)) CopyDirectory(folder, Path.Combine(destination, Path.GetFileName(folder)));
    }

    // Puts the file that is about to be replaced aside, under a name of its own, so it can be put back.
    private static string? PrepareBackup(string destination)
    {
        try
        {
            var backup = destination + ".axdeck-backup";
            if (File.Exists(backup) || Directory.Exists(backup)) DeletePath(backup);
            if (Directory.Exists(destination)) Directory.Move(destination, backup);
            else if (File.Exists(destination)) File.Move(destination, backup);
            else return null;
            return backup;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return null; }
    }

    // Puts a file that was put aside back.
    private static void RestoreBackup(string backup, string destination)
    {
        try
        {
            if (!File.Exists(backup) && !Directory.Exists(backup)) return;
            if (File.Exists(destination) || Directory.Exists(destination)) DeletePath(destination);
            if (Directory.Exists(backup)) Directory.Move(backup, destination);
            else File.Move(backup, destination);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    // Undoes what a failed transfer left behind.
    private static void Cleanup(BoxTransferPlan plan)
    {
        if (plan.Kind == BoxTransferKind.Move) RollbackMoves([plan]);
        else DeletePath(plan.Destination);
    }

    // Moves the files of a failed batch back, but only where the shell really moved them: the source is gone and the target is
    // there. Otherwise a file the user has since touched would be taken away.
    private static void RollbackMoves(IReadOnlyList<BoxTransferPlan> plans)
    {
        for (var index = plans.Count - 1; index >= 0; index--)
        {
            var plan = plans[index];
            if (!File.Exists(plan.Source) && !Directory.Exists(plan.Source) && (File.Exists(plan.Destination) || Directory.Exists(plan.Destination)))
            {
                try { Native.ShellFileOperations.Move([(plan.Destination, plan.Source)]); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
        }
    }

    // Undoes the transfers that had already succeeded, in the reverse order, so a folder that holds another does not get in the way.
    private static void Rollback(IReadOnlyList<Completed> completed)
    {
        for (var index = completed.Count - 1; index >= 0; index--)
        {
            var item = completed[index];
            if (item.Plan.Kind == BoxTransferKind.Move)
            {
                if (File.Exists(item.Plan.Destination) || Directory.Exists(item.Plan.Destination))
                {
                    try { Native.ShellFileOperations.Move([(item.Plan.Destination, item.Plan.Source)]); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                }
            }
            else DeletePath(item.Plan.Destination);
            if (item.Backup is { } backup) RestoreBackup(backup, item.Plan.Destination);
        }
    }

    // Removes a file or a folder, whatever it is.
    private static void DeletePath(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }
}
