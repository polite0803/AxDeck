using System.Globalization;
using System.Text;

namespace AxDeck.Core;

// What the Services page can say about AxDeck itself and the hardware readings available on this PC. The values come from one
// short process sample and the monitor snapshot that already exists; nothing here starts a sampler or asks a sensor directly.
public enum DiagnosticState { Unknown, Available, Estimated, Unavailable }

public enum DiagnosticSubject { Monitor, Cpu, Memory, Gpu, Storage, CpuTemperature, GpuTemperature, DriveTemperature, Power, Battery, Frames }

// The existing place that can fix or explain a missing reading. The Services page decides whether opening a page should become
// a direct grant for the frame rate; the core mapping only says which settings area owns that source.
public enum DiagnosticAction { OpenMonitorSettings, OpenSensorSettings, AllowFrames }

// One line of the capability report. Detail is already localized and contains the measured value or the reason it is unavailable.
public sealed record DiagnosticFact(DiagnosticSubject Subject, DiagnosticState State, string Detail);

// One sample of this process. CpuPercent is its share of the whole machine, so it reads on the same scale as Task Manager's CPU
// column rather than as a share of one logical processor.
public sealed record ProcessResourceUsage(
    double CpuPercent, TimeSpan SampleWindow, ulong WorkingSetBytes, ulong PrivateBytes, int HandleCount, int ThreadCount, int LogicalProcessors);

public sealed record AppDiagnostics(
    DateTimeOffset At,
    Reading<ProcessResourceUsage> Resources,
    Reading<TrafficTotals> Traffic,
    int LogicalProcessors,
    ulong TotalMemoryBytes,
    ulong UsedMemoryBytes,
    IReadOnlyList<DiagnosticFact> Facts,
    IReadOnlyList<SensorStop> Stops)
{
    public static AppDiagnostics Build(
        Reading<ProcessResourceUsage> resources, MonitorSnapshot? monitor, MemorySample? memory, int logicalProcessors,
        SensorStates sensors, IReadOnlyList<SensorStop> stops, DateTimeOffset at, Reading<TrafficTotals>? traffic = null)
    {
        var totals = traffic ?? (monitor?.Traffic is { } sample
            ? Reading<TrafficTotals>.Of(sample)
            : Reading<TrafficTotals>.Unavailable(Loc.T("尚未读取本机流量", "Local traffic not sampled")));
        var facts = new List<DiagnosticFact>(11)
        {
            MemoryFact(memory, logicalProcessors),
            CpuFact(monitor, logicalProcessors),
            GpuFact(monitor, sensors.Gpu),
            StorageFact(monitor, sensors.Storage),
            CpuTemperatureFact(monitor, sensors.Cpu),
            GpuTemperatureFact(monitor, sensors.Gpu),
            DriveTemperatureFact(monitor, sensors.DiskTemperatures),
            PowerFact(monitor, sensors.Power),
            BatteryFact(monitor, sensors.Charge),
            FramesFact(monitor, sensors.Frames),
            MonitorFact(monitor, at),
        };
        return new AppDiagnostics(at, resources, totals, logicalProcessors, memory?.Total ?? 0, memory?.Used ?? 0, facts, [.. stops]);
    }

    public static string SubjectName(DiagnosticSubject subject) => subject switch
    {
        DiagnosticSubject.Monitor => Loc.T("监控快照", "Monitor snapshot"),
        DiagnosticSubject.Cpu => "CPU",
        DiagnosticSubject.Memory => Loc.T("内存", "Memory"),
        DiagnosticSubject.Gpu => "GPU",
        DiagnosticSubject.Storage => Loc.T("存储", "Storage"),
        DiagnosticSubject.CpuTemperature => Loc.T("CPU 温度", "CPU temperature"),
        DiagnosticSubject.GpuTemperature => Loc.T("GPU 温度", "GPU temperature"),
        DiagnosticSubject.DriveTemperature => Loc.T("硬盘温度", "Drive temperature"),
        DiagnosticSubject.Power => Loc.T("整机功耗", "System power"),
        DiagnosticSubject.Battery => Loc.T("电池", "Battery"),
        DiagnosticSubject.Frames => Loc.T("帧率", "Frame rate"),
        _ => subject.ToString()
    };

    public static string StateName(DiagnosticState state) => state switch
    {
        DiagnosticState.Available => Loc.T("可用", "available"),
        DiagnosticState.Estimated => Loc.T("估算", "estimated"),
        DiagnosticState.Unavailable => Loc.T("不可用", "unavailable"),
        _ => Loc.T("尚未读取", "not sampled")
    };

    // A missing sensor does not always have a repair: an absent battery or adapter is a fact, not a setting. Enhanced sensors own
    // CPU temperature and power; everything else that can be changed is on the system monitor page. Frame access is refined by the
    // Windows layer, which knows whether permission or the switch is missing.
    public static DiagnosticAction? ActionFor(DiagnosticSubject subject, DiagnosticState state) => state switch
    {
        DiagnosticState.Available or DiagnosticState.Estimated => null,
        _ => subject switch
        {
            DiagnosticSubject.CpuTemperature or DiagnosticSubject.Power => DiagnosticAction.OpenSensorSettings,
            DiagnosticSubject.GpuTemperature or DiagnosticSubject.DriveTemperature
                or DiagnosticSubject.Monitor or DiagnosticSubject.Frames => DiagnosticAction.OpenMonitorSettings,
            _ => null,
        },
    };

    // The resource line shown on the page. Millisecond-rate CPU readings are noisy, so the exact window is included.
    public string ResourceDetail => Resources.TryGet(out var sample)
        ? Loc.T(
            $"CPU {Pct(sample.CpuPercent)} · 工作集 {MonitorFormat.ProcessMemory(sample.WorkingSetBytes)} · 私有 {MonitorFormat.ProcessMemory(sample.PrivateBytes)} · "
            + $"{sample.HandleCount} 句柄 · {sample.ThreadCount} 线程 · {Math.Round(sample.SampleWindow.TotalMilliseconds)} ms",
            $"CPU {Pct(sample.CpuPercent)} · working set {MonitorFormat.ProcessMemory(sample.WorkingSetBytes)} · private {MonitorFormat.ProcessMemory(sample.PrivateBytes)} · "
            + $"{sample.HandleCount} handles · {sample.ThreadCount} threads · {Math.Round(sample.SampleWindow.TotalMilliseconds)} ms")
        : Loc.T("读取失败：", "Read failed: ") + Resources.Reason;

    public DiagnosticState ResourceState => Resources.HasValue ? DiagnosticState.Available : DiagnosticState.Unavailable;
    public DiagnosticState TrafficState => Traffic.HasValue ? DiagnosticState.Available : DiagnosticState.Unknown;

    // Today's disk and network traffic as the chart already counts it. A missing counter says so and is not drawn as zero.
    public string TrafficDetail => Traffic.TryGet(out var traffic)
        ? Loc.T(
            $"磁盘读 {MonitorFormat.Bytes(traffic.DiskRead).Text} · 写 {MonitorFormat.Bytes(traffic.DiskWritten).Text} · 下载 {MonitorFormat.Bytes(traffic.NetworkReceived).Text} · 上传 {MonitorFormat.Bytes(traffic.NetworkSent).Text}",
            $"disk read {MonitorFormat.Bytes(traffic.DiskRead).Text} · write {MonitorFormat.Bytes(traffic.DiskWritten).Text} · down {MonitorFormat.Bytes(traffic.NetworkReceived).Text} · up {MonitorFormat.Bytes(traffic.NetworkSent).Text}")
        : Traffic.Reason;

    // The text copied by "Copy diagnostics": self cost, machine summary, every capability, and each sensor that SensorGuard has
    // stopped. It deliberately contains no session contents, credentials, settings values or services.json contents.
    public string ClipboardText()
    {
        var text = new StringBuilder();
        text.AppendLine(Loc.T("AxDeck 诊断", "AxDeck diagnostics"));
        text.AppendLine(Loc.T("生成时间：", "Generated: ") + At.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        text.AppendLine(Loc.T("应用开销：", "App cost: ") + StateName(ResourceState) + " · " + ResourceDetail);
        text.AppendLine(Loc.T("处理器：", "Processors: ") + LogicalProcessors);
        text.AppendLine(Loc.T("系统内存：", "System memory: ") + (TotalMemoryBytes > 0
            ? $"{MonitorFormat.Gigabytes(UsedMemoryBytes)} / {MonitorFormat.Gigabytes(TotalMemoryBytes)} GB"
            : Loc.T("尚未读取", "not sampled")));
        text.AppendLine(Loc.T("本机流量：", "Local traffic: ") + StateName(TrafficState) + " · " + TrafficDetail);
        foreach (var fact in Facts)
            text.AppendLine($"{SubjectName(fact.Subject)}: {StateName(fact.State)} · {fact.Detail}");
        text.AppendLine(Loc.T($"自动停用的读数：{Stops.Count}", $"Automatically stopped readings: {Stops.Count}"));
        foreach (var stop in Stops) text.AppendLine("- " + SensorStops.Notice(stop));
        return text.ToString();
    }

    private static DiagnosticFact CpuFact(MonitorSnapshot? monitor, int logicalProcessors)
    {
        if (monitor?.Cpu.TryGet(out var cpu) == true)
            return new(DiagnosticSubject.Cpu, DiagnosticState.Available,
                Loc.T($"{logicalProcessors} 个逻辑处理器 · 当前占用 {Pct(cpu.Usage * 100)}",
                    $"{logicalProcessors} logical processors · {Pct(cpu.Usage * 100)} in use"));
        return new(DiagnosticSubject.Cpu, DiagnosticState.Unknown,
            Loc.T($"{logicalProcessors} 个逻辑处理器 · 当前占用尚未读取", $"{logicalProcessors} logical processors · current usage not sampled"));
    }

    private static DiagnosticFact MemoryFact(MemorySample? memory, int logicalProcessors)
    {
        if (memory is { Total: > 0 } sample)
            return new(DiagnosticSubject.Memory, DiagnosticState.Available,
                Loc.T($"{MonitorFormat.Gigabytes(sample.Used)} / {MonitorFormat.Gigabytes(sample.Total)} GB 已用 · {logicalProcessors} 个逻辑处理器",
                    $"{MonitorFormat.Gigabytes(sample.Used)} / {MonitorFormat.Gigabytes(sample.Total)} GB used · {logicalProcessors} logical processors"));
        return new(DiagnosticSubject.Memory, DiagnosticState.Unknown, Loc.T("尚未读取系统内存", "System memory not sampled"));
    }

    private static DiagnosticFact GpuFact(MonitorSnapshot? monitor, SensorState sensor)
    {
        if (monitor?.Gpus is { Count: > 0 } gpus)
        {
            string names = string.Join(" · ", gpus.Take(2).Select(gpu => $"{gpu.Name ?? "GPU"} {Pct(gpu.Usage * 100)}"));
            return new(DiagnosticSubject.Gpu, DiagnosticState.Available, names + (gpus.Count > 2 ? $" · +{gpus.Count - 2}" : ""));
        }
        return sensor == SensorState.None
            ? new(DiagnosticSubject.Gpu, DiagnosticState.Unavailable, Loc.T("没有可用显卡读数", "No graphics adapter reading is available"))
            : new(DiagnosticSubject.Gpu, DiagnosticState.Unknown, Loc.T("图形适配器尚未读取", "Graphics adapters not sampled"));
    }

    private static DiagnosticFact StorageFact(MonitorSnapshot? monitor, SensorState sensor)
    {
        if (monitor?.Storage is { Count: > 0 } drives)
            return new(DiagnosticSubject.Storage, DiagnosticState.Available,
                Loc.T($"{drives.Count} 块硬盘 · {string.Join(", ", drives.Take(3).Select(drive => drive.Model))}",
                    $"{Loc.Count(drives.Count, "drive", "drives")} · {string.Join(", ", drives.Take(3).Select(drive => drive.Model))}"));
        return sensor == SensorState.None
            ? new(DiagnosticSubject.Storage, DiagnosticState.Unavailable, Loc.T("没有检测到硬盘", "No drives detected"))
            : new(DiagnosticSubject.Storage, DiagnosticState.Unknown, Loc.T("硬盘尚未读取", "Drives not sampled"));
    }

    private static DiagnosticFact CpuTemperatureFact(MonitorSnapshot? monitor, SensorState sensor)
    {
        if (monitor?.Cpu.TryGet(out var cpu) == true && cpu.Temperature is { } degrees)
            return new(DiagnosticSubject.CpuTemperature, DiagnosticState.Available,
                $"{TemperatureFormat(degrees)} · {CpuSource(cpu.TemperatureSource)}");
        return sensor switch
        {
            SensorState.None => new(DiagnosticSubject.CpuTemperature, DiagnosticState.Unavailable,
                Loc.T("Windows 热区与硬件监视器都没有给出温度", "Neither Windows thermal zones nor a hardware monitor reported a temperature")),
            SensorState.Unknown => new(DiagnosticSubject.CpuTemperature, DiagnosticState.Unknown, Loc.T("温度尚未读取", "Temperature not sampled")),
            _ => new(DiagnosticSubject.CpuTemperature, DiagnosticState.Unknown, Loc.T("传感器可用，但当前快照没有温度", "A sensor exists, but this snapshot has no temperature"))
        };
    }

    private static DiagnosticFact GpuTemperatureFact(MonitorSnapshot? monitor, SensorState sensor)
    {
        if (monitor?.Gpus.FirstOrDefault(gpu => gpu.Temperature is not null) is { Temperature: { } degrees } gpu)
            return new(DiagnosticSubject.GpuTemperature, DiagnosticState.Available,
                $"{gpu.Name ?? "GPU"} {TemperatureFormat(degrees)}");
        return sensor == SensorState.None
            ? new(DiagnosticSubject.GpuTemperature, DiagnosticState.Unavailable,
                Loc.T("显卡驱动没有报告温度", "The graphics driver does not report a temperature"))
            : new(DiagnosticSubject.GpuTemperature, DiagnosticState.Unknown, Loc.T("显卡温度尚未读取", "GPU temperature not sampled"));
    }

    private static DiagnosticFact DriveTemperatureFact(MonitorSnapshot? monitor, SensorState sensor)
    {
        var hot = monitor?.Storage.Where(drive => drive.Temperature is not null).ToList() ?? [];
        if (hot.Count > 0)
            return new(DiagnosticSubject.DriveTemperature, DiagnosticState.Available,
                string.Join(" · ", hot.Select(drive => $"{drive.Model} {TemperatureFormat(drive.Temperature!.Celsius)}")));
        return sensor == SensorState.None
            ? new(DiagnosticSubject.DriveTemperature, DiagnosticState.Unavailable,
                Loc.T("硬盘没有报告温度或健康日志", "The drives report no temperature or health log"))
            : new(DiagnosticSubject.DriveTemperature, DiagnosticState.Unknown, Loc.T("硬盘温度尚未读取", "Drive temperatures not sampled"));
    }

    private static DiagnosticFact PowerFact(MonitorSnapshot? monitor, SensorState sensor)
    {
        if (monitor?.Power.TryGet(out var power) == true)
        {
            string source = power.Source switch
            {
                PowerSource.SystemMeter => Loc.T("整机功率计", "system meter"),
                PowerSource.Battery => Loc.T("电池放电", "battery discharge"),
                _ => Loc.T("CPU / GPU 等组件", "CPU / GPU and other components")
            };
            return new(DiagnosticSubject.Power, power.Estimated ? DiagnosticState.Estimated : DiagnosticState.Available,
                $"{PowerFormat.Watts(power.Watts)} W · {(power.Estimated ? Loc.T("估算", "estimated") : Loc.T("实测", "measured"))} · {source}");
        }
        return sensor == SensorState.None
            ? new(DiagnosticSubject.Power, DiagnosticState.Unavailable, Loc.T("没有可读的功率来源", "No readable power source"))
            : new(DiagnosticSubject.Power, DiagnosticState.Unknown, Loc.T("功耗尚未读取", "Power not sampled"));
    }

    private static DiagnosticFact BatteryFact(MonitorSnapshot? monitor, SensorState sensor)
    {
        if (monitor?.Battery.TryGet(out var battery) == true)
            return new(DiagnosticSubject.Battery, DiagnosticState.Available,
                $"{battery.Percent.ToString("0", CultureInfo.InvariantCulture)}% · {PowerText.State(battery)}");
        return sensor == SensorState.None
            ? new(DiagnosticSubject.Battery, DiagnosticState.Unavailable, BatteryModel.NoBattery)
            : new(DiagnosticSubject.Battery, DiagnosticState.Unknown, Loc.T("电池尚未读取", "Battery not sampled"));
    }

    private static DiagnosticFact FramesFact(MonitorSnapshot? monitor, SensorState sensor)
    {
        if (monitor?.Frames.TryGet(out var frames) == true)
            return new(DiagnosticSubject.Frames, DiagnosticState.Available,
                $"{frames.PerSecond.ToString("0.#", CultureInfo.InvariantCulture)} fps · {frames.Program}");
        return sensor == SensorState.None
            ? new(DiagnosticSubject.Frames, DiagnosticState.Unavailable,
                Loc.T("未获得图形捕获权限或没有前台程序", "No graphics-capture access or foreground program"))
            : new(DiagnosticSubject.Frames, DiagnosticState.Unknown, Loc.T("帧率尚未读取", "Frame rate not sampled"));
    }

    private static DiagnosticFact MonitorFact(MonitorSnapshot? monitor, DateTimeOffset at) => monitor is null
        ? new(DiagnosticSubject.Monitor, DiagnosticState.Unknown,
            Loc.T("系统监控当前未运行；本页不会为了诊断额外启动采样", "The system monitor is not running; this page does not start one just for diagnostics"))
        : new(DiagnosticSubject.Monitor, DiagnosticState.Available,
            Loc.T($"复用 {Shelf.When(monitor.Time, at)} 的系统快照，本页未增加传感器轮询",
                $"Reusing the {Shelf.When(monitor.Time, at)} system snapshot; this page adds no sensor polling"));

    private static string Pct(double value) => (double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0)
        .ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string TemperatureFormat(double celsius) => Temperatures.Format(celsius) + " °C";

    private static string CpuSource(CpuTemperatureSource source) => source switch
    {
        CpuTemperatureSource.Processor => Loc.T("处理器传感器", "processor sensor"),
        CpuTemperatureSource.HwInfo => "HWiNFO",
        CpuTemperatureSource.LibreHardwareMonitor => "LibreHardwareMonitor",
        _ => Loc.T("ACPI 热区", "ACPI thermal zone")
    };
}
