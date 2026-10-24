using Newtonsoft.Json;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Windows.Win32.System.Threading;
using Windows.Win32.NetworkManagement.IpHelper;
using Windows.Win32.System.ProcessStatus;
using Windows.Win32.Graphics.Dxgi;

// JSON emitted to redirected Node/Playwright pipes must be UTF-8 on Windows too.
Console.OutputEncoding = new System.Text.UTF8Encoding(false);

if (ProductIconProbe.TryRun(args)) return;
if (ProcessLaunchProbe.TryRun(args)) return;
if (NativeUi.TryRun(args)) return;

if (args[0] == "process-elevation")
{
    using var handle = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, uint.Parse(args[1]));
    if (handle.IsInvalid || !PInvoke.OpenProcessToken(handle, Windows.Win32.Security.TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token)) throw new InvalidOperationException("Test process token unavailable.");
    using (token)
    {
        unsafe
        {
            Windows.Win32.Security.TOKEN_ELEVATION elevation = default;
            if (!PInvoke.GetTokenInformation(token, Windows.Win32.Security.TOKEN_INFORMATION_CLASS.TokenElevation, &elevation, (uint)sizeof(Windows.Win32.Security.TOKEN_ELEVATION), out var size) || size != sizeof(Windows.Win32.Security.TOKEN_ELEVATION)) throw new InvalidOperationException("Test process elevation unavailable.");
            if (!PInvoke.GetProcessTimes(handle, out var created, out _, out _, out _)) throw new InvalidOperationException("Test process creation identity unavailable.");
            var creationTime = ((ulong)(uint)created.dwHighDateTime << 32) | (uint)created.dwLowDateTime;
            Console.WriteLine(JsonConvert.SerializeObject(new { pid = uint.Parse(args[1]), elevated = elevation.TokenIsElevated != 0, creationTimeFileTime = creationTime.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        }
    }
    return;
}

if (args[0] == "gpu-identities")
{
    IDXGIFactory1? factory = null;
    try
    {
        PInvoke.CreateDXGIFactory1(out factory).ThrowOnFailure();
        if (factory is null) throw new InvalidOperationException("No DXGI factory.");
        var descriptions = new List<object>();
        for (uint i = 0; i < 256; i++)
        {
            IDXGIAdapter1? adapter = null;
            try
            {
                var status = factory.EnumAdapters1(i, out adapter);
                if (status == HRESULT.DXGI_ERROR_NOT_FOUND) break;
                status.ThrowOnFailure();
                var value = adapter.GetDesc1();
                descriptions.Add(new { description = value.Description.ToString(), highPart = value.AdapterLuid.HighPart, lowPart = value.AdapterLuid.LowPart,
                    software = (value.Flags & DXGI_ADAPTER_FLAG.DXGI_ADAPTER_FLAG_SOFTWARE) != 0 });
            }
            finally { if (adapter is not null) System.Runtime.InteropServices.Marshal.ReleaseComObject(adapter); }
        }
        Console.WriteLine(JsonConvert.SerializeObject(descriptions));
    }
    finally { if (factory is not null) System.Runtime.InteropServices.Marshal.ReleaseComObject(factory); }
    return;
}

if (args[0] == "network-counters")
{
    unsafe
    {
        var status = PInvoke.GetIfTable2(out MIB_IF_TABLE2* table);
        if (status != 0) throw new InvalidOperationException("GetIfTable2 failed: " + status);
        try
        {
            var rows = new List<object>();
            for (var i = 0; i < table->NumEntries; i++)
            {
                var row = table->Table[i];
                rows.Add(new { id = row.InterfaceLuid.Value.ToString(), alias = row.Alias.ToString(), hardware = row.InterfaceAndOperStatusFlags.HardwareInterface,
                    filter = row.InterfaceAndOperStatusFlags.FilterInterface, type = row.Type, status = row.OperStatus.ToString(), description = row.Description.ToString(),
                    receivedBytes = row.InOctets.ToString(), sentBytes = row.OutOctets.ToString(), receiveLinkSpeed = row.ReceiveLinkSpeed.ToString(), transmitLinkSpeed = row.TransmitLinkSpeed.ToString() });
            }
            Console.WriteLine(JsonConvert.SerializeObject(rows));
        }
        finally { PInvoke.FreeMibTable(table); }
    }
    return;
}
if (args[0] == "process-resources")
{
    using var handle = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, uint.Parse(args[1]));
    if (handle.IsInvalid || !PInvoke.GetProcessTimes(handle, out var creation, out _, out var kernel, out var user)) throw new InvalidOperationException("Test process unavailable.");
    var actual = ((ulong)(uint)creation.dwHighDateTime << 32) | (uint)creation.dwLowDateTime;
    if (actual / 10 != ulong.Parse(args[2]) / 10) throw new InvalidOperationException("Test process identity changed.");
    unsafe
    {
        var memory = new PROCESS_MEMORY_COUNTERS_EX { cb = (uint)sizeof(PROCESS_MEMORY_COUNTERS_EX) };
        if (!PInvoke.K32GetProcessMemoryInfo(new HANDLE(handle.DangerousGetHandle()), (PROCESS_MEMORY_COUNTERS*)&memory, (uint)sizeof(PROCESS_MEMORY_COUNTERS_EX)) || !PInvoke.GetProcessHandleCount(handle, out var handles)) throw new InvalidOperationException("Resource probe failed.");
        var cpu = (((ulong)(uint)kernel.dwHighDateTime << 32) | (uint)kernel.dwLowDateTime) + (((ulong)(uint)user.dwHighDateTime << 32) | (uint)user.dwLowDateTime);
        Console.WriteLine(JsonConvert.SerializeObject(new { cpuTime100ns = cpu.ToString(), privateBytes = (ulong)memory.PrivateUsage, workingSetBytes = (ulong)memory.WorkingSetSize, handles }));
    }
    return;
}

if (args[0] == "process-state")
{
    using var handle = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, uint.Parse(args[1]));
    if (handle.IsInvalid || !PInvoke.GetProcessTimes(handle, out var creation, out _, out _, out _)) throw new InvalidOperationException("Test process unavailable.");
    var actual = ((ulong)(uint)creation.dwHighDateTime << 32) | (uint)creation.dwLowDateTime;
    if (actual / 10 != ulong.Parse(args[2]) / 10) throw new InvalidOperationException("Test process identity changed.");
    unsafe
    {
        var power = new PROCESS_POWER_THROTTLING_STATE { Version = 1 };
        var available = PInvoke.GetProcessInformation(new HANDLE(handle.DangerousGetHandle()), PROCESS_INFORMATION_CLASS.ProcessPowerThrottling, &power, (uint)sizeof(PROCESS_POWER_THROTTLING_STATE));
        Console.WriteLine(JsonConvert.SerializeObject(new { priority = PInvoke.GetPriorityClass(handle), available = (bool)available, controlMask = power.ControlMask, stateMask = power.StateMask }));
    }
    return;
}
if (args[0] == "service-create-permission")
{
    using var manager = PInvoke.OpenSCManager(null, null, 2 /* SC_MANAGER_CREATE_SERVICE */);
    Console.WriteLine(JsonConvert.SerializeObject(new { available = !manager.IsInvalid, nativeErrorCode = manager.IsInvalid ? System.Runtime.InteropServices.Marshal.GetLastPInvokeError() : 0 }));
    return;
}

// Independent native UI inspection, scoped to PIDs owned by this E2E run.
var pids = args[0].Split(',').Select(entry => entry.Split(':')).ToDictionary(parts => uint.Parse(parts[0]), parts => ulong.Parse(parts[1]));
HWND target = default;
PInvoke.EnumWindows((window, unused) =>
{
    PInvoke.GetWindowThreadProcessId(window, out var pid);
    if (pids.TryGetValue(pid, out var expected) && PInvoke.IsWindowVisible(window))
    {
        using var handle = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle.IsInvalid || !PInvoke.GetProcessTimes(handle, out var created, out _, out _, out _)) return true;
        // Win32_Process datetime is precise to microseconds; allow its <1us truncation.
        var actual = ((ulong)(uint)created.dwHighDateTime << 32) | (uint)created.dwLowDateTime;
        if (actual / 10 != expected / 10) return true;
        if (NativeUi.ClassName(window) != "Chrome_WidgetWin_1" || !(NativeUi.Title(window).Contains("蓝域") && NativeUi.Title(window).Contains("任务管理器"))) return true;
        target = window; return false;
    }
    return true;
}, default);
if (target.IsNull) throw new InvalidOperationException("No visible window owned by the test host tree.");
if (args.Length > 1 && args[1] == "restore") PInvoke.ShowWindow(target, SHOW_WINDOW_CMD.SW_RESTORE);
if (args.Length > 3 && args[1] == "resize")
{
    PInvoke.ShowWindow(target, SHOW_WINDOW_CMD.SW_RESTORE);
    if (!PInvoke.GetWindowRect(target, out var beforeRect) || !PInvoke.GetClientRect(target, out var beforeClient)) throw new InvalidOperationException("Native client bounds unavailable.");
    var clientWidth = int.Parse(args[2]) + (beforeRect.right - beforeRect.left) - (beforeClient.right - beforeClient.left);
    var clientHeight = int.Parse(args[3]) + (beforeRect.bottom - beforeRect.top) - (beforeClient.bottom - beforeClient.top);
    if (!PInvoke.SetWindowPos(target, default, 0, 0, clientWidth, clientHeight, SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE))
        throw new InvalidOperationException("Native resize failed.");
}
var placement = new WINDOWPLACEMENT();
unsafe { placement.length = (uint)sizeof(WINDOWPLACEMENT); }
if (!PInvoke.GetWindowPlacement(target, ref placement) || !PInvoke.GetWindowRect(target, out var rect) || !PInvoke.GetClientRect(target, out var client)) throw new InvalidOperationException("Native placement query failed.");
Console.WriteLine(JsonConvert.SerializeObject(new { hwnd = ((nint)target).ToString(), topmost = ((long)PInvoke.GetWindowLongPtr(target, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE) & 8) != 0, state = (int)placement.showCmd, width = rect.right - rect.left, height = rect.bottom - rect.top, clientWidth = client.right - client.left, clientHeight = client.bottom - client.top }));
