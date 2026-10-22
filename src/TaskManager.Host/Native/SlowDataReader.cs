using Microsoft.Win32;
using System.Runtime.InteropServices;
using TaskManager.Host.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.RemoteDesktop;
using Windows.Win32.System.Services;

namespace TaskManager.Host.Native;

internal sealed record SlowData(List<ServiceDto> Services, List<StartupDto> StartupItems, List<UserDto> Users, List<Unavailable> Warnings, DateTime CapturedAtUtc);

internal static class SlowDataReader
{
    internal static SlowData Read()
    {
        var warnings = new List<Unavailable>();
        return new(ReadServices(warnings), ReadStartup(warnings), ReadUsers(warnings), warnings, DateTime.UtcNow);
    }

    private static unsafe List<ServiceDto> ReadServices(List<Unavailable> warnings)
    {
        var result = new List<ServiceDto>();
        using var manager = PInvoke.OpenSCManager(null, null, PInvoke.SC_MANAGER_ENUMERATE_SERVICE);
        if (manager.IsInvalid) { warnings.Add(NativeSupport.LastError("读取服务管理器失败")); return result; }
        uint resume = 0;
        // EnumServicesStatusEx supports a bounded paged buffer; process ERROR_MORE_DATA pages too.
        var buffer = new byte[256 * 1024];
        fixed (byte* data = buffer)
        {
            do
            {
                var success = PInvoke.EnumServicesStatusEx(manager, SC_ENUM_TYPE.SC_ENUM_PROCESS_INFO, ENUM_SERVICE_TYPE.SERVICE_WIN32,
                    ENUM_SERVICE_STATE.SERVICE_STATE_ALL, data, (uint)buffer.Length, out _, out var count, ref resume, null);
                var error = success ? 0 : Marshal.GetLastPInvokeError();
                if (!success && error != 234) { warnings.Add(NativeSupport.Error(error, "枚举服务失败")); break; }
                var services = (ENUM_SERVICE_STATUS_PROCESSW*)data;
                for (var index = 0; index < count; index++)
                {
                    var service = services[index];
                    result.Add(new(service.lpServiceName.ToString(), service.lpDisplayName.ToString(), service.ServiceStatusProcess.dwCurrentState.ToString(), service.ServiceStatusProcess.dwProcessId));
                }
                if (success || count == 0) break;
            } while (resume != 0);
        }
        return result.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static unsafe List<UserDto> ReadUsers(List<Unavailable> warnings)
    {
        var result = new List<UserDto>();
        if (!PInvoke.WTSEnumerateSessions(default, 0, 1, out WTS_SESSION_INFOW* sessions, out var count))
        { warnings.Add(NativeSupport.LastError("枚举 Windows 登录会话失败")); return result; }
        try
        {
            for (var i = 0; i < count; i++)
            {
                var session = sessions[i];
                var unavailable = new Dictionary<string, Unavailable>();
                var name = QuerySessionText(session.SessionId, WTS_INFO_CLASS.WTSUserName, unavailable);
                var domain = QuerySessionText(session.SessionId, WTS_INFO_CLASS.WTSDomainName, unavailable);
                // Session 0 and the listener are real sessions, but not logged-in users.
                if (string.IsNullOrWhiteSpace(name)) continue;
                result.Add(new UserDto { SessionId = session.SessionId, UserName = string.IsNullOrWhiteSpace(domain) ? name : $"{domain}\\{name}",
                    StationName = session.pWinStationName.ToString(), State = session.State.ToString(), Unavailable = unavailable });
            }
        }
        finally { PInvoke.WTSFreeMemory(sessions); }
        return result;
    }

    private static unsafe string? QuerySessionText(uint sessionId, WTS_INFO_CLASS field, Dictionary<string, Unavailable> unavailable)
    {
        if (!PInvoke.WTSQuerySessionInformation(default, sessionId, field, out PWSTR value, out _)) { unavailable["userName"] = NativeSupport.LastError("读取会话用户失败"); return null; }
        try { return value.ToString(); }
        finally { PInvoke.WTSFreeMemory(value.Value); }
    }

    private static List<StartupDto> ReadStartup(List<Unavailable> warnings)
    {
        var result = new List<StartupDto>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var keyName in new[] { @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\RunOnce" })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(keyName, false);
                if (key is null) continue;
                var source = $"{(hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}\\{keyName} ({view})";
                foreach (var name in key.GetValueNames())
                {
                    var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (value is not string command) continue;
                    result.Add(new($"{source}\\{name}", name, command, source, null, "只读枚举启动注册项；未解析 StartupApproved/组策略的最终启用状态。"));
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            { warnings.Add(new("accessDenied", $"读取启动注册项失败：{hive}/{view}/{keyName}。")); }
        }
        foreach (var location in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
        {
            var path = Environment.GetFolderPath(location);
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) continue;
                foreach (var file in Directory.EnumerateFiles(path).Where(file => !Path.GetFileName(file).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)))
                    result.Add(new(file, Path.GetFileNameWithoutExtension(file), file, location == Environment.SpecialFolder.Startup ? "当前用户 Startup 文件夹" : "公共 Startup 文件夹", null, "只读枚举真实文件；不推断快捷方式/策略最终启用状态。"));
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            { warnings.Add(new("accessDenied", $"读取 {location} 启动文件夹失败。")); }
        }
        // On 64-bit Windows HKCU Run views can point to the same physical key; keep distinct actual values once.
        return result.DistinctBy(item => (item.Source.Replace(" (Registry32)", "").Replace(" (Registry64)", ""), item.Name, item.Command)).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
