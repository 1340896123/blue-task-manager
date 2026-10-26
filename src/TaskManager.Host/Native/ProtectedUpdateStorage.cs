using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using TaskManager.Host.Services;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.Storage.FileSystem;

namespace TaskManager.Host.Native;

// Every executable and trust record is created with an administrator/SYSTEM-only
// owner and DACL atomically. A user-writable preferences directory is never used.
internal sealed class ProtectedUpdateStorage
{
    internal string DirectoryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BlueDomainTaskManager", "Updates");
    internal string InstallerPath => Path.Combine(DirectoryPath, "installer.exe");
    internal string PartPath => Path.Combine(DirectoryPath, "installer.part");
    private string PendingPath => Path.Combine(DirectoryPath, "pending.json");
    private const string Descriptor = "O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)";
    private static readonly JsonSerializerSettings Json = new() { ContractResolver = new CamelCasePropertyNamesContractResolver(), TypeNameHandling = TypeNameHandling.None, MissingMemberHandling = MissingMemberHandling.Error, MaxDepth = 8 };
    internal bool Exists => Directory.Exists(DirectoryPath);
    internal void Ensure()
    {
        var common = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        RejectLinks(common);
        CreateProtectedDirectory(Path.Combine(common, "BlueDomainTaskManager"));
        CreateProtectedDirectory(DirectoryPath);
        Validate();
    }
    internal void Validate()
    {
        RejectLinks(DirectoryPath);
        ValidateSecurity(Path.GetDirectoryName(DirectoryPath)!, true);
        ValidateSecurity(DirectoryPath, true);
    }
    internal PendingUpdate? Read()
    {
        if (!Exists) return null;
        Validate();
        if (!File.Exists(PendingPath)) return null;
        ValidateFile(PendingPath);
        if (new FileInfo(PendingPath).Length > 32768) throw Invalid("更新安排文件超过读取上限。");
        var pending = JsonConvert.DeserializeObject<PendingUpdate>(File.ReadAllText(PendingPath, Encoding.UTF8), Json) ?? throw Invalid("更新安排为空。");
        ValidatePending(pending);
        return pending;
    }
    internal void Write(PendingUpdate pending)
    {
        ValidatePending(pending); Ensure();
        var temporary = Path.Combine(DirectoryPath, "pending-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(pending, Formatting.Indented, Json));
            if (bytes.Length > 32768) throw Invalid("更新安排超过保存上限。");
            using (var stream = CreateProtectedFile(temporary)) { stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(PendingPath)) ValidateFile(PendingPath);
            File.Move(temporary, PendingPath, true);
        }
        finally { DeleteExact(temporary); }
    }
    internal FileStream CreatePart()
    {
        Ensure(); DeleteExact(PartPath); return CreateProtectedFile(PartPath);
    }
    internal IDisposable AcquireLease()
    {
        // CreateFile with no sharing never waits for another holder. Sharing
        // violations become 409 immediately; callers must not spin or wait.
        Ensure();
        var path = Path.Combine(DirectoryPath, "operation.lock");
        if (File.Exists(path)) ValidateFile(path);
        var lease = CreateProtectedFile(path, FILE_CREATION_DISPOSITION.OPEN_ALWAYS, true);
        try { ValidateFile(path); return lease; } catch { lease.Dispose(); throw; }
    }
    internal void CommitPart()
    {
        ValidateFile(PartPath);
        if (File.Exists(InstallerPath)) ValidateFile(InstallerPath);
        File.Move(PartPath, InstallerPath, true);
    }
    internal void DeletePart() => DeleteExact(PartPath);
    internal void ClearPending(bool removeInstaller)
    {
        if (!Exists) return;
        Validate(); DeleteExact(PendingPath);
        if (removeInstaller) { try { DeleteExact(InstallerPath); } catch (IOException) { /* NSIS may still hold the verified EXE until it exits; the next download replaces this exact file. */ } }
    }
    internal void ValidateInstaller() { Validate(); ValidateFile(InstallerPath); }
    private void DeleteExact(string file)
    {
        if (!Path.GetFullPath(file).StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw Invalid("更新清理路径无效。");
        if (!File.Exists(file)) return;
        Validate(); ValidateFile(file); File.Delete(file);
    }
    private static unsafe void CreateProtectedDirectory(string path)
    {
        if (!PInvoke.ConvertStringSecurityDescriptorToSecurityDescriptor(Descriptor, 1, out var descriptor)) throw NativeError("创建更新目录安全描述符失败");
        try
        {
            var attributes = new SECURITY_ATTRIBUTES { nLength = (uint)sizeof(SECURITY_ATTRIBUTES), lpSecurityDescriptor = descriptor.Value };
            if (!PInvoke.CreateDirectory(path, attributes) && Marshal.GetLastPInvokeError() != 183) throw NativeError("创建受保护更新目录失败");
        }
        finally { PInvoke.LocalFree(new HLOCAL((nint)descriptor.Value)); }
        RejectLinks(path); ValidateSecurity(path, true);
    }
    private static unsafe FileStream CreateProtectedFile(string path, FILE_CREATION_DISPOSITION disposition = FILE_CREATION_DISPOSITION.CREATE_NEW, bool readWrite = false)
    {
        if (!PInvoke.ConvertStringSecurityDescriptorToSecurityDescriptor(Descriptor, 1, out var descriptor)) throw NativeError("创建更新文件安全描述符失败");
        try
        {
            var attributes = new SECURITY_ATTRIBUTES { nLength = (uint)sizeof(SECURITY_ATTRIBUTES), lpSecurityDescriptor = descriptor.Value };
            var handle = PInvoke.CreateFile(path, readWrite ? 0xc0000000u : 0x40000000u, 0, attributes, disposition,
                FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL | FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_WRITE_THROUGH, null);
            if (handle.IsInvalid)
            {
                var code = Marshal.GetLastPInvokeError(); handle.Dispose();
                if (code is 32 or 33) throw new ApiException(409, "updateBusy", "另一个应用实例正在处理更新，请稍后重试。", code);
                throw new ApiException(code == 5 ? 403 : 503, "updateStorageUnavailable", NativeSupport.Error(code, "创建受保护更新文件失败").Message, code);
            }
            return new FileStream(handle, readWrite ? FileAccess.ReadWrite : FileAccess.Write, 65536, false);
        }
        finally { PInvoke.LocalFree(new HLOCAL((nint)descriptor.Value)); }
    }
    private static void ValidateFile(string path)
    {
        RejectLinks(path);
        if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) throw Invalid("更新文件不能是目录。");
        ValidateSecurity(path, false);
    }
    internal static void RejectLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw Invalid("更新路径包含链接或目录联接，无法安全使用。");
            var parent = Path.GetDirectoryName(current); if (parent == current) break; current = parent!;
        }
    }
    private static unsafe void ValidateSecurity(string path, bool directory)
    {
        const uint ownerAndDacl = 0x00000001 | 0x00000004;
        PInvoke.GetFileSecurity(path, ownerAndDacl, default, 0, out var needed);
        if (needed is 0 or > 65536) throw NativeError("无法核验更新路径权限");
        var bytes = new byte[needed];
        fixed (byte* pointer = bytes)
            if (!PInvoke.GetFileSecurity(path, ownerAndDacl, new PSECURITY_DESCRIPTOR(pointer), needed, out _)) throw NativeError("读取更新路径权限失败");
        var security = new RawSecurityDescriptor(bytes, 0);
        if (security.Owner?.Value is not ("S-1-5-18" or "S-1-5-32-544") || security.DiscretionaryAcl is null
            || directory && !security.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected)) throw Invalid("更新目录所有者或保护权限不可信。");
        var administrators = false; var system = false;
        foreach (var entry in security.DiscretionaryAcl)
        {
            if (entry is not CommonAce ace || ace.IsCallback || ace.AceQualifier != AceQualifier.AccessAllowed || ace.AccessMask != 0x1f01ff
                || ace.SecurityIdentifier.Value is not ("S-1-5-18" or "S-1-5-32-544")) throw Invalid("更新目录允许了其他账户写入，不能执行其中的安装包。");
            administrators |= ace.SecurityIdentifier.Value == "S-1-5-32-544"; system |= ace.SecurityIdentifier.Value == "S-1-5-18";
        }
        if (!administrators || !system) throw Invalid("更新目录缺少管理员或SYSTEM的完整权限。");
    }
    private static void ValidatePending(PendingUpdate value)
    {
        var item = value.Candidate;
        if (value.Schema != 1 || item is null || item.SourceCommit is null || item.Sha256 is null || item.AssetName is null || value.InstallDirectory is null
            || !UpdateReleaseIdentity.TryTag(item.Tag, out var version) || !UpdateReleaseIdentity.CommitPattern.IsMatch(item.SourceCommit)
            || item.ReleaseId <= 0 || item.AssetId <= 0 || item.Size is <= 0 or > 1024L * 1024 * 1024 || !System.Text.RegularExpressions.Regex.IsMatch(item.Sha256, "\\A[0-9a-f]{64}\\z")
            || item.AssetName != $"BlueDomainTaskManager-v{version!.Major}.{version.Minor}.{version.Patch}-win-x64-setup.exe"
            || !GitHubUpdateSource.OfficialAssetUrl(item.DownloadUrl, item.Tag, item.AssetName) || !GitHubUpdateSource.OfficialAssetUrl(item.ChecksumsUrl, item.Tag, "SHA256SUMS")
            || item.ReleaseUrl != "https://github.com/" + GitHubUpdateSource.Repository + "/releases/tag/" + Uri.EscapeDataString(item.Tag)
            || value.InstallDirectory.Length > 32767 || !Path.IsPathFullyQualified(value.InstallDirectory) || value.Attempts is < 0 or > 1
            || value.Mode is not ("ready" or "scheduled" or "installing" or "error") || value.AttemptId is not null && !System.Text.RegularExpressions.Regex.IsMatch(value.AttemptId, "\\A[0-9a-f]{32}\\z")
            || value.FromCommit is not null && !UpdateReleaseIdentity.CommitPattern.IsMatch(value.FromCommit) || value.LastError?.Length > 1024)
            throw Invalid("保存的更新安排字段无效。");
    }
    private static ApiException Invalid(string message) => new(503, "updateStorageUntrusted", message);
    private static ApiException NativeError(string message) { var error = NativeSupport.LastError(message); return new(error.NativeErrorCode == 5 ? 403 : 503, "updateStorageUnavailable", error.Message, error.NativeErrorCode); }
}

internal sealed record PendingUpdate(int Schema, UpdateCandidate Candidate, string InstallDirectory, string? FromCommit, string Mode = "ready", int Attempts = 0, string? AttemptId = null, string? LastError = null);
