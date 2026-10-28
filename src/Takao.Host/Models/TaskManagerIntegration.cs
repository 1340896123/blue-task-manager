namespace Takao.Host.Models;

public sealed record TaskManagerIntegrationState(string Status, bool? Enabled, bool OwnsDebugger, bool CanRead, bool? CanWrite, bool CanEnable, bool CanDisable,
    bool ExecutableAvailable, string Scope, string ExecutablePath, string RegistrationCommand, string? CurrentDebugger, uint? UseFilter, string Revision, DateTime CheckedAtUtc,
    string? ConflictReason, Unavailable? ReadError, Unavailable? WriteError, bool MigrationRequired, bool CanRepair, bool CanTakeOver);
public sealed record TaskManagerIntegrationRequest(bool Enabled, string ExpectedRevision, bool Repair = false, bool TakeOver = false);
public sealed record TaskManagerIntegrationResult(TaskManagerIntegrationState State, bool Changed, string Message);
