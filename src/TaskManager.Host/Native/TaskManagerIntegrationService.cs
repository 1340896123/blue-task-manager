using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;
using TaskManager.Host.Models;
using TaskManager.Host.Services;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;

namespace TaskManager.Host.Native;

// Standard Windows IFEO configuration, independently implemented from the public API.
// No renderer-provided paths, commands, hive, registry view or value names are accepted.
public sealed class TaskManagerIntegrationService
{
    internal const string ParentPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    internal const string KeyPath = ParentPath + @"\taskmgr.exe";
    internal const string EntryFlag = "--task-manager-entry";
    internal const string UninstallFlag = "--uninstall-task-manager-integration";
    private const REG_SAM_FLAGS ReadRights = REG_SAM_FLAGS.KEY_QUERY_VALUE | REG_SAM_FLAGS.KEY_ENUMERATE_SUB_KEYS | REG_SAM_FLAGS.KEY_WOW64_64KEY;
    private readonly object gate = new();
    private static readonly UnicodeEncoding Unicode = new(false, false, true);
    public static string ExecutablePath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TaskManager.Host.exe"));
    public static string LauncherPath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TaskManager.Launcher.exe"));
    public static string RegistrationCommand => $"\"{LauncherPath}\" {EntryFlag}";
    private static string LegacyRegistrationCommand => $"\"{ExecutablePath}\" {EntryFlag}";

    // Fixed local maintenance entry: never accepts a hive, path, value or command.
    // Reuse the same REG_SZ ownership, revision, write-handle re-read and readback as the UI.
    internal TaskManagerIntegrationResult RemoveOwnedForUninstall()
    {
        lock (gate)
        {
            var before = Read();
            if (!before.CanRead) throw Error(before.ReadError ?? new("error", "无法确认任务管理器入口所有权，卸载必须中止并保留程序文件。"));
            if (!before.OwnsDebugger) return new(before, false, "没有精确属于本安装目录的REG_SZ Debugger，未修改Windows入口配置。");
            return Apply(new(false, before.Revision));
        }
    }

    public TaskManagerIntegrationState Read()
    {
        using var root = Root();
        var opened = PInvoke.RegOpenKeyEx(root, KeyPath, 0, ReadRights, out var key);
        using (key)
        {
            var configuration = opened == WIN32_ERROR.ERROR_FILE_NOT_FOUND ? new Configuration([], [], null) : opened == WIN32_ERROR.ERROR_SUCCESS ? ReadConfiguration(key) : new([], [], NativeSupport.Error((int)opened, "读取任务管理器入口配置失败"));
            var permission = ProbeWrite(root, opened == WIN32_ERROR.ERROR_FILE_NOT_FOUND);
            return Describe(configuration, permission.CanWrite, permission.Error);
        }
    }

    public TaskManagerIntegrationResult Apply(TaskManagerIntegrationRequest request)
    {
        if (request.Repair && !request.Enabled) throw new ApiException(400, "integrationRepairInvalid", "修复接管必须明确请求启用新的本目录入口。");
        if (request.TakeOver && (!request.Enabled || request.Repair)) throw new ApiException(400, "integrationTakeOverInvalid", "接管现有入口必须明确请求启用，不能同时请求修复或取消接管。");
        if (request.ExpectedRevision is null || request.ExpectedRevision.Length != 64 || !request.ExpectedRevision.All(Uri.IsHexDigit)) throw new ApiException(400, "integrationRevisionRequired", "请先读取任务管理器入口的实际状态，再确认操作。");
        lock (gate)
        {
            var before = Read();
            if (!before.CanRead) throw Error(before.ReadError ?? new("error", "实际入口配置不可读取。"));
            CheckRevision(before, request.ExpectedRevision);
            if (request.Enabled)
            {
                if (before.Status == "enabled") return new(before, false, "本目录程序已接管任务管理器入口，未重复写入。");
                ValidateEnableRequest(before, request);
            }
            else if (!before.OwnsDebugger)
            {
                if (before.Status == "disabled") return new(before, false, "当前没有本程序接管值，未修改Windows配置。");
                throw new ApiException(409, "integrationConflict", "当前Debugger不精确属于本目录程序，不能取消其他工具的接管。");
            }

            using var root = Root();
            var opened = PInvoke.RegOpenKeyEx(root, KeyPath, 0, ReadRights | REG_SAM_FLAGS.KEY_SET_VALUE, out var key);
            if (opened == WIN32_ERROR.ERROR_FILE_NOT_FOUND && request.Enabled)
            {
                key.Dispose();
                opened = PInvoke.RegCreateKeyEx(root, KeyPath, default, REG_OPEN_CREATE_OPTIONS.REG_OPTION_NON_VOLATILE, ReadRights | REG_SAM_FLAGS.KEY_SET_VALUE, null, out key, out _);
            }
            using (key)
            {
                if (opened != WIN32_ERROR.ERROR_SUCCESS) throw Error(NativeSupport.Error((int)opened, "Windows拒绝写入任务管理器入口；请以管理员身份运行后重试"));
                // Re-read the complete key through this exact write handle immediately before mutation.
                var actualConfiguration = ReadConfiguration(key);
                var actual = Describe(actualConfiguration, true, null);
                if (!actual.CanRead) throw Error(actual.ReadError!);
                CheckRevision(actual, request.ExpectedRevision);
                if (request.Enabled) ValidateEnableRequest(actual, request);
                if (!request.Enabled && !actual.OwnsDebugger) throw new ApiException(409, "integrationConflict", "写入前Debugger已不属于本目录程序，未删除。");

                // A filtered child can otherwise override the root Debugger. Keep its
                // settings for diagnostics, but disable path filtering for takeover/reset.
                if (request.TakeOver || !request.Enabled) DisableFilterIfNeeded(key, actualConfiguration);
                var result = request.Enabled ? PInvoke.RegSetValueEx(key, "Debugger", REG_VALUE_TYPE.REG_SZ, Encoding.Unicode.GetBytes(RegistrationCommand + '\0')) : PInvoke.RegDeleteValue(key, "Debugger");
                CheckMutation(result, request.Enabled ? "注册任务管理器入口失败" : "恢复Windows标准任务管理器入口失败");
                var afterConfiguration = ReadConfiguration(key);
                var after = Describe(afterConfiguration, true, null);
                CheckReadback(after);
                if (request.Enabled ? after.Status != "enabled" : afterConfiguration.Values.ContainsKey("Debugger") || RequiresFilterReset(afterConfiguration))
                    throw ChangedAfterWrite();
                return new(after, true, request.Enabled ? request.Repair ? "已修复旧接管并读回确认；系统入口将通过 Windows 管理员授权启动。" : request.TakeOver ? "已接管现有任务管理器入口并读回确认；取消接管将恢复Windows标准任务管理器，不恢复此前工具。" : "已读回确认本目录程序接管任务管理器入口。" : "已读回确认恢复Windows标准任务管理器入口，不恢复此前工具。");
            }
        }
    }

    private static SafeRegistryHandle Root() => new((nint)HKEY.HKEY_LOCAL_MACHINE, false);
    private static void CheckRevision(TaskManagerIntegrationState state, string expected)
    { if (!string.Equals(state.Revision, expected, StringComparison.Ordinal)) throw new ApiException(409, "integrationChanged", "Windows任务管理器入口配置已经变化，未执行修改；请刷新后重新确认。"); }
    private static ApiException Error(Unavailable error) => new(error.NativeErrorCode == 5 ? 403 : 503, error.NativeErrorCode == 5 ? "integrationAccessDenied" : "integrationUnavailable", error.Message, error.NativeErrorCode);

    private static void ValidateEnableRequest(TaskManagerIntegrationState state, TaskManagerIntegrationRequest request)
    {
        if (state.MigrationRequired && !request.Repair && !request.TakeOver) throw new ApiException(409, "integrationRepairRequired", "旧接管需要明确确认修复，未自动迁移。");
        var requiredStatus = request.TakeOver ? "conflict" : request.Repair ? "migrationRequired" : "disabled";
        if (state.Status != requiredStatus) throw new ApiException(409, "integrationConflict", request.TakeOver ? "现有入口状态已变化，请刷新后重新选择接管或修复。" : state.ConflictReason ?? "已有其他入口配置，请明确确认接管现有入口。");
        if (!state.ExecutableAvailable) throw new ApiException(409, "integrationExecutableUnavailable", "当前目录缺少 TaskManager.Launcher.exe 或 TaskManager.Host.exe，请恢复完整程序目录后重试。");
    }

    private static bool RequiresFilterReset(Configuration configuration) => configuration.Values.TryGetValue("UseFilter", out var value)
        && (value.Type != (uint)REG_VALUE_TYPE.REG_DWORD || value.Data.Length != sizeof(uint) || System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(value.Data) != 0);

    private static void DisableFilterIfNeeded(SafeRegistryHandle key, Configuration before)
    {
        if (!RequiresFilterReset(before)) return;
        var zero = new byte[sizeof(uint)];
        CheckMutation(PInvoke.RegSetValueEx(key, "UseFilter", REG_VALUE_TYPE.REG_DWORD, zero), "关闭任务管理器入口过滤失败");

        // Before the second mutation, verify that only our filter write occurred.
        // Registry writes provide no cross-tool compare-and-swap operation.
        var expectedValues = new Dictionary<string, RegistryValue>(before.Values, StringComparer.OrdinalIgnoreCase) { ["UseFilter"] = new((uint)REG_VALUE_TYPE.REG_DWORD, zero) };
        var expected = Describe(new(expectedValues, before.Subkeys, null), true, null);
        var actual = Describe(ReadConfiguration(key), true, null);
        CheckReadback(actual);
        if (actual.Revision != expected.Revision) throw ChangedAfterWrite();
    }

    private static void CheckMutation(WIN32_ERROR result, string action)
    {
        if (result != WIN32_ERROR.ERROR_SUCCESS) throw Error(NativeSupport.Error((int)result, $"{action}；配置可能部分生效，请刷新实际状态，不要盲目重试"));
    }

    private static void CheckReadback(TaskManagerIntegrationState state)
    {
        if (!state.CanRead) throw new ApiException(503, "integrationReadbackFailed", "Windows已受理配置修改，但读回失败；配置可能部分生效，请刷新实际状态，不要盲目重试。", state.ReadError?.NativeErrorCode);
    }

    private static ApiException ChangedAfterWrite() => new(409, "integrationChangedAfterWrite", "修改后的实际配置与请求不一致；可能有其他工具同时更改，配置可能部分生效，请刷新后重新确认，不再自动写入。");

    private static (bool? CanWrite, Unavailable? Error) ProbeWrite(SafeRegistryHandle root, bool missing)
    {
        var rights = (missing ? REG_SAM_FLAGS.KEY_CREATE_SUB_KEY : REG_SAM_FLAGS.KEY_SET_VALUE) | REG_SAM_FLAGS.KEY_WOW64_64KEY;
        var result = PInvoke.RegOpenKeyEx(root, missing ? ParentPath : KeyPath, 0, rights, out var probe);
        using (probe)
        {
            if (result == WIN32_ERROR.ERROR_SUCCESS) return (true, null);
            return (result == WIN32_ERROR.ERROR_ACCESS_DENIED ? false : null, NativeSupport.Error((int)result, "当前账户无法写入计算机范围的任务管理器入口；需要管理员写权限"));
        }
    }

    private static Configuration ReadConfiguration(SafeRegistryHandle key)
    {
        var values = new Dictionary<string, RegistryValue>(StringComparer.OrdinalIgnoreCase); var subkeys = new List<string>();
        var name = new char[16384]; var bytes = new byte[65536];
        for (uint index = 0; ; index++)
        {
            uint nameLength = (uint)name.Length, length = (uint)bytes.Length;
            var result = PInvoke.RegEnumValue(key, index, name, ref nameLength, out var type, bytes, ref length);
            if (result == WIN32_ERROR.ERROR_NO_MORE_ITEMS) break;
            if (result != WIN32_ERROR.ERROR_SUCCESS) return new(values, subkeys, NativeSupport.Error((int)result, "读取IFEO值失败，未允许修改"));
            if (index >= 64 || length > bytes.Length || nameLength >= name.Length) return new(values, subkeys, new("capacity", "IFEO值超过64项/64KiB单值上限，未允许修改。"));
            values[new string(name, 0, (int)nameLength)] = new(type, bytes.AsSpan(0, (int)length).ToArray());
        }
        for (uint index = 0; ; index++)
        {
            uint length = (uint)name.Length;
            var result = PInvoke.RegEnumKeyEx(key, index, name, ref length, default);
            if (result == WIN32_ERROR.ERROR_NO_MORE_ITEMS) break;
            if (result != WIN32_ERROR.ERROR_SUCCESS) return new(values, subkeys, NativeSupport.Error((int)result, "读取IFEO过滤子键失败，未允许修改"));
            if (index >= 64 || length >= name.Length) return new(values, subkeys, new("capacity", "IFEO子键超过64项上限，未允许修改。"));
            subkeys.Add(new string(name, 0, (int)length));
        }
        return new(values, subkeys, null);
    }

    private static TaskManagerIntegrationState Describe(Configuration configuration, bool? canWrite, Unavailable? writeError)
    {
        string? debugger = null, conflict = null; uint? filter = null;
        if (configuration.Values.TryGetValue("Debugger", out var value))
        {
            if (value.Type != (uint)REG_VALUE_TYPE.REG_SZ || (value.Data.Length & 1) != 0) conflict = "现有Debugger不是普通REG_SZ文本；可明确接管并替换为本目录入口。";
            else
            {
                try { debugger = Unicode.GetString(value.Data); if (debugger.EndsWith('\0')) debugger = debugger[..^1]; if (debugger.Contains('\0')) conflict = "现有Debugger包含异常空字符；可明确接管并替换为本目录入口。"; }
                catch (DecoderFallbackException) { conflict = "现有Debugger的Unicode文本无效；可明确接管并替换为本目录入口。"; }
            }
        }
        var migrationRequired = conflict is null && debugger == LegacyRegistrationCommand && value?.Type == (uint)REG_VALUE_TYPE.REG_SZ;
        var owns = conflict is null && (debugger == RegistrationCommand || migrationRequired) && value?.Type == (uint)REG_VALUE_TYPE.REG_SZ;
        if (configuration.Values.TryGetValue("UseFilter", out var useFilter))
        {
            if (useFilter.Type != (uint)REG_VALUE_TYPE.REG_DWORD || useFilter.Data.Length != 4) conflict ??= "UseFilter不是有效DWORD；接管时将关闭入口过滤。";
            else { filter = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(useFilter.Data); if (filter != 0) conflict ??= "已启用UseFilter入口过滤；接管时将关闭过滤并使用本目录入口。"; }
        }
        // Other diagnostic values and dormant filter children do not select an
        // alternate entry when UseFilter is absent or a valid DWORD zero.
        if (!string.IsNullOrEmpty(debugger) && !owns) conflict ??= "任务管理器入口已配置为其他工具或其他目录/命令；可明确接管到本目录程序。";
        var status = configuration.Error is not null ? "unavailable" : conflict is not null ? "conflict" : migrationRequired ? "migrationRequired" : owns ? "enabled" : "disabled";
        var executableAvailable = File.Exists(ExecutablePath) && File.Exists(LauncherPath);
        var fingerprint = JsonConvert.SerializeObject(new { command = RegistrationCommand, canWrite, values = configuration.Values.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => new { name = p.Key.ToUpperInvariant(), type = p.Value.Type, data = Convert.ToBase64String(p.Value.Data) }), subkeys = configuration.Subkeys.Order(StringComparer.OrdinalIgnoreCase), error = configuration.Error });
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
        return new(status, status == "enabled" ? true : status == "disabled" ? false : null, owns, configuration.Error is null, canWrite, status == "disabled" && canWrite == true && executableAvailable, configuration.Error is null && owns && canWrite == true,
            executableAvailable, "machine", LauncherPath, RegistrationCommand, debugger, filter, revision, DateTime.UtcNow, conflict, configuration.Error, writeError, migrationRequired, status == "migrationRequired" && canWrite == true && executableAvailable, status == "conflict" && canWrite == true && executableAvailable);
    }
    private sealed record RegistryValue(uint Type, byte[] Data);
    private sealed record Configuration(Dictionary<string, RegistryValue> Values, List<string> Subkeys, Unavailable? Error);
}
