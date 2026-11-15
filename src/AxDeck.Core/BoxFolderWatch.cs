namespace AxDeck.Core;

// The look of a box's folder at one moment, in a form that can be compared: a box shows the files of its folder, and the
// folder may be changed from anywhere else (Explorer, a browser saving a file, another program). The window looks at it now
// and then and redraws when the look has changed.
//
// 原版的分组也会跟着文件夹变（Dasktop 里图标是它自己那层壳读目录画出来的），所以这里也要有。做法是隔一会儿看一眼：
// 逐项比较太琐碎，改成把「里面有什么、各自多大、各自什么时候改的」拼成一句话，两句话不一样才重画。
public static class BoxFolderWatch
{
    // The signature of a folder's contents: one line per entry, sorted by name, so that the order the file system lists
    // them in does not matter. Adding, removing or renaming an entry changes it, and so does a file being written.
    public static string Signature(IEnumerable<(string Name, long Length, long ModifiedUtcTicks)> entries)
        => string.Join('\n', entries
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => $"{entry.Name}|{entry.Length}|{entry.ModifiedUtcTicks}"));

    // The signature of the folder as it is on the disk now. A folder that is not there has the signature of an empty
    // one, so the box shows it as empty rather than keeping what it showed before.
    public static string Of(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return "";
        try
        {
            var entries = new List<(string Name, long Length, long ModifiedUtcTicks)>();
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                // 读不到某一项（权限、别处正在删它）时略过它：这只是个签名，不值得为此让整次比较失效。
                var info = new FileInfo(path);
                long length = 0, ticks = 0;
                if (info.Exists) { length = info.Length; ticks = info.LastWriteTimeUtc.Ticks; }
                else
                {
                    var directory = new DirectoryInfo(path);
                    if (!directory.Exists) continue;
                    ticks = directory.LastWriteTimeUtc.Ticks;
                }
                entries.Add((Path.GetFileName(path), length, ticks));
            }
            return Signature(entries);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return "";
        }
    }
}