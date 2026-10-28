using System.ComponentModel;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Takao.Host.Models;
using Takao.Host.Native;

namespace Takao.Host.Services;

public sealed class ApplicationUpdateService : BackgroundService
{
    private readonly object gate = new();
    private readonly bool desktopMode;
    private readonly UpdateReleaseIdentity identity = UpdateReleaseIdentity.Current();
    private readonly UpdateInstallation installation = UpdateInstallation.Read();
    private readonly ProtectedUpdateStorage storage = new();
    private readonly GitHubUpdateSource source = new();
    private CancellationToken stoppingToken;
    private CancellationTokenSource? operationCancellation;
    private Task? operation;
    private Func<Task>? closeDesktop;
    private string status = "checking", revision = NewRevision();
    private UpdateCandidate? candidate;
    private PendingUpdate? pending;
    private Unavailable? error;
    private string? storageReason;
    private long downloadedBytes;
    private DateTime? checkedAtUtc;
    private DateTimeOffset nextCheckUtc = DateTimeOffset.MinValue, lastManualCheckUtc = DateTimeOffset.MinValue;

    public ApplicationUpdateService(bool desktopMode)
    {
        this.desktopMode = desktopMode;
        if (!desktopMode) { status = "unavailable"; error = new("desktopOnly", "更新检查与安装仅在正式桌面启动时可用。"); return; }
        try
        {
            pending = storage.Read();
            if (pending is null) return;
            if (!installation.Supported || !pending.InstallDirectory.Equals(installation.Directory, StringComparison.OrdinalIgnoreCase))
                throw new ApiException(409, "updateInstallationChanged", "下载更新所属安装目录与当前目录不匹配。");
            candidate = pending.Candidate; downloadedBytes = candidate.Size;
            if (pending.Attempts == 1)
            {
                // Only normal startup reconciles an earlier install result. A
                // dedicated port/data-dir instance must not consume user state.
                status = "error"; error = new("updateResultPending", "上次更新结果将在正常启动时核验；不会自动重复安装。");
            }
            else status = pending.Mode == "scheduled" ? "scheduled" : "ready";
        }
        catch (Exception failure) when (Expected(failure)) { status = "error"; error = DescribeError(failure, "读取待安装更新失败"); if (IsStorageFailure(failure)) storageReason = error.Message; }
    }
    public void ConfigureDesktopShutdown(Func<Task> close) => closeDesktop = close;
    public ApplicationUpdateState Read()
    {
        lock (gate)
        {
            var busy = status is "checking" or "downloading" or "installing" || operation is { IsCompleted: false };
            var supported = desktopMode && installation.Supported && storageReason is null;
            return new(status, identity.Version.ToNormalizedString(), identity.ReleaseTag, identity.SourceCommit, candidate?.Tag, candidate?.ReleaseUrl,
                downloadedBytes, candidate?.Size, candidate is null ? null : Math.Clamp(100d * downloadedBytes / candidate.Size, 0, 100), checkedAtUtc,
                desktopMode && !busy && status is not ("ready" or "scheduled"), supported && candidate is not null && !busy && status is ("available" or "error"),
                status is ("downloading" or "scheduled") || pending is not null && status is ("ready" or "error"), supported && pending is not null && !busy && status is ("ready" or "scheduled" or "error"),
                supported, desktopMode ? storageReason ?? installation.Reason : "更新仅在正式桌面启动时可用。", revision, error);
        }
    }
    public ApplicationUpdateState BeginCheck()
    {
        lock (gate)
        {
            RequireDesktop(); RequireIdle();
            if (pending is not null) throw new ApiException(409, "updateAlreadyDownloaded", "已有下载完成的更新，请先选择安装或取消安排。");
            var now = DateTimeOffset.UtcNow;
            if (now - lastManualCheckUtc < TimeSpan.FromSeconds(60) || source.RetryAtUtc > now) throw new ApiException(429, "updateCheckCooldown", "更新检查正在冷却或受GitHub限额限制，请稍后重试。");
            lastManualCheckUtc = now; StartCheckLocked(); return Read();
        }
    }
    public ApplicationUpdateState BeginDownload(UpdateRevisionRequest request)
    {
        lock (gate)
        {
            RequireDesktop(); CheckRevision(request.ExpectedRevision); RequireIdle(); installation.RequireCurrent();
            if (!installation.Supported || candidate is null || status is not ("available" or "error")) throw new ApiException(409, "updateNotAvailable", installation.Reason ?? "没有可下载的已确认更新。");
            storage.Ensure(); storageReason = null;
            operationCancellation?.Dispose(); operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var selected = candidate; var expectedPending = pending; status = "downloading"; downloadedBytes = 0; error = null; revision = NewRevision();
            operation = Task.Run(() => DownloadAsync(selected, expectedPending, operationCancellation.Token)); return Read();
        }
    }
    public ApplicationUpdateState Cancel(UpdateRevisionRequest request)
    {
        lock (gate)
        {
            CheckRevision(request.ExpectedRevision);
            if (status == "downloading") { operationCancellation?.Cancel(); return Read(); }
            if (status == "scheduled" && pending is not null)
            {
                using var lease = storage.AcquireLease(); RequireDiskPending(pending);
                pending = pending with { Mode = "ready", Attempts = 0, AttemptId = null, LastError = null }; storage.Write(pending);
                status = "ready"; error = null; revision = NewRevision(); return Read();
            }
            if (pending is not null && status is "ready" or "error")
            {
                using var lease = storage.AcquireLease(); RequireDiskPending(pending);
                storage.ClearPending(true); pending = null; downloadedBytes = 0; error = null;
                if (candidate is not null && identity.TargetInstalled(candidate)) candidate = null;
                status = candidate is null ? "upToDate" : "available"; revision = NewRevision(); return Read();
            }
            throw new ApiException(409, "updateCannotCancel", "当前没有正在下载或安排下次启动的更新。");
        }
    }
    public async Task<ApplicationUpdateState> PrepareInstallAsync(UpdateInstallRequest request)
    {
        if (request.Mode is not ("now" or "nextStart")) throw new ApiException(400, "invalidUpdateMode", "更新方式必须是立即更新或下次启动。");
        PendingUpdate selected;
        lock (gate)
        {
            RequireDesktop(); CheckRevision(request.ExpectedRevision); RequireIdle(); installation.RequireCurrent();
            if (!installation.Supported || pending is null || candidate is null || status is not ("ready" or "scheduled" or "error")) throw new ApiException(409, "updateNotReady", installation.Reason ?? "安装包尚未完成校验。");
            selected = pending; status = "installing"; error = null; revision = NewRevision();
        }
        try
        {
            using var lease = storage.AcquireLease(); RequireDiskPending(selected);
            await VerifyInstallerAsync(selected, stoppingToken);
            if (installation.OtherHostRunning()) throw new ApiException(409, "updateOtherInstanceRunning", "当前安装目录还有其他实例，请先关闭其他实例再更新。");
            var next = selected with { Mode = request.Mode == "now" ? "installing" : "scheduled", Attempts = request.Mode == "now" ? 1 : 0,
                AttemptId = request.Mode == "now" ? Guid.NewGuid().ToString("N") : null, LastError = null };
            storage.Write(next);
            lock (gate) { pending = next; status = request.Mode == "now" ? "installing" : "scheduled"; revision = NewRevision(); return Read(); }
        }
        catch (Exception failure) when (Expected(failure))
        {
            lock (gate) { status = "error"; error = DescribeError(failure, "准备更新失败"); revision = NewRevision(); }
            throw AsApi(failure, "准备更新失败");
        }
    }
    // Called by Response.OnCompleted. Installer acceptance precedes closing this
    // exact desktop; NSIS owns bounded lock waiting and success-only restart.
    public async Task ApplyAfterResponseAsync()
    {
        PendingUpdate selected;
        lock (gate) { if (pending is not { Mode: "installing", Attempts: 1, AttemptId: not null }) return; selected = pending; }
        try
        {
            if (closeDesktop is null) throw new ApiException(503, "updateDesktopUnavailable", "桌面关闭入口不可用，请重新启动后重试。");
            using var lease = storage.AcquireLease(); RequireDiskPending(selected);
            await VerifyInstallerAsync(selected, stoppingToken);
            await UpdateInstallation.LaunchInstallerAsync(storage.InstallerPath, selected.AttemptId!);
            await closeDesktop();
        }
        catch (Exception failure) when (Expected(failure)) { RecordAttemptFailure(selected, failure); }
    }
    public async Task<bool> TryApplyScheduledAtStartupAsync()
    {
        if (desktopMode && pending is { Attempts: 1 } previous)
        {
            try
            {
                using var lease = storage.AcquireLease(); RequireDiskPending(previous);
                var attempt = UpdateInstallation.ReadRegistryText("LastUpdateAttemptId");
                var result = UpdateInstallation.ReadRegistryText("LastUpdateResult");
                if (previous.AttemptId is not null && attempt == previous.AttemptId && result == "success" && identity.TargetInstalled(previous.Candidate))
                {
                    storage.ClearPending(true);
                    lock (gate) { pending = null; candidate = null; downloadedBytes = 0; status = "upToDate"; error = null; revision = NewRevision(); }
                }
                else
                {
                    var detail = attempt == previous.AttemptId ? UpdateInstallation.ReadRegistryText("LastUpdateError") : null;
                    var message = "上次自动更新未能确认成功，请重试或重新下载安装包。" + (string.IsNullOrWhiteSpace(detail) ? "" : " " + detail[..Math.Min(detail.Length, 256)]);
                    pending = previous with { Mode = "error", LastError = message }; storage.Write(pending);
                    lock (gate) { status = "error"; error = new("updatePreviousAttemptFailed", message); revision = NewRevision(); }
                }
            }
            catch (Exception failure) when (Expected(failure)) { RecordAttemptFailure(previous, failure); }
            return false;
        }
        if (!desktopMode || pending is not { Mode: "scheduled", Attempts: 0 } selected) return false;
        try
        {
            using var lease = storage.AcquireLease(); RequireDiskPending(selected);
            installation.RequireCurrent();
            if (installation.OtherHostRunning()) throw new ApiException(409, "updateOtherInstanceRunning", "其他实例仍在使用本安装目录，本次没有自动安装。");
            await VerifyInstallerAsync(selected, CancellationToken.None);
            selected = selected with { Mode = "installing", Attempts = 1, AttemptId = Guid.NewGuid().ToString("N") }; storage.Write(selected);
            lock (gate) { pending = selected; status = "installing"; }
            await UpdateInstallation.LaunchInstallerAsync(storage.InstallerPath, selected.AttemptId!); return true;
        }
        catch (Exception failure) when (Expected(failure)) { RecordAttemptFailure(selected with { Attempts = 1 }, failure); return false; }
    }
    private void RecordAttemptFailure(PendingUpdate selected, Exception failure)
    {
        var detail = DescribeError(failure, "启动更新安装器失败");
        var failed = selected with { Mode = "error", Attempts = 1, LastError = detail.Message };
        try
        {
            using var lease = storage.AcquireLease();
            var actual = storage.Read();
            if (actual is not null && actual.Candidate == selected.Candidate && actual.AttemptId == selected.AttemptId) storage.Write(failed);
            else detail = new("updateChanged", "更新安排已被另一实例更改，请重新启动后读取实际状态。");
        }
        catch (Exception saveError) when (Expected(saveError)) { detail = DescribeError(saveError, "保存更新失败状态失败"); }
        lock (gate)
        {
            pending = failed;
            status = "error"; error = detail; revision = NewRevision();
        }
    }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        stoppingToken = token;
        if (!desktopMode) return;
        while (!token.IsCancellationRequested)
        {
            lock (gate)
                if (pending is null && (operation is null || operation.IsCompleted) && DateTimeOffset.UtcNow >= nextCheckUtc) StartCheckLocked();
            try { await Task.Delay(TimeSpan.FromSeconds(30), token); } catch (OperationCanceledException) { break; }
        }
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        operationCancellation?.Cancel(); await base.StopAsync(cancellationToken);
        var current = operation;
        if (current is not null) { try { await current.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken); } catch (OperationCanceledException) { } catch (TimeoutException) { } }
    }
    private void StartCheckLocked()
    {
        operationCancellation?.Dispose(); operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        status = "checking"; error = null; revision = NewRevision();
        operation = Task.Run(() => CheckAsync(operationCancellation.Token));
    }
    private async Task CheckAsync(CancellationToken token)
    {
        try
        {
            if (storage.Exists) storage.Validate();
            var result = await source.CheckAsync(identity, token);
            lock (gate)
            {
                candidate = result; downloadedBytes = 0; checkedAtUtc = DateTime.UtcNow; status = result is null ? "upToDate" : "available";
                error = null; storageReason = null; nextCheckUtc = DateTimeOffset.UtcNow.AddHours(6); revision = NewRevision();
            }
        }
        catch (Exception failure) when (Expected(failure))
        {
            lock (gate)
            {
                status = "error"; error = DescribeError(failure, "检查更新失败"); checkedAtUtc = DateTime.UtcNow;
                if (IsStorageFailure(failure)) storageReason = error.Message;
                nextCheckUtc = source.RetryAtUtc > DateTimeOffset.UtcNow.AddMinutes(15) ? source.RetryAtUtc.Value : DateTimeOffset.UtcNow.AddMinutes(15); revision = NewRevision();
            }
        }
    }
    private async Task DownloadAsync(UpdateCandidate selected, PendingUpdate? expectedPending, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(20));
        try
        {
            using var lease = storage.AcquireLease(); RequireDiskPending(expectedPending);
            using var response = await source.DownloadResponseAsync(new Uri(selected.DownloadUrl), timeout.Token);
            if (response.Content.Headers.ContentLength is { } declared && declared != selected.Size) throw new ApiException(503, "updateSizeMismatch", "GitHub附件长度与已确认版本不一致。");
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            using (var output = storage.CreatePart())
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[65536]; long total = 0; var lastProgress = Environment.TickCount64;
                for (var count = await input.ReadAsync(buffer, timeout.Token); count != 0; count = await input.ReadAsync(buffer, timeout.Token))
                {
                    total += count; if (total > selected.Size) throw new ApiException(503, "updateSizeMismatch", "安装包超过已确认长度，下载已取消。");
                    hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                    if (Environment.TickCount64 - lastProgress >= 1000) { lock (gate) downloadedBytes = total; lastProgress = Environment.TickCount64; }
                }
                output.Flush(true);
                if (total != selected.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(selected.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new ApiException(503, "updateIntegrityFailed", "安装包长度或SHA256校验失败，请重新下载。");
            }
            timeout.Token.ThrowIfCancellationRequested(); installation.RequireCurrent(); storage.CommitPart();
            var ready = new PendingUpdate(1, selected, installation.Directory, identity.SourceCommit); storage.Write(ready);
            lock (gate) { pending = ready; downloadedBytes = selected.Size; status = "ready"; error = null; revision = NewRevision(); }
        }
        catch (Exception failure) when (Expected(failure))
        {
            try { using var cleanupLease = storage.AcquireLease(); RequireDiskPending(expectedPending); storage.DeletePart(); }
            catch (Exception cleanup) when (Expected(cleanup)) { if (cleanup is not ApiException { Code: "updateBusy" or "updateChanged" }) failure = cleanup; }
            lock (gate)
            {
                downloadedBytes = 0; status = token.IsCancellationRequested ? "available" : "error";
                error = token.IsCancellationRequested ? null : DescribeError(failure, "后台下载更新失败"); revision = NewRevision();
            }
        }
    }
    private async Task VerifyInstallerAsync(PendingUpdate selected, CancellationToken token)
    {
        installation.RequireCurrent();
        if (!selected.InstallDirectory.Equals(installation.Directory, StringComparison.OrdinalIgnoreCase)) throw new ApiException(409, "updateInstallationChanged", "更新安排属于另一个安装目录。");
        if (identity.TargetInstalled(selected.Candidate)) throw new ApiException(409, "updateAlreadyInstalled", "当前程序已是该版本或更新版本，未运行旧安装包。");
        if (identity.ReleaseTag is null && (identity.SourceCommit is null || identity.SourceCommit != selected.FromCommit))
            throw new ApiException(409, "updateIdentityChanged", "下载后当前程序源码身份已变化，请重新检查并下载更新。");
        storage.ValidateInstaller();
        await using var input = new FileStream(storage.InstallerPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan | FileOptions.Asynchronous);
        if (input.Length != selected.Candidate.Size) throw new ApiException(409, "updateIntegrityFailed", "待安装文件长度已变化，请重新下载。");
        var digest = await SHA256.HashDataAsync(input, token);
        if (!Convert.ToHexString(digest).Equals(selected.Candidate.Sha256, StringComparison.OrdinalIgnoreCase)) throw new ApiException(409, "updateIntegrityFailed", "待安装文件校验失败，请重新下载。");
    }
    private void RequireDiskPending(PendingUpdate? expected)
    {
        if (storage.Read() != expected) throw new ApiException(409, "updateChanged", "待安装更新已被另一实例更改，请重新启动后读取实际状态。");
    }
    private void RequireDesktop() { if (!desktopMode) throw new ApiException(503, "updateDesktopOnly", "更新仅在正式桌面启动时可用。"); }
    private void RequireIdle() { if (status is "checking" or "downloading" or "installing" || operation is { IsCompleted: false }) throw new ApiException(409, "updateBusy", "更新任务仍在进行，请稍后重试。"); }
    private void CheckRevision(string? expected) { if (expected is null || expected.Length != 64) throw new ApiException(400, "updateRevisionRequired", "请先读取更新状态。"); if (expected != revision) throw new ApiException(409, "updateChanged", "更新状态已变化，请刷新后重新操作。"); }
    private static string NewRevision() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static bool Expected(Exception error) => error is ApiException or IOException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException or JsonException or ArgumentException or Win32Exception or System.Runtime.InteropServices.COMException or CryptographicException or System.Security.SecurityException;
    private static bool IsStorageFailure(Exception error) => error is ApiException api && api.Code.StartsWith("updateStorage", StringComparison.Ordinal);
    private static Unavailable DescribeError(Exception error, string action) => error is ApiException api ? new(api.Code, api.Message, api.NativeErrorCode)
        : error is Win32Exception native ? new("updateNativeError", action + "：" + native.Message, native.NativeErrorCode)
        : new("updateFailed", action + (error is OperationCanceledException ? "：操作已取消或超时。" : error is HttpRequestException ? "：网络连接不可用，请稍后重试。" : "：" + error.Message));
    private static ApiException AsApi(Exception error, string action) { var detail = DescribeError(error, action); return error as ApiException ?? new ApiException(503, detail.Code, detail.Message, detail.NativeErrorCode); }
    public override void Dispose() { operationCancellation?.Cancel(); operationCancellation?.Dispose(); source.Dispose(); base.Dispose(); }
}
