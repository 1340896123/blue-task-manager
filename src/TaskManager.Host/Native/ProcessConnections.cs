using System.Buffers.Binary;
using System.Net;
using System.Runtime.InteropServices;
using TaskManager.Host.Models;
using Windows.Win32;
using Windows.Win32.NetworkManagement.IpHelper;

namespace TaskManager.Host.Native;

public sealed class ProcessConnections
{
    private readonly object gate = new();
    private readonly Dictionary<string, ConnectionsResult> cache = new();
    private int cachedRows;
    public ConnectionsResult Read(uint pid, string creation)
    {
        using var handle = ProcessAccess.Open(pid, creation);
        var identity = $"{pid}:{creation}";
        lock (gate) if (cache.TryGetValue(identity, out var prior) && DateTime.UtcNow - prior.CapturedAtUtc < TimeSpan.FromSeconds(1)) return prior;
        var rows = new List<ConnectionDto>(); var errors = new Dictionary<string, Unavailable>(); var truncated = false;
        foreach (var af in new uint[] { 2, 23 }) foreach (var tcp in new[] { true, false }) ReadTable(pid, af, tcp, rows, errors, ref truncated);
        ProcessAccess.Validate(handle, creation);
        var result = new ConnectionsResult(DateTime.UtcNow, identity, rows, errors, truncated);
        lock (gate)
        {
            if (cache.Remove(identity, out var existing)) cachedRows -= existing.Connections.Count;
            while (cache.Count > 0 && (cache.Count >= 128 || cachedRows + rows.Count > 20_000)) { var key = cache.Keys.First(); cachedRows -= cache[key].Connections.Count; cache.Remove(key); }
            cache[identity] = result; cachedRows += rows.Count;
        }
        return result;
    }
    private static unsafe void ReadTable(uint pid, uint af, bool tcp, List<ConnectionDto> rows, Dictionary<string, Unavailable> errors, ref bool truncated)
    {
        var field = $"{(tcp ? "tcp" : "udp")}{(af == 2 ? "4" : "6")}";
        uint size = 0;
        uint Read(Span<byte> bytes) => tcp ? PInvoke.GetExtendedTcpTable(bytes, ref size, false, af, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0) : PInvoke.GetExtendedUdpTable(bytes, ref size, false, af, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
        var error = Read(default);
        for (var attempt = 0; attempt < 3 && (error == 122 || attempt == 0); attempt++)
        {
            if (size is < 4 or > 4 * 1024 * 1024) { errors[field] = new("partial", "Windows连接表超过4 MiB的读取上限。"); return; }
            var bytes = new byte[size]; error = Read(bytes);
            if (error == 122) continue;
            if (error != 0) break;
            fixed (byte* data = bytes)
            {
                uint count = *(uint*)data;
                var rowSize = tcp ? af == 2 ? sizeof(MIB_TCPROW_OWNER_PID) : sizeof(MIB_TCP6ROW_OWNER_PID) : af == 2 ? sizeof(MIB_UDPROW_OWNER_PID) : sizeof(MIB_UDP6ROW_OWNER_PID);
                if (count > (bytes.Length - 4) / rowSize) { errors[field] = new("error", "Windows连接表长度无效。"); return; }
                for (var i = 0; i < count; i++)
                {
                    var ptr = data + 4 + i * rowSize;
                    ConnectionDto? row = null;
                    if (tcp && af == 2)
                    {
                        var item = (MIB_TCPROW_OWNER_PID*)ptr;
                        if (item->dwOwningPid == pid) row = new("TCP", "IPv4", new IPAddress(item->dwLocalAddr).ToString(), Port(item->dwLocalPort), new IPAddress(item->dwRemoteAddr).ToString(), Port(item->dwRemotePort), ((MIB_TCP_STATE)item->dwState).ToString(), pid);
                    }
                    else if (tcp)
                    {
                        var item = (MIB_TCP6ROW_OWNER_PID*)ptr;
                        if (item->dwOwningPid == pid) row = new("TCP", "IPv6", new IPAddress(item->ucLocalAddr.AsSpan(), item->dwLocalScopeId).ToString(), Port(item->dwLocalPort), new IPAddress(item->ucRemoteAddr.AsSpan(), item->dwRemoteScopeId).ToString(), Port(item->dwRemotePort), ((MIB_TCP_STATE)item->dwState).ToString(), pid);
                    }
                    else if (af == 2)
                    {
                        var item = (MIB_UDPROW_OWNER_PID*)ptr;
                        if (item->dwOwningPid == pid) row = new("UDP", "IPv4", new IPAddress(item->dwLocalAddr).ToString(), Port(item->dwLocalPort), null, null, null, pid);
                    }
                    else
                    {
                        var item = (MIB_UDP6ROW_OWNER_PID*)ptr;
                        if (item->dwOwningPid == pid) row = new("UDP", "IPv6", new IPAddress(item->ucLocalAddr.AsSpan(), item->dwLocalScopeId).ToString(), Port(item->dwLocalPort), null, null, null, pid);
                    }
                    if (row is not null) { if (rows.Count < 5000) rows.Add(row); else truncated = true; }
                }
            }
            return;
        }
        errors[field] = NativeSupport.Error((int)error, "读取Windows端点表失败");
    }
    private static int Port(uint networkPort) => BinaryPrimitives.ReverseEndianness((ushort)networkPort);
}
