using AxDeck.Core;

// The Services page's capability report: a missing reading keeps its honest state, component power stays an estimate, and the
// clipboard report cannot turn local infrastructure or a conversation into text a user might paste into an issue.
static class AppDiagnosticsTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(8));

    public static void Run(Action<string, bool> check)
    {
        Unknown(check);
        Estimates(check);
        Actions(check);
        Clipboard(check);
    }

    private static void Unknown(Action<string, bool> check)
    {
        var diagnostics = AppDiagnostics.Build(
            Reading<ProcessResourceUsage>.Unavailable("probe unavailable"), null, null, 16, SensorStates.Unknown, [], At);
        check("AppDiagnostics: an unsampled capability is not called unavailable",
            diagnostics.Facts.Single(fact => fact.Subject == DiagnosticSubject.Cpu).State == DiagnosticState.Unknown
            && diagnostics.Facts.Single(fact => fact.Subject == DiagnosticSubject.Gpu).State == DiagnosticState.Unknown);
        check("AppDiagnostics: an unsampled monitor says the page did not start one",
            diagnostics.Facts.Single(fact => fact.Subject == DiagnosticSubject.Monitor).State == DiagnosticState.Unknown);
        check("AppDiagnostics: a confirmed absent sensor is unavailable",
            AppDiagnostics.Build(Reading<ProcessResourceUsage>.Unavailable("test"), null, null, 16,
                new SensorStates(SensorState.None, SensorState.None, SensorState.None, SensorState.None)
                {
                    Power = SensorState.None,
                    Charge = SensorState.None,
                    Frames = SensorState.None,
                }, [], At).Facts.Single(fact => fact.Subject == DiagnosticSubject.GpuTemperature).State == DiagnosticState.Unavailable);
        check("AppDiagnostics: missing traffic stays unsampled and is not written as zero",
            diagnostics.TrafficState == DiagnosticState.Unknown && diagnostics.TrafficDetail == "尚未读取本机流量");
    }

    private static void Estimates(Action<string, bool> check)
    {
        var memory = new MemorySample(32UL << 30, 18UL << 30, 0, 0);
        var power = new PowerSample(28.7, PowerOrigin.Estimated, PowerSource.Components,
            [new PowerPart("cpu", "CPU", 28.7, "RAPL")]);
        var monitor = new MonitorSnapshot(At, 16,
            Reading<CpuSample>.Of(new CpuSample([], Reading<double>.Unavailable("test")) with { Temperature = null }),
            Reading<MemorySample>.Of(memory), Reading<NetworkSample>.Unavailable("test"), Reading<GpuSample>.Unavailable("test"),
            Reading<ProcessList>.Unavailable("test"))
        {
            Gpus = [new GpuSample(.28, Reading<ulong>.Unavailable("test")) { Name = "Intel Arc 140T", UsageSource = "Intel" }],
            Power = Reading<PowerSample>.Of(power),
            Battery = Reading<BatterySample>.Unavailable("test"),
            Traffic = new TrafficTotals(0, 0, 0, 0),
        };
        var diagnostics = AppDiagnostics.Build(
            Reading<ProcessResourceUsage>.Of(new ProcessResourceUsage(1.2, TimeSpan.FromMilliseconds(250), 100UL << 20, 70UL << 20, 222, 18, 16)),
            monitor, memory, 16,
            new SensorStates(SensorState.None, SensorState.None, SensorState.Found, SensorState.Found)
            {
                Power = SensorState.Found,
                Charge = SensorState.None,
                Frames = SensorState.None,
            }, [new SensorStop(GuardedReading.CpuTemperature, "CPU", "CPU", 1, At)], At);
        check("AppDiagnostics: component power is estimated, never measured",
            diagnostics.Facts.Single(fact => fact.Subject == DiagnosticSubject.Power).State == DiagnosticState.Estimated);
        check("AppDiagnostics: a sampled zero is a real zero and not confusion with missing data",
            diagnostics.TrafficState == DiagnosticState.Available && diagnostics.TrafficDetail.Contains("磁盘读 0 KB", StringComparison.Ordinal));
        check("AppDiagnostics: the stopped readings are counted for the page",
            diagnostics.Stops.Count == 1 && diagnostics.ClipboardText().Contains("自动停用的读数：1", StringComparison.Ordinal));
        check("AppDiagnostics: the adapter name and usage are shown without inventing a GPU temperature",
            diagnostics.Facts.Single(fact => fact.Subject == DiagnosticSubject.Gpu).Detail.Contains("Intel Arc 140T", StringComparison.Ordinal)
            && diagnostics.Facts.Single(fact => fact.Subject == DiagnosticSubject.GpuTemperature).State == DiagnosticState.Unavailable);
    }

    private static void Actions(Action<string, bool> check)
    {
        check("AppDiagnostics: CPU temperature opens the enhanced sensors page",
            AppDiagnostics.ActionFor(DiagnosticSubject.CpuTemperature, DiagnosticState.Unavailable) == DiagnosticAction.OpenSensorSettings);
        check("AppDiagnostics: system power opens the enhanced sensors page",
            AppDiagnostics.ActionFor(DiagnosticSubject.Power, DiagnosticState.Unknown) == DiagnosticAction.OpenSensorSettings);
        check("AppDiagnostics: GPU temperature never points at PawnIO",
            AppDiagnostics.ActionFor(DiagnosticSubject.GpuTemperature, DiagnosticState.Unavailable) == DiagnosticAction.OpenMonitorSettings);
        check("AppDiagnostics: drive temperature and frames open the monitor settings",
            AppDiagnostics.ActionFor(DiagnosticSubject.DriveTemperature, DiagnosticState.Unavailable) == DiagnosticAction.OpenMonitorSettings
            && AppDiagnostics.ActionFor(DiagnosticSubject.Frames, DiagnosticState.Unavailable) == DiagnosticAction.OpenMonitorSettings);
        check("AppDiagnostics: confirmed absent hardware has no misleading repair",
            AppDiagnostics.ActionFor(DiagnosticSubject.Battery, DiagnosticState.Unavailable) is null
            && AppDiagnostics.ActionFor(DiagnosticSubject.Gpu, DiagnosticState.Unavailable) is null
            && AppDiagnostics.ActionFor(DiagnosticSubject.Storage, DiagnosticState.Unavailable) is null);
        check("AppDiagnostics: a real or estimated reading has no repair button",
            AppDiagnostics.ActionFor(DiagnosticSubject.CpuTemperature, DiagnosticState.Available) is null
            && AppDiagnostics.ActionFor(DiagnosticSubject.Power, DiagnosticState.Estimated) is null);
    }

    private static void Clipboard(Action<string, bool> check)
    {
        var diagnostics = AppDiagnostics.Build(
            Reading<ProcessResourceUsage>.Of(new ProcessResourceUsage(0.4, TimeSpan.FromMilliseconds(250), 128UL << 20, 80UL << 20, 200, 16, 16)),
            null, new MemorySample(32UL << 30, 16UL << 30, 0, 0), 16, SensorStates.Unknown, [], At,
            Reading<TrafficTotals>.Unavailable("test"));
        string text = diagnostics.ClipboardText();
        check("AppDiagnostics: the clipboard text carries the app cost and machine summary",
            text.Contains("应用开销：", StringComparison.Ordinal) && text.Contains("系统内存：", StringComparison.Ordinal));
        check("AppDiagnostics: the clipboard text has no network infrastructure or conversation text",
            !text.Contains("nas.example", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("ssh", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("15432", StringComparison.Ordinal)
            && !text.Contains("rollout-", StringComparison.OrdinalIgnoreCase));
    }
}
