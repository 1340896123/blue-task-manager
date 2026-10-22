using Newtonsoft.Json;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Windows.Win32.System.Threading;

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
        target = window; return false;
    }
    return true;
}, default);
if (target.IsNull) throw new InvalidOperationException("No visible window owned by the test host tree.");
if (args.Length > 1 && args[1] == "restore") PInvoke.ShowWindow(target, SHOW_WINDOW_CMD.SW_RESTORE);
if (args.Length > 3 && args[1] == "resize")
{
    PInvoke.ShowWindow(target, SHOW_WINDOW_CMD.SW_RESTORE);
    if (!PInvoke.SetWindowPos(target, default, 0, 0, int.Parse(args[2]), int.Parse(args[3]), SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE))
        throw new InvalidOperationException("Native resize failed.");
}
var placement = new WINDOWPLACEMENT();
unsafe { placement.length = (uint)sizeof(WINDOWPLACEMENT); }
if (!PInvoke.GetWindowPlacement(target, ref placement) || !PInvoke.GetWindowRect(target, out var rect)) throw new InvalidOperationException("Native placement query failed.");
Console.WriteLine(JsonConvert.SerializeObject(new { state = (int)placement.showCmd, width = rect.right - rect.left, height = rect.bottom - rect.top }));
