using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using TaskManager.Host.Models;

namespace TaskManager.Host.Services;

public sealed class PreferencesService
{
    private readonly object gate = new();
    private readonly string file;
    private Persisted persisted = new(1, 1000, new());
    private Unavailable? warning;
    private static readonly JsonSerializerSettings Json = new() { ContractResolver = new CamelCasePropertyNamesContractResolver(), MaxDepth = 12, MissingMemberHandling = MissingMemberHandling.Error, TypeNameHandling = TypeNameHandling.None };
    private static readonly HashSet<string> Pages = ["processes", "details", "performance", "history", "startup", "users", "services", "settings"];
    private static readonly HashSet<string> Keys = ["name", "pid", "parentPid", "sessionId", "threadCount", "handleCount", "priority", "cpuPercent", "workingSetBytes", "privateBytes", "io", "ioReadBytesPerSecond", "ioWriteBytesPerSecond", "gpuPercent", "gpuEngine", "path", "publisher", "userName", "state", "startedAtUtc", "uptimeSeconds", "networkBytesPerSecond", "cpu", "memory", "source", "display", "session", "station", "count", "last", "observedCpuTime100ns", "observedIoReadBytes", "observedIoWriteBytes", "firstObservedAtUtc", "lastObservedAtUtc", "isRunning", "command", "enabled", "location"];
    private static readonly HashSet<string> ProcessColumns = ["name", "pid", "state", "parentPid", "sessionId", "threadCount", "handleCount", "priority", "cpuPercent", "workingSetBytes", "privateBytes", "io", "networkBytesPerSecond", "gpuPercent", "gpuEngine", "userName", "publisher", "path"];
    private static readonly HashSet<string> SensorColumns = ["sensor", "originalSensor", "name", "originalName", "unit", "value", "minimum", "maximum", "average", "type", "identity"];
    private static readonly IReadOnlyDictionary<string, HashSet<string>> Columns = new Dictionary<string, HashSet<string>>
    {
        ["processes"] = ProcessColumns, ["details"] = ProcessColumns,
        ["performance"] = SensorColumns,
        ["history"] = ["name", "pid", "state", "cpu", "observedIoReadBytes", "observedIoWriteBytes", "last"],
        ["startup"] = ["name", "source", "enabled", "command", "location"],
        ["users"] = ["name", "session", "station", "state", "count", "cpu", "memory"],
        ["services"] = ["name", "display", "state", "pid"]
    };
    public PreferencesService()
    {
        var directory = Environment.GetEnvironmentVariable("TASKMANAGER_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlueDomainTaskManager");
        file = Path.Combine(Path.GetFullPath(directory), "preferences.json");
        try
        {
            if (!File.Exists(file)) return;
            if (new FileInfo(file).Length > 64 * 1024) throw new JsonSerializationException("Preference file exceeds limit.");
            var loaded = JsonConvert.DeserializeObject<Persisted>(File.ReadAllText(file), Json) ?? throw new JsonSerializationException("Empty preferences.");
            if (loaded.Version != 1 || loaded.IntervalMs is not (1000 or 2000 or 5000)) throw new JsonSerializationException("Unsupported preferences.");
            Validate(loaded.Ui);
            persisted = loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ApiException or ArgumentException)
        { warning = new("preferencesRecovered", "保存的界面偏好无法读取，已恢复默认；下次保存将替换该配置。"); }
    }
    public int IntervalMs { get { lock (gate) return persisted.IntervalMs; } }
    public Unavailable? Warning { get { lock (gate) return warning; } }
    public PreferencesResult Current { get { lock (gate) return new(persisted.Ui, warning); } }
    public PreferencesResult Save(UiPreferences value)
    {
        Validate(value);
        lock (gate) { Write(persisted with { Ui = value with { Window = value.Window ?? persisted.Ui.Window } }); return new(persisted.Ui, warning); }
    }
    public void SaveInterval(int value) { lock (gate) { if (persisted.IntervalMs != value) Write(persisted with { IntervalMs = value }); } }
    public void SaveWindow(WindowPreference value) { lock (gate) { var next = persisted.Ui with { Window = value }; Validate(next); Write(persisted with { Ui = next }); } }
    private void Write(Persisted value)
    {
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var text = JsonConvert.SerializeObject(value, Formatting.Indented, Json);
            if (System.Text.Encoding.UTF8.GetByteCount(text) > 64 * 1024) throw new ApiException(400, "preferencesTooLarge", "偏好大小超过 64 KiB 上限。");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false))) { writer.Write(text); writer.Flush(); stream.Flush(true); }
            // Same-directory rename is atomic on the target Windows filesystem.
            File.Move(temporary, file, true);
            persisted = value;
            warning = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { warning = new("preferencesWriteFailed", "界面偏好未能保存到本机，请检查配置目录权限。"); throw new ApiException(500, warning.Code, warning.Message); }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    private static void Validate(UiPreferences ui)
    {
        if (ui is null || ui.Version != 1 || ui.Density is not ("compact" or "standard") || ui.ProcessView is not ("flat" or "tree") || ui.Pages?.Count > 8) Invalid();
        if (ui!.SearchQuery?.Length > 256 || ui.SearchQuery?.Contains('\0') == true) Invalid();
        foreach (var pair in ui!.Pages ?? new Dictionary<string, PagePreference>())
        {
            var p = pair.Value;
            if (!Pages.Contains(pair.Key) || p is null || p.Query is null || p.Query.Length > 256 || p.Query.Contains('\0') || !(Keys.Contains(p.SortKey) || pair.Key == "performance" && SensorColumns.Contains(p.SortKey)) || p.SortDirection is not ("ascending" or "descending") || !double.IsFinite(p.ScrollTop) || p.ScrollTop is < 0 or > 10_000_000 || p.SelectedIdentity?.Length > 128 || p.VisibleColumns?.Count > 40 || p.VisibleColumns?.Any(key => !(Keys.Contains(key) || pair.Key == "performance" && SensorColumns.Contains(key))) == true || p.Filters?.State is not (null or "all" or "running" or "restricted")) Invalid();
            if (p.VisibleColumns is not null && (!p.VisibleColumns.Contains("name") || (pair.Key is "processes" or "details" && !p.VisibleColumns.Contains("pid")))) Invalid();
            if (p.ColumnWidths is { } widths && (widths.Count > 40 || widths.Any(width => !Columns.TryGetValue(pair.Key, out var allowed) || !allowed.Contains(width.Key) || !double.IsFinite(width.Value) || width.Value is < 64 or > 1600))) Invalid();
            if (p.ResourceType is not (null or "system" or "cpu" or "memory" or "network" or "disk" or "gpu" or "hwinfo") || p.ResourceId?.Length > 256 || p.ResourceId?.Contains('\0') == true || p.CpuGraphMode is not (null or "total" or "logicalProcessors")) Invalid();
        }
        if (ui.Window is { } window && (window.Width is < 960 or > 3840 || window.Height is < 640 or > 2160 || !double.IsFinite(window.ZoomFactor) || window.ZoomFactor is < .75 or > 2)) Invalid();
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Invalid() => throw new ApiException(400, "invalidPreferences", "界面偏好字段无效或超出范围；名称/PID列必须保留。");
    private sealed record Persisted(int Version, int IntervalMs, UiPreferences Ui);
}
