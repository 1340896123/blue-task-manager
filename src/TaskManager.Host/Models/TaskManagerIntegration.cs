namespace TaskManager.Host.Models;

public sealed record TaskManagerIntegrationState(string Status, bool? Enabled, bool OwnsDebugger, bool CanRead, bool? CanWrite, bool CanEnable, bool CanDisable,
    bool ExecutableAvailable, string Scope, string ExecutablePath, string RegistrationCommand, string? CurrentDebugger, uint? UseFilter, string Revision, DateTime CheckedAtUtc,
    string? ConflictReason, Unavailable? ReadError, Unavailable? WriteError);
public sealed record TaskManagerIntegrationRequest(bool Enabled, string ExpectedRevision);
public sealed record TaskManagerIntegrationResult(TaskManagerIntegrationState State, bool Changed, string Message);
