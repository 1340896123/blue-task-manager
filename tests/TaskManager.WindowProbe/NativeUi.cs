using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

// Test-only independent Windows inspection. Never accepts executable names for
// termination and validates the process creation identity before any UI action.
internal static class NativeUi
{
    internal static unsafe string Title(HWND window)
    {
        Span<char> buffer = stackalloc char[2048];
        var size = PInvoke.GetWindowText(window, buffer);
        return new string(buffer[..size]);
    }
    internal static unsafe string ClassName(HWND window)
    {
        Span<char> buffer = stackalloc char[256];
        var size = PInvoke.GetClassName(window, buffer);
        return new string(buffer[..size]);
    }
    private static ulong Creation(uint pid)
    {
        using var handle = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle.IsInvalid || !PInvoke.GetProcessTimes(handle, out var time, out _, out _, out _)) throw new InvalidOperationException("Window process unavailable.");
        return ((ulong)(uint)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;
    }
    internal static unsafe bool TryRun(string[] args)
    {
        if (args[0] == "windows")
        {
            var windows = new List<object>();
            PInvoke.EnumWindows((window, _) =>
            {
                if (!PInvoke.IsWindowVisible(window)) return true;
                var title = Title(window); var cls = ClassName(window);
                // Only inspect known app/dialog/Shell surfaces selected by caller.
                if (args[1].StartsWith("class:") ? cls != args[1][6..] : !title.Contains(args[1], StringComparison.OrdinalIgnoreCase)) return true;
                PInvoke.GetWindowThreadProcessId(window, out var pid);
                try { windows.Add(new { hwnd = ((nint)window).ToString(), pid, fileTime = Creation(pid).ToString(), title, className = cls }); } catch { }
                return true;
            }, default);
            Console.WriteLine(JsonConvert.SerializeObject(windows)); return true;
        }
        if (args[0] != "native-ui") return false;
        var target = new HWND((nint)long.Parse(args[1])); var expectedPid = uint.Parse(args[2]); var expectedTime = ulong.Parse(args[3]);
        PInvoke.GetWindowThreadProcessId(target, out var actualPid);
        if (actualPid != expectedPid || Creation(actualPid) != expectedTime) throw new InvalidOperationException("Native window identity changed.");
        var command = args[4];
        if (command == "foreground")
        {
            var currentThread = PInvoke.GetCurrentThreadId(); var foreground = PInvoke.GetForegroundWindow(); var foregroundThread = PInvoke.GetWindowThreadProcessId(foreground, out _);
            var attached = currentThread != foregroundThread && PInvoke.AttachThreadInput(currentThread, foregroundThread, true);
            try { PInvoke.ShowWindow(target, SHOW_WINDOW_CMD.SW_RESTORE); PInvoke.BringWindowToTop(target); PInvoke.SetForegroundWindow(target); }
            finally { if (attached) PInvoke.AttachThreadInput(currentThread, foregroundThread, false); }
            Console.WriteLine(JsonConvert.SerializeObject(new { applied = PInvoke.GetForegroundWindow() == target })); return true;
        }
        if (command is "close" or "cancel")
        {
            PInvoke.PostMessage(target, command == "close" ? 0x0010u : 0x0111u, command == "cancel" ? new WPARAM(2) : default, default);
            Console.WriteLine(JsonConvert.SerializeObject(new { submitted = true })); return true;
        }
        if (command == "choose")
        {
            var edit = new HWND((nint)long.Parse(args[5]));
            PInvoke.GetWindowThreadProcessId(edit, out var editPid);
            if (editPid != expectedPid || !PInvoke.IsChild(target, edit) || ClassName(edit) != "Edit") throw new InvalidOperationException("Expected observed native Edit control.");
            unsafe { fixed (char* text = args[6]) PInvoke.SendMessage(edit, 0x000C, default, new LPARAM((nint)text)); }
            PInvoke.PostMessage(target, 0x0111, new WPARAM(1), default);
            Console.WriteLine(JsonConvert.SerializeObject(new { submitted = true })); return true;
        }
        if (command == "capture")
        {
            if (!PInvoke.GetWindowRect(target, out var rect)) throw new InvalidOperationException("Window bounds unavailable.");
            using var bitmap = new Bitmap(rect.right - rect.left, rect.bottom - rect.top);
            using var graphics = Graphics.FromImage(bitmap); var hdc = graphics.GetHdc();
            try { if (!PInvoke.PrintWindow(target, new Windows.Win32.Graphics.Gdi.HDC(hdc), (Windows.Win32.Storage.Xps.PRINT_WINDOW_FLAGS)2)) throw new InvalidOperationException("PrintWindow failed."); }
            finally { graphics.ReleaseHdc(hdc); }
            bitmap.Save(args[5], ImageFormat.Png); Console.WriteLine(JsonConvert.SerializeObject(new { captured = true })); return true;
        }
        var controls = new List<object>();
        PInvoke.EnumChildWindows(target, (child, _) => { controls.Add(new { hwnd = ((nint)child).ToString(), id = PInvoke.GetDlgCtrlID(child), className = ClassName(child), title = Title(child) }); return controls.Count < 1000; }, default);
        var elements = new List<object>();
        var initialized = PInvoke.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED);
        initialized.ThrowOnFailure();
        PInvoke.CoCreateInstance(new Guid("FF48DBA4-60EF-4201-AA87-54103EEF594E"), null, CLSCTX.CLSCTX_INPROC_SERVER, out IUIAutomation automation).ThrowOnFailure();
        IUIAutomationElement? root = null; IUIAutomationCondition? condition = null; IUIAutomationElementArray? array = null;
        try
        {
            root = automation.ElementFromHandle(target); condition = automation.CreateTrueCondition(); array = root.FindAll(TreeScope.TreeScope_Subtree, condition);
            for (var i = 0; i < Math.Min(array.Length, 1000); i++)
            {
                var element = array.GetElement(i);
                try
                {
                    string? value = null; bool? selected = null;
                    try { var pattern = (IUIAutomationValuePattern)element.GetCurrentPattern((UIA_PATTERN_ID)10002); try { value = pattern.CurrentValue.ToString(); } finally { Marshal.ReleaseComObject(pattern); } } catch { }
                    try { var pattern = (IUIAutomationSelectionItemPattern)element.GetCurrentPattern((UIA_PATTERN_ID)10010); try { selected = pattern.CurrentIsSelected; } finally { Marshal.ReleaseComObject(pattern); } } catch { }
                    elements.Add(new { name = element.CurrentName.ToString(), id = element.CurrentAutomationId.ToString(), className = element.CurrentClassName.ToString(), controlType = (int)element.CurrentControlType, value, selected });
                }
                catch { }
                finally { Marshal.ReleaseComObject(element); }
            }
        }
        finally
        {
            if (array is not null) Marshal.ReleaseComObject(array); if (condition is not null) Marshal.ReleaseComObject(condition); if (root is not null) Marshal.ReleaseComObject(root); Marshal.ReleaseComObject(automation); PInvoke.CoUninitialize();
        }
        Console.WriteLine(JsonConvert.SerializeObject(new { title = Title(target), className = ClassName(target), controls, elements })); return true;
    }
}
