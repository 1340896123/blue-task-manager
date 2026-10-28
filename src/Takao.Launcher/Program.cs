using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Takao.Launcher;

internal static class Program
{
    private const string EntryFlag = "--task-manager-entry";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            ValidateEntry(args);
            var directory = Path.GetFullPath(AppContext.BaseDirectory);
            var host = Path.Combine(directory, "Takao.Host.exe");
            if (!File.Exists(host)) throw new Win32Exception(2, "同目录 Takao.Host.exe 不存在，请修复完整程序目录。");
            Elevate(host, directory);
            return 0;
        }
        catch (Exception error) when (error is Win32Exception or ArgumentException or IOException or UnauthorizedAccessException or COMException)
        {
            var code = error is Win32Exception native ? native.NativeErrorCode : error is ArgumentException ? 87 : error.HResult & 0xffff;
            var message = code == 1223 ? "已取消管理员授权，任务管理器没有启动。" : "无法启动Takao：" + error.Message;
            Console.Error.WriteLine(message + $" Windows 错误：{code}");
            if (code is not (1223 or 87)) PInvoke.MessageBox(default, message + $"\nWindows 错误：{code}", "Takao", MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONERROR);
            return code == 0 ? 1 : code;
        }
    }

    private static void ValidateEntry(string[] args)
    {
        if (args.Length is < 1 or > 34 || args[0] != EntryFlag)
            throw new ArgumentException("此入口只接受固定的任务管理器启动标记。");
        if (args.Length == 1) return;
        if (!IsSystemTaskManager(args[1])) throw new ArgumentException("只接受 Windows 系统目录中的 taskmgr.exe 作为原始目标。");
        // Original taskmgr switches have no Host meaning. Never forward them to
        // configuration, maintenance, executable selection or the elevated command.
        foreach (var argument in args[2..])
            if (argument.Length is < 2 or > 128 || argument[0] != '/' || argument.AsSpan(1).ContainsAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789:=-"))
                throw new ArgumentException("原始任务管理器参数无效；不接受主机配置或其他命令。");
    }

    private static bool IsSystemTaskManager(string path)
    {
        if (path.Equals("taskmgr.exe", StringComparison.OrdinalIgnoreCase)) return true;
        if (!Path.IsPathFullyQualified(path)) return false;
        Span<char> system = stackalloc char[32768];
        Span<char> windows = stackalloc char[32768];
        var systemLength = PInvoke.GetSystemDirectory(system);
        var windowsLength = PInvoke.GetWindowsDirectory(windows);
        if (systemLength == 0 || systemLength >= system.Length || windowsLength == 0 || windowsLength >= windows.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法确认 Windows 系统目录。");
        var full = Path.GetFullPath(path);
        return string.Equals(full, Path.Combine(new string(system[..(int)systemLength]), "taskmgr.exe"), StringComparison.OrdinalIgnoreCase)
            || string.Equals(full, Path.Combine(new string(windows[..(int)windowsLength]), "SysWOW64", "taskmgr.exe"), StringComparison.OrdinalIgnoreCase);
    }

    private static unsafe void Elevate(string host, string directory)
    {
        var initialized = PInvoke.CoInitializeEx(null, COINIT.COINIT_APARTMENTTHREADED | COINIT.COINIT_DISABLE_OLE1DDE);
        Marshal.ThrowExceptionForHR(initialized.Value);
        try
        {
            fixed (char* file = host, workingDirectory = directory, verb = "runas", parameters = EntryFlag)
            {
                var info = new SHELLEXECUTEINFOW
                {
                    cbSize = (uint)sizeof(SHELLEXECUTEINFOW),
                    fMask = PInvoke.SEE_MASK_NOCLOSEPROCESS | PInvoke.SEE_MASK_NOASYNC | PInvoke.SEE_MASK_FLAG_NO_UI,
                    lpFile = new PCWSTR(file), lpDirectory = new PCWSTR(workingDirectory),
                    lpVerb = new PCWSTR(verb), lpParameters = new PCWSTR(parameters), nShow = 1
                };
                try
                {
                    if (!PInvoke.ShellExecuteEx(ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                finally { if (!info.hProcess.IsNull) PInvoke.CloseHandle(info.hProcess); }
            }
        }
        finally { PInvoke.CoUninitialize(); }
    }
}
