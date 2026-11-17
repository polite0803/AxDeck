using System.Management;
using System.Runtime.InteropServices;
using AxDeck.Core;

namespace AxDeck.Native;

// The day's traffic: the bytes read from and written to the disks, and the bytes received over and sent through the network,
// counted since the day began. The system gives counters that count since the machine started, so the day's totals are kept
// here: the counter's reading when the day began is remembered, and what it grew by since is added (TrafficToday).
//
// The disks' counters come from Windows' own performance counters through WMI: the raw class Win32_PerfRawData_PerfDisk_PhysicalDisk
// gives the total bytes read and written since the machine started, as a raw (not a rate) counter. Windows' performance counters
// give no cumulative disk counter of their own (PDH_CSTATUS_NO_COUNTER for "Disk Read Bytes"), and the IOCTL_DISK_PERFORMANCE
// interface is not answered by an NVMe drive, so the performance counters are the one source that answers everywhere.
// The first query costs about half a second (WMI starts its providers), the later ones 13 to 32 ms, so this is read on its own
// slow timer, never on a tick and never on the drawing thread.
//
// The network's counters come from the adapters themselves (GetIfTable2): InOctets and OutOctets count since the machine started.
// Only the physical adapters are counted, as the virtual ones (Hyper-V, VPN, loopback) carry traffic another adapter already
// counted.
internal sealed class TrafficCounters
{
    // How often the disks' counters are read: the query costs tens of milliseconds, and the totals change slowly.
    private const double DiskEvery = 20;

    private readonly object _gate = new();
    private double _diskAt = double.NegativeInfinity;
    private (long Read, long Written) _disk = (0, 0);
    private bool _diskKnown;
    private long _received, _sent;
    private bool _networkKnown;

    // Whether the disks' counters are due again. The slow caller asks first, so a tick never pays for the query.
    public bool DiskDue(double now)
    {
        lock (_gate) { return now - _diskAt >= DiskEvery; }
    }

    // Reads the disks' counters, which takes tens of milliseconds; the caller does it on a background thread.
    public void ReadDisk(double now)
    {
        var counters = Query();
        lock (_gate)
        {
            _diskAt = now;
            if (counters is not { } value) return;
            _disk = value; _diskKnown = true;
        }
    }

    // The disks' counters as Windows' performance counters give them: the total bytes read from and written to the disks since
    // the machine started. The raw class' fields carry a "PerSec" name, but the raw counter is a total, as its name says: the
    // difference between two readings is the bytes moved between them.
    private static (long Read, long Written)? Query()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DiskReadBytesPersec, DiskWriteBytesPersec FROM Win32_PerfRawData_PerfDisk_PhysicalDisk WHERE Name='_Total'");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                long read = Convert.ToInt64(item["DiskReadBytesPersec"]);
                long written = Convert.ToInt64(item["DiskWriteBytesPersec"]);
                return (read, written);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        return null;
    }

    // The network's cumulative counters, as the adapters give them. The table is read every time: it is the same call the network
    // readings use, and it is the only way to see an adapter that joined since the last reading.
    public void ReadNetwork()
    {
        if (GetIfTable2(out var table) != 0) return;
        long received = 0, sent = 0;
        try
        {
            int count = Marshal.ReadInt32(table);
            for (int i = 0; i < count; i++)
            {
                var row = table + 8 + i * RowSize;
                if (!NetworkProvider.Physical(row)) continue;
                var (inbound, outbound) = NetworkProvider.Counters(row);
                received += (long)inbound; sent += (long)outbound;
            }
        }
        finally { FreeMibTable(table); }
        lock (_gate) { _received = received; _sent = sent; _networkKnown = true; }
    }

    // The four counters as they stand, or null where one of them is not known yet.
    public TrafficToday.Counters? Counters()
    {
        lock (_gate)
        {
            if (!_diskKnown || !_networkKnown) return null;
            return new(_disk.Read, _disk.Written, _received, _sent);
        }
    }

    // MIB_IF_ROW2 on 64-bit Windows: InterfaceLuid at 0, Type at 1128, the status flags at 1152, InOctets at 1208 and
    // OutOctets at 1280. The table's rows start 8 bytes in, after NumEntries and its padding.
    private const int RowSize = 1352;
    [DllImport("iphlpapi.dll")] private static extern int GetIfTable2(out IntPtr table);
    [DllImport("iphlpapi.dll")] private static extern void FreeMibTable(IntPtr memory);
}
