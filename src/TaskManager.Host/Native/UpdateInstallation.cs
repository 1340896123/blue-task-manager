using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using TaskManager.Host.Services;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Registry;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Shell;

namespace TaskManager.Host.Native;

internal sealed record UpdateInstallation(bool Supported, string Directory, string? Reason)
{
    internal const string RegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\BlueDomainTaskManager";
    internal const string ProductId = "BlueDomainTaskManager.Admin.x64";
    internal static UpdateInstallation Read()
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        try
        {
            ProtectedUpdateStorage.RejectLinks(directory);
            var registered = ReadRegistryText("InstallLocation");
            if (registered is null || !Path.IsPathFullyQualified(registered) || !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(registered)), directory, StringComparison.OrdinalIgnoreCase))
                return new(false, directory, "此目录是便携版本；自动更新仅支持当前正式安装目录。");
            var marker = Path.Combine(directory, ".bluedomain-install.ini");
            if (!File.Exists(marker) || new FileInfo(marker).Length > 16384) return new(false, directory, "安装产品标记缺失或无效，请修复安装后更新。");
            ProtectedUpdateStorage.RejectLinks(marker);
            if (ReadMarker(marker, "Id") != ProductId || !string.Equals(ReadMarker(marker, "Directory"), directory, StringComparison.OrdinalIgnoreCase))
                return new(false, directory, "安装目录与产品标记不匹配，无法自动更新。");
            if (!File.Exists(Path.Combine(directory, "TaskManager.Host.exe"))) return new(false, directory, "安装主程序缺失。");
            return new(true, directory, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ApiException or ArgumentException)
        { return new(false, directory, "无法确认正式安装身份：" + error.Message); }
    }
    internal void RequireCurrent()
    {
        var actual = Read();
        if (!actual.Supported || !string.Equals(actual.Directory, Directory, StringComparison.OrdinalIgnoreCase)) throw new ApiException(409, "updateInstallationChanged", actual.Reason ?? "安装目录身份已变化。");
    }
    internal static string? ReadRegistryText(string requested)
    {
        using var root = new SafeRegistryHandle((nint)HKEY.HKEY_LOCAL_MACHINE, false);
        var result = PInvoke.RegOpenKeyEx(root, RegistryPath, 0, REG_SAM_FLAGS.KEY_QUERY_VALUE | REG_SAM_FLAGS.KEY_WOW64_64KEY, out var key);
        using (key)
        {
            if (result == WIN32_ERROR.ERROR_FILE_NOT_FOUND) return null;
            if (result != WIN32_ERROR.ERROR_SUCCESS) throw new ApiException(503, "updateInstallationUnavailable", NativeSupport.Error((int)result, "读取安装记录失败").Message, (int)result);
            var name = new char[16384]; var bytes = new byte[65536];
            for (uint index = 0; index < 128; index++)
            {
                uint nameLength = (uint)name.Length, length = (uint)bytes.Length;
                result = PInvoke.RegEnumValue(key, index, name, ref nameLength, out var type, bytes, ref length);
                if (result == WIN32_ERROR.ERROR_NO_MORE_ITEMS) return null;
                if (result != WIN32_ERROR.ERROR_SUCCESS) throw new ApiException(503, "updateInstallationUnavailable", "安装记录读取失败。", (int)result);
                if (!new string(name, 0, (int)nameLength).Equals(requested, StringComparison.OrdinalIgnoreCase)) continue;
                if (type != (uint)REG_VALUE_TYPE.REG_SZ || length > 65536 || (length & 1) != 0) throw new ApiException(503, "updateInstallationUnavailable", "安装记录的文本类型无效。");
                var text = new UnicodeEncoding(false, false, true).GetString(bytes, 0, (int)length).TrimEnd('\0');
                if (text.Contains('\0')) throw new ApiException(503, "updateInstallationUnavailable", "安装记录包含无效字符。");
                return text;
            }
            throw new ApiException(503, "updateInstallationUnavailable", "安装记录数量超过读取上限。");
        }
    }
    private static string ReadMarker(string file, string key)
    {
        Span<char> buffer = stackalloc char[32768];
        var length = PInvoke.GetPrivateProfileString("Product", key, "", buffer, file);
        if (length >= buffer.Length - 1) throw new ApiException(503, "updateInstallationUnavailable", "安装标记超过读取上限。");
        return new string(buffer[..(int)length]);
    }
    internal bool OtherHostRunning()
    {
        var expected = Path.Combine(Directory, "TaskManager.Host.exe");
        foreach (var process in Win32Collector.EnumerateBaseProcesses().Where(row => row.Pid != Environment.ProcessId && row.Name.Equals("TaskManager.Host.exe", StringComparison.OrdinalIgnoreCase)))
        {
            using var handle = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, process.Pid);
            if (handle.IsInvalid) throw new ApiException(409, "updateOtherInstanceUnknown", "存在无法确认路径的任务管理器实例，请关闭其他实例后重试。");
            var path = new char[32768]; uint size = (uint)path.Length;
            if (!PInvoke.QueryFullProcessImageName(handle, 0, path, ref size)) throw new ApiException(409, "updateOtherInstanceUnknown", "无法确认其他任务管理器实例的路径。");
            if (string.Equals(new string(path, 0, (int)size), expected, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
    // Contract with the standard NSIS installer: /S /TASKMANAGERUPDATE=<32 lowercase hex>.
    // Fixed HKLM product values LastUpdateAttemptId/Result/Error correlate the result.
    internal static Task LaunchInstallerAsync(string installer, string attemptId)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { LaunchInstallerOnSta(installer, attemptId); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "TaskManager update installer" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private static unsafe void LaunchInstallerOnSta(string installer, string attemptId)
    {
        var initialized = PInvoke.CoInitializeEx(null, COINIT.COINIT_APARTMENTTHREADED | COINIT.COINIT_DISABLE_OLE1DDE);
        Marshal.ThrowExceptionForHR(initialized.Value);
        try
        {
            var parameters = "/S /TASKMANAGERUPDATE=" + attemptId;
            var directory = Path.GetDirectoryName(installer)!;
            fixed (char* file = installer, working = directory, arguments = parameters, verb = "open")
            {
                var info = new SHELLEXECUTEINFOW { cbSize = (uint)sizeof(SHELLEXECUTEINFOW), fMask = PInvoke.SEE_MASK_NOCLOSEPROCESS | PInvoke.SEE_MASK_NOASYNC | PInvoke.SEE_MASK_FLAG_NO_UI,
                    lpFile = new PCWSTR(file), lpDirectory = new PCWSTR(working), lpVerb = new PCWSTR(verb), lpParameters = new PCWSTR(arguments), nShow = 1 };
                try { if (!PInvoke.ShellExecuteEx(ref info)) throw new Win32Exception(Marshal.GetLastPInvokeError()); }
                finally { if (!info.hProcess.IsNull) PInvoke.CloseHandle(info.hProcess); }
            }
        }
        finally { PInvoke.CoUninitialize(); }
    }
}
