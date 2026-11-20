namespace AxDeck.Core;

// A read-only view of what the local records say about today's work. It joins the focus timer's own history with the
// metadata samples saved for those sessions, so the panel can say when work happened without reading a conversation,
// scanning a project folder or guessing the current app from a window title.
public sealed record WorkOverview(
    int FocusCount,
    int FocusMinutes,
    int ActiveSourceCount,
    IReadOnlyList<ProviderId> ActiveSources,
    IReadOnlyList<WorkOverviewProject> Projects);

public sealed record WorkOverviewProject(ProjectEntry Project, int FocusCount, int FocusMinutes, DateTimeOffset? LastFocusedAt);

public static class WorkOverviewBuilder
{
    private static readonly IReadOnlyList<ProviderId> NoSources = [];

    // Builds the day's account from local midnight. A session that began yesterday and ended today counts where it ended;
    // this is a display of today's work, not a billing or time-tracking record.
    public static WorkOverview Build(FocusTimer timer, IEnumerable<ProjectEntry>? projects = null, DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.Now;
        var day = at.Date;
        var sessions = (timer.History ?? []).Where(session => session.EndedAt >= day && session.EndedAt <= at).ToList();
        var sourceSet = sessions.SelectMany(session => session.WorkSources).ToHashSet();
        // A sample with any record still being written means a watched tool was working then. This is metadata from the
        // focus session file, never a read of the session record itself. Declaring a tool for a session is not evidence
        // that the tool actually worked, so only recorded activity is reported here.
        var activeSources = sessions.SelectMany(session => session.Samples ?? [])
            .SelectMany(sample => sample.Activity)
            .Where(item => item.ActiveRecordCount > 0 && sourceSet.Contains(item.Source))
            .Select(item => item.Source)
            .Distinct()
            .OrderBy(source => source)
            .ToList();

        var projectList = projects?.Where(project => project is not null).ToList() ?? [];
        var grouped = sessions.Where(session => session.ProjectId is { } id && !Equals(id, Guid.Empty))
            .GroupBy(session => session.ProjectId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        var rows = new List<WorkOverviewProject>();
        foreach (var project in projectList)
        {
            if (!grouped.TryGetValue(project.Id, out var owned)) continue;
            rows.Add(new WorkOverviewProject(project, owned.Count,
                owned.Sum(session => Math.Max(0, session.TargetSeconds)) / 60,
                owned.Max(session => session.EndedAt)));
        }
        foreach (var (id, owned) in grouped.Where(item => projectList.All(project => project.Id != item.Key)))
        {
            rows.Add(new WorkOverviewProject(new ProjectEntry { Id = id, DisplayName = Loc.T("未登记的项目", "Unregistered project") },
                owned.Count, owned.Sum(session => Math.Max(0, session.TargetSeconds)) / 60,
                owned.Max(session => session.EndedAt)));
        }
        rows = rows.OrderByDescending(row => row.LastFocusedAt).ThenBy(row => row.Project.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();

        return new WorkOverview(sessions.Count, sessions.Sum(session => Math.Max(0, session.TargetSeconds)) / 60,
            activeSources.Count, activeSources.Count == 0 ? NoSources : activeSources, rows);
    }
}
