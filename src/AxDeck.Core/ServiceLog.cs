namespace AxDeck.Core;

// The tail of a service's log, and how it is read. The log itself belongs to the service and lives on this machine; only
// the last lines are shown, so a long log does not have to be read whole to see what just happened.
public static class ServiceLog
{
    // How many lines the log window shows by default.
    public const int DefaultLines = 300;

    // The last lines of the log, oldest first. A log with fewer lines than asked for is returned whole.
    public static IReadOnlyList<string> Tail(IEnumerable<string> lines, int count)
    {
        if (count <= 0) return [];
        var list = new List<string>();
        foreach (var line in lines)
        {
            list.Add(line);
            if (list.Count > count) list.RemoveAt(0);
        }
        return list;
    }

    // The text the log window shows: the lines, oldest first, with the empty ones kept so the layout does not jump.
    public static string Text(IEnumerable<string> lines, int count) => string.Join("\n", Tail(lines, count));

    // What the log window says when there is nothing to show.
    public static string Empty => Loc.T("还没有日志。服务启动后，它的输出会写到这里。", "There is no log yet. Once the service starts, its output is written here.");
}
