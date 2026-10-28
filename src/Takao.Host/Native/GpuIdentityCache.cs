using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Takao.Host.Models;
using Windows.Win32;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Foundation;

namespace Takao.Host.Native;

internal sealed record GpuIdentity(string? Description, bool? IsSoftware, DateTime? CapturedAtUtc, Unavailable? Error);

/// <summary>Used only by the deferred PDH worker; no native/COM objects survive enumeration.</summary>
internal sealed class GpuIdentityCache
{
    private Dictionary<ulong, GpuIdentity> _adapters = new();
    private long? _lastAttempt;
    private Unavailable? _enumerationError;
    private DateTime? _capturedAtUtc;

    internal IReadOnlyDictionary<string, GpuIdentity> Resolve(IEnumerable<string> source)
    {
        var requested = source.Distinct(StringComparer.Ordinal).Take(256).ToArray();
        if (requested.Length == 0) return new Dictionary<string, GpuIdentity>();
        var unknown = requested.Any(id => !TryLuid(id, out var luid) || !_adapters.ContainsKey(luid));
        var elapsed = _lastAttempt.HasValue ? Stopwatch.GetElapsedTime(_lastAttempt.Value) : TimeSpan.MaxValue;
        if (!_lastAttempt.HasValue || elapsed >= TimeSpan.FromMinutes(5) || unknown && elapsed >= TimeSpan.FromSeconds(10)) Refresh();
        return requested.ToDictionary(id => id, id => TryLuid(id, out var luid) && _adapters.TryGetValue(luid, out var adapter) ? adapter
            : new GpuIdentity(null, null, _capturedAtUtc, _enumerationError ?? new("notFound", "DXGI未找到与此计数器LUID精确匹配的适配器；保留原始身份。")), StringComparer.Ordinal);
    }

    private void Refresh()
    {
        _lastAttempt = Stopwatch.GetTimestamp();
        IDXGIFactory1? factory = null;
        var result = new Dictionary<ulong, GpuIdentity>();
        Unavailable? error = null;
        try
        {
            var status = PInvoke.CreateDXGIFactory1(out factory);
            status.ThrowOnFailure();
            if (factory is null) throw new COMException("DXGI未返回工厂对象。");
            for (uint index = 0; index <= 256; index++)
            {
                IDXGIAdapter1? adapter = null;
                try
                {
                    status = factory.EnumAdapters1(index, out adapter);
                    if (status == HRESULT.DXGI_ERROR_NOT_FOUND) break;
                    if (status.Failed) { error = Failure(new COMException("DXGI枚举失败。", status.Value)); break; }
                    if (index == 256) { error = new("capacity", "DXGI适配器超过256项边界，未返回部分身份映射。"); result.Clear(); break; }
                    var desc = adapter.GetDesc1();
                    var luid = ((ulong)unchecked((uint)desc.AdapterLuid.HighPart) << 32) | desc.AdapterLuid.LowPart;
                    var description = desc.Description.ToString().Trim();
                    result[luid] = new(description.Length == 0 ? null : description, (desc.Flags & DXGI_ADAPTER_FLAG.DXGI_ADAPTER_FLAG_SOFTWARE) != 0,
                        DateTime.UtcNow, description.Length == 0 ? new("notCollected", "DXGI适配器描述为空，保留LUID身份。") : null);
                }
                catch (COMException exception) { error = Failure(exception); }
                finally { if (adapter is not null) Marshal.ReleaseComObject(adapter); }
            }
        }
        catch (Exception exception) when (exception is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException)
        { error = Failure(exception); }
        finally { if (factory is not null) Marshal.ReleaseComObject(factory); }
        _adapters = result;
        _capturedAtUtc = DateTime.UtcNow;
        _enumerationError = error;
    }

    // PDH names encode HighPart then LowPart. Signed HighPart is compared as its exact UInt32 bits.
    private static bool TryLuid(string name, out ulong value)
    {
        value = 0;
        var parts = name.Split('_');
        if (parts.Length != 3 || parts[0] != "luid" || !parts[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase) || !parts[2].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            || !uint.TryParse(parts[1].AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var high)
            || !uint.TryParse(parts[2].AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var low)) return false;
        value = ((ulong)high << 32) | low;
        return true;
    }

    private static Unavailable Failure(Exception exception) => new(exception is DllNotFoundException or EntryPointNotFoundException ? "notSupported" : "error",
        $"读取DXGI适配器身份失败（HRESULT 0x{exception.HResult:X8}）；GPU计数仍按原始LUID采集。", exception.HResult);
}
