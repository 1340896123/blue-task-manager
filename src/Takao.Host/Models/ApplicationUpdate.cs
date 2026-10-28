namespace Takao.Host.Models;

public sealed record ApplicationUpdateState(string Status, string CurrentVersion, string? CurrentReleaseTag, string? CurrentSourceCommit,
    string? CandidateVersion, string? ReleaseUrl, long DownloadedBytes, long? TotalBytes, double? Progress, DateTime? CheckedAtUtc,
    bool CanCheck, bool CanDownload, bool CanCancel, bool CanInstall, bool InstallSupported, string? InstallReason, string Revision, Unavailable? Error);
public sealed record UpdateRevisionRequest(string ExpectedRevision);
public sealed record UpdateInstallRequest(string ExpectedRevision, string Mode);
