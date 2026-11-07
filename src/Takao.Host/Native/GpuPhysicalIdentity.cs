using Takao.Host.Models;
using Windows.Win32.Foundation;
using Windows.Wdk.Graphics.Direct3D;
using Kmt = Windows.Wdk.PInvoke;

namespace Takao.Host.Native;

// Public Windows WDDM thunk queries only; no driver installation or state changes.
// A DXGI LUID identifies a logical adapter. Indirect display drivers may expose
// additional LUIDs for one PCI device, so model names cannot identify hardware.
internal static class GpuPhysicalIdentity
{
    internal sealed record Identity(string? DeviceId, string Kind, Unavailable? Error);
    internal static unsafe Identity Read(LUID luid, bool software)
    {
        try { return ReadCore(luid, software); }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        { return Unknown(software, "Windows图形物理身份查询接口不可用，保留独立逻辑适配器。", exception.HResult); }
    }
    private static unsafe Identity ReadCore(LUID luid, bool software)
    {
        var opened = new D3DKMT_OPENADAPTERFROMLUID { AdapterLuid = luid };
        var status = Kmt.D3DKMTOpenAdapterFromLuid(ref opened);
        if (status.Value < 0) return Unknown(software, "无法验证图形适配器物理身份，保留独立逻辑适配器。", status.Value);
        try
        {
            var count = new D3DKMT_PHYSICAL_ADAPTER_COUNT();
            var query = new D3DKMT_QUERYADAPTERINFO { hAdapter = opened.hAdapter,
                Type = KMTQUERYADAPTERINFOTYPE.KMTQAITYPE_PHYSICALADAPTERCOUNT,
                pPrivateDriverData = &count, PrivateDriverDataSize = (uint)sizeof(D3DKMT_PHYSICAL_ADAPTER_COUNT) };
            status = Kmt.D3DKMTQueryAdapterInfo(ref query);
            if (status.Value < 0 || count.Count != 1)
                return Unknown(software, "未确认单个物理设备（可能为多节点聚合适配器），保留独立逻辑身份。", status.Value < 0 ? status.Value : null);
            var buffer = new char[2048];
            uint length = (uint)buffer.Length;
            fixed (char* text = buffer)
            {
                var identity = new D3DKMT_QUERY_PHYSICAL_ADAPTER_PNP_KEY { PhysicalAdapterIndex = 0,
                    PnPKeyType = D3DKMT_PNP_KEY_TYPE.D3DKMT_PNP_KEY_HARDWARE, pDest = text, pCchDest = &length };
                query.Type = KMTQUERYADAPTERINFOTYPE.KMTQAITYPE_PHYSICALADAPTERPNPKEY;
                query.pPrivateDriverData = &identity;
                query.PrivateDriverDataSize = (uint)sizeof(D3DKMT_QUERY_PHYSICAL_ADAPTER_PNP_KEY);
                status = Kmt.D3DKMTQueryAdapterInfo(ref query);
                if (status.Value < 0 || length == 0 || length > buffer.Length)
                    return Unknown(software, "Windows未返回可验证的物理PnP身份，保留独立逻辑适配器。", status.Value < 0 ? status.Value : null);
            }
            var end = Array.IndexOf(buffer, '\0');
            if (end < 0) return Unknown(software, "物理PnP身份缺少有效终止符，保留独立逻辑适配器。");
            var deviceId = Normalize(new string(buffer, 0, end));
            if (deviceId is null) return Unknown(software, "物理PnP身份格式无法确认，保留独立逻辑适配器。");
            var kind = software ? "software" : deviceId.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase) ||
                deviceId.StartsWith("SWD\\", StringComparison.OrdinalIgnoreCase) ? "virtual" : "physical";
            return new(deviceId, kind, null);
        }
        finally { Kmt.D3DKMTCloseAdapter(new D3DKMT_CLOSEADAPTER { hAdapter = opened.hAdapter }); }
    }
    internal static string? Normalize(string key)
    {
        const string prefix = @"\REGISTRY\MACHINE\SYSTEM\";
        const string suffix = @"\Device Parameters";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
        var index = key.IndexOf(@"\Enum\", prefix.Length, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        var instance = key[(index + 6)..^suffix.Length];
        var parts = instance.Split('\\');
        return parts.Length == 3 && parts.All(part => part.Length is > 0 and < 512 && part.All(character => character is >= ' ' and < '\x7f'))
            ? instance.ToUpperInvariant() : null;
    }
    private static Identity Unknown(bool software, string reason, int? code = null) =>
        new(null, software ? "software" : "logical", new("notCollected", reason, code));
}
