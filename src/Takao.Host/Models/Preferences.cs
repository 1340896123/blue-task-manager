namespace Takao.Host.Models;

public sealed record PageFilters(bool CurrentUser = false, uint? SessionId = null, string State = "all");
public sealed record PagePreference(string Query = "", string SortKey = "name", string SortDirection = "ascending", IReadOnlyList<string>? VisibleColumns = null, PageFilters? Filters = null, double ScrollTop = 0, string? SelectedIdentity = null, string? ResourceType = null, string? ResourceId = null, IReadOnlyDictionary<string, double>? ColumnWidths = null, string? CpuGraphMode = null);
public sealed record WindowPreference(int Width = 1380, int Height = 860, double ZoomFactor = 1, bool AlwaysOnTop = false);
public sealed record UiPreferences(int Version = 1, string Density = "compact", bool SidebarCollapsed = false, string ProcessView = "flat", IReadOnlyDictionary<string, PagePreference>? Pages = null, WindowPreference? Window = null, string? SearchQuery = null, bool TaskManagerPromptDismissed = false);
public sealed record PreferencesResult(UiPreferences Preferences, Unavailable? Warning);
