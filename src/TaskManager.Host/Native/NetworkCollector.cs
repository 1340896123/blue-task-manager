using System.Diagnostics;
using System.Globalization;
using TaskManager.Host.Models;
using Windows.Win32;
using Windows.Win32.NetworkManagement.IpHelper;

namespace TaskManager.Host.Native;

internal sealed class NetworkCollector
{
    private Dictionary<ulong, (ulong Receive, ulong Send, bool Up)>? _previous;
    private long _tick;
    internal unsafe IReadOnlyList<NetworkAdapterDto> Collect(SystemDto system, long tick)
    {
        var error = PInvoke.GetIfTable2(out MIB_IF_TABLE2* table);
        if (error != 0)
        {
            system.Unavailable["networkReceiveBytesPerSecond"] = system.Unavailable["networkSendBytesPerSecond"] = NativeSupport.Error((int)error, "读取网卡计数失败");
            _previous = null;
            return [];
        }
        try
        {
            // Bound managed state even if a faulty driver reports an unreasonable number of interfaces.
            if (table->NumEntries > 4096)
            {
                system.Unavailable["networkReceiveBytesPerSecond"] = system.Unavailable["networkSendBytesPerSecond"] = new("capacity", "网络接口表超过4096项边界。");
                _previous = null;
                return [];
            }
            var current = new Dictionary<ulong, (ulong Receive, ulong Send, bool Up)>();
            var result = new List<NetworkAdapterDto>((int)table->NumEntries);
            var seconds = Stopwatch.GetElapsedTime(_tick, tick).TotalSeconds;
            for (var i = 0; i < table->NumEntries; i++)
            {
                var row = table->Table[i];
                var id = row.InterfaceLuid.Value;
                var up = (int)row.OperStatus == 1;
                var unavailable = new Dictionary<string, Unavailable>();
                double? receive = null, send = null;
                if (!up) unavailable["receiveBytesPerSecond"] = unavailable["sendBytesPerSecond"] = new("disconnected", "接口当前未处于Up状态，吞吐不可用。");
                else if (_previous is null || !_previous.TryGetValue(id, out var before) || !before.Up || seconds <= 0)
                    unavailable["receiveBytesPerSecond"] = unavailable["sendBytesPerSecond"] = NativeSupport.WarmingUp;
                else
                {
                    if (row.InOctets >= before.Receive) receive = (row.InOctets - before.Receive) / seconds;
                    else unavailable["receiveBytesPerSecond"] = NativeSupport.CounterReset;
                    if (row.OutOctets >= before.Send) send = (row.OutOctets - before.Send) / seconds;
                    else unavailable["sendBytesPerSecond"] = NativeSupport.CounterReset;
                }
                current[id] = (row.InOctets, row.OutOctets, up);
                var hardware = row.InterfaceAndOperStatusFlags.HardwareInterface;
                result.Add(new(id.ToString(CultureInfo.InvariantCulture), row.InterfaceIndex, row.Alias.ToString(), row.Description.ToString(),
                    row.OperStatus.ToString(), row.Type, hardware, !hardware && row.Type is not (24 or 131), row.Type == 24, row.Type == 131,
                    row.ReceiveLinkSpeed.ToString(CultureInfo.InvariantCulture), row.TransmitLinkSpeed.ToString(CultureInfo.InvariantCulture),
                    row.InOctets.ToString(CultureInfo.InvariantCulture), row.OutOctets.ToString(CultureInfo.InvariantCulture), receive, send, unavailable));
            }
            var included = result.Where(row => row.Status == "IfOperStatusUp" && !row.IsLoopback && !row.IsTunnel).ToArray();
            system.NetworkInterfaceCount = included.Length;
            var beforeIncluded = _previous?.Where(row => row.Value.Up).Select(row => row.Key).ToHashSet();
            // A topology change invalidates only the aggregate for one sample; individual stable interfaces remain valid.
            var sameSet = _previous is not null && result.Where(row => row.Status == "IfOperStatusUp").All(row => beforeIncluded!.Contains(ulong.Parse(row.Id, CultureInfo.InvariantCulture))) && beforeIncluded!.Count == result.Count(row => row.Status == "IfOperStatusUp");
            if (included.Length > 0 && sameSet && included.All(row => row.ReceiveBytesPerSecond.HasValue && row.SendBytesPerSecond.HasValue))
            {
                system.NetworkReceiveBytesPerSecond = included.Sum(row => row.ReceiveBytesPerSecond!.Value);
                system.NetworkSendBytesPerSecond = included.Sum(row => row.SendBytesPerSecond!.Value);
            }
            else system.Unavailable["networkReceiveBytesPerSecond"] = system.Unavailable["networkSendBytesPerSecond"] = included.Length == 0
                ? new("notCollected", "未发现活动的非回环、非隧道网络接口。") : NativeSupport.WarmingUp;
            _previous = current;
            _tick = tick;
            return result.OrderByDescending(row => row.IsHardware).ThenBy(row => row.Alias, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally { PInvoke.FreeMibTable(table); }
    }
}
