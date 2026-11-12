using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using System.Globalization;
using Takao.Host.Models;
using Takao.Host.Services;

namespace Takao.Host.MewUI;

internal sealed class TaskManagerWindow : Window
{
    private readonly TaskManagerView view;

    public TaskManagerWindow(SnapshotService snapshots, PreferencesService preferences, ProcessActionService actions, SystemInformationService systemInformation)
    {
        view = new TaskManagerView(snapshots, preferences, actions, systemInformation);
        Title = "Takao";
        this.Resizable(1100, 720, minWidth: 960, minHeight: 640)
            .StartCenterScreen()
            .Content(view)
            .OnLoaded(view.Start)
            .OnClosed(view.Stop);
    }
}

internal sealed class TaskManagerView : UserControl
{
    private static readonly string[] PageKeys = ["processes", "performance", "history", "startup", "users", "details", "services", "settings", "systemInformation"];
    private static readonly string[] PageTitles = ["进程", "性能", "应用历史记录", "启动应用", "用户", "详细信息", "服务", "设置", "系统信息"];
    private readonly SnapshotService snapshots;
    private readonly PreferencesService preferences;
    private readonly ProcessActionService actions;
    private readonly SystemInformationService systemInformation;
    private readonly List<FrameworkElement> pages = [];
    private readonly List<ProcessDto> processRows = [];
    private readonly ItemsView<ProcessDto> processItems;
    private readonly List<ProcessHistoryDto> historyRows = [];
    private readonly ItemsView<ProcessHistoryDto> historyItems;
    private readonly List<StartupDto> startupRows = [];
    private readonly ItemsView<StartupDto> startupItems;
    private readonly List<UserDto> userRows = [];
    private readonly ItemsView<UserDto> userItems;
    private readonly List<ServiceDto> serviceRows = [];
    private readonly ItemsView<ServiceDto> serviceItems;
    private readonly ItemsView<ProcessDto> detailItems;
    private GridView? processGrid;
    private ListBox? navigation;
    private TextBox? searchBox;
    private TextBlock? cpuSummary;
    private TextBlock? memorySummary;
    private TextBlock? processSummary;
    private TextBlock? statusText;
    private TextBlock? pageTitle;
    private TextBlock? systemInfoSummary;
    private DispatcherTimer? timer;
    private int currentPage;
    private string query = string.Empty;
    private readonly ObservableValue<string> search = new(string.Empty);
    private readonly List<(TextBlock Block, Func<string> Get)> liveText = [];

    public TaskManagerView(SnapshotService snapshots, PreferencesService preferences, ProcessActionService actions, SystemInformationService systemInformation)
    {
        this.snapshots = snapshots;
        this.preferences = preferences;
        this.actions = actions;
        this.systemInformation = systemInformation;
        processItems = ItemsView.Create(processRows, row => row.Name, row => row.Identity);
        historyItems = ItemsView.Create(historyRows, row => row.Name, row => row.Identity);
        startupItems = ItemsView.Create(startupRows, row => row.Name, row => row.Id);
        userItems = ItemsView.Create(userRows, row => row.UserName ?? row.StationName, row => row.SessionId);
        serviceItems = ItemsView.Create(serviceRows, row => row.DisplayName, row => row.Name);
        detailItems = ItemsView.Create(processRows, row => row.Name, row => row.Identity);
        Build();
    }

    public void Start()
    {
        timer = new DispatcherTimer().IntervalMs(Math.Max(500, preferences.IntervalMs)).OnTick(Poll);
        timer.Start();
        Poll();
        _ = RefreshSystemInformation(false);
    }

    public void Stop() => timer?.Stop();

    protected override Element OnBuild()
    {
        var root = new Grid().Rows("48, *").Columns("204, *");
        var top = BuildTopBar().ColumnSpan(2);
        var sidebar = BuildSidebar().Row(1);
        var content = BuildContent().Row(1).Column(1);
        root.Children(top, sidebar, content);
        return root;
    }

    private FrameworkElement BuildTopBar()
    {
        searchBox = new TextBox()
            .Width(360)
            .Placeholder("搜索名称、PID 或发布者")
            .BindText(search)
            .OnTextChanged(value =>
            {
                query = value.Trim();
                UpdateProcessRows();
            });
        cpuSummary = new TextBlock().Text("CPU —").CenterVertical();
        memorySummary = new TextBlock().Text("内存 —").CenterVertical();
        processSummary = new TextBlock().Text("进程 —").CenterVertical();
        statusText = new TextBlock().Text("正在连接 Windows 数据源…").CenterVertical();
        pageTitle = new TextBlock().Text(PageTitles[0]).FontSize(ThemeFontSize.Medium).SemiBold().CenterVertical();

        var brand = new TextBlock().Text("Takao").FontSize(ThemeFontSize.Medium).SemiBold().CenterVertical().Width(204);
        var summary = new StackPanel().Horizontal().Spacing(18).Children(cpuSummary, memorySummary, processSummary, statusText);
        return new Border()
            .Padding(16, 0)
            .BorderThickness(new Thickness(0, 0, 0, 1))
            .WithTheme((theme, border) =>
            {
                border.Background(theme.Palette.WindowBackground);
                border.BorderBrush(theme.Palette.ControlBorder);
            })
            .Child(new Grid().Columns("204, Auto, *, Auto").Children(
                brand,
                searchBox.Column(1).Margin(0, 0, 16, 0),
                new Border().Column(2),
                summary.Column(3)));
    }

    private FrameworkElement BuildSidebar()
    {
        navigation = new ListBox()
            .Items(PageTitles)
            .ItemHeight(40)
            .SelectedIndex(0)
            .OnSelectionChanged(value =>
            {
                if (value is not string title) return;
                var index = Array.IndexOf(PageTitles, title);
                if (index >= 0) SelectPage(index);
            });
        var settingsHint = new TextBlock().Text("设置").FontSize(ThemeFontSize.Small).SemiBold();
        var sidebar = new Border()
            .Padding(8)
            .BorderThickness(new Thickness(0, 0, 1, 0))
            .WithTheme((theme, border) =>
            {
                border.Background(theme.Palette.WindowBackground);
                border.BorderBrush(theme.Palette.ControlBorder);
            })
            .Child(new DockPanel().Spacing(8).Children(
                new StackPanel().DockBottom().Spacing(4).Children(settingsHint, new TextBlock().Text("MewUI / Windows").FontSize(ThemeFontSize.Small)),
                navigation));
        return sidebar;
    }

    private FrameworkElement BuildContent()
    {
        pages.Clear();
        pages.Add(BuildProcessesPage());
        pages.Add(BuildPerformancePage());
        pages.Add(BuildHistoryPage());
        pages.Add(BuildStartupPage());
        pages.Add(BuildUsersPage());
        pages.Add(BuildDetailsPage());
        pages.Add(BuildServicesPage());
        pages.Add(BuildSettingsPage());
        pages.Add(BuildSystemInformationPage());
        foreach (var page in pages) page.IsVisible = false;
        pages[0].IsVisible = true;

        var body = new Grid().Rows("56, *").Children(
            new Border().Row(0).Padding(16, 0).BorderThickness(new Thickness(0, 0, 0, 1))
                .WithTheme((theme, border) => border.BorderBrush(theme.Palette.ControlBorder))
                .Child(pageTitle!),
            new Border().Row(1).Padding(16).Child(new Grid().Children(pages.ToArray())));
        return body;
    }

    private FrameworkElement BuildProcessesPage()
    {
        var run = new Button().Content("运行新任务").OnClick(() => RunTask());
        var end = new Button().Content("结束任务").IsEnabled(false).OnClick(() => EndSelected());
        processGrid = new GridView { ItemsSource = processItems, ZebraStriping = false, ShowGridLines = false };
        processGrid.Columns(
            new GridViewColumn<ProcessDto>().Header("名称").StarWidth(3, minWidth: 260).SortBy(row => row.Name, StringComparer.OrdinalIgnoreCase).Text(row => row.Name),
            new GridViewColumn<ProcessDto>().Header("状态").Width(100).Text(row => row.State == "running" ? "运行中" : row.State),
            new GridViewColumn<ProcessDto>().Header("CPU").Width(100).HeaderTextAlignment(TextAlignment.Right).SortBy(row => row.CpuPercent ?? double.MinValue).Text(row => Percent(row.CpuPercent)),
            new GridViewColumn<ProcessDto>().Header("内存").Width(120).HeaderTextAlignment(TextAlignment.Right).SortBy(row => row.WorkingSetBytes ?? long.MinValue).Text(row => Bytes(row.WorkingSetBytes)),
            new GridViewColumn<ProcessDto>().Header("I/O").Width(120).HeaderTextAlignment(TextAlignment.Right).Text(row => Rate(row.IoReadBytesPerSecond, row.IoWriteBytesPerSecond)),
            new GridViewColumn<ProcessDto>().Header("PID").Width(86).HeaderTextAlignment(TextAlignment.Right).SortBy(row => row.Pid).Text(row => row.Pid.ToString(CultureInfo.InvariantCulture)));
        processGrid.OnSelectionChanged(value => end.IsEnabled = value is ProcessDto dto && dto.CanTerminate);
        // The global search box lives in the title bar. Keep the page command
        // row limited to actions so a MewUI element never has two parents.
        var command = new DockPanel().Spacing(8).Children(new StackPanel().DockRight().Horizontal().Spacing(8).Children(run, end));
        return new Grid().Rows("Auto, *").Children(command, new Border().Row(1).Child(processGrid));
    }

    private FrameworkElement BuildPerformancePage()
    {
        return PageStack(
            new TextBlock().Text("系统资源").FontSize(ThemeFontSize.Medium).SemiBold(),
            MetricRow("CPU 使用率", () => snapshots.Current?.System.CpuPercent is { } cpu ? Percent(cpu) : "—"),
            MetricRow("物理内存", () => snapshots.Current?.System.MemoryPercent is { } memory ? Percent(memory) : "—"),
            MetricRow("网络接收 / 发送", () => Rate(snapshots.Current?.System.NetworkReceiveBytesPerSecond, snapshots.Current?.System.NetworkSendBytesPerSecond)),
            MetricRow("磁盘读取 / 写入", () => Rate(snapshots.Current?.System.DiskReadBytesPerSecond, snapshots.Current?.System.DiskWriteBytesPerSecond)),
            new TextBlock().Text("曲线使用 Windows 真实成功采样点；首次采样或来源不可用时保留‘—’。").FontSize(ThemeFontSize.Small));
    }

    private FrameworkElement BuildHistoryPage()
    {
        var grid = new GridView { ItemsSource = historyItems, ShowGridLines = false, ZebraStriping = false };
        grid.Columns(
            new GridViewColumn<ProcessHistoryDto>().Header("名称").StarWidth(3, minWidth: 260).Text(row => row.Name),
            new GridViewColumn<ProcessHistoryDto>().Header("PID").Width(90).HeaderTextAlignment(TextAlignment.Right).Text(row => row.Pid.ToString(CultureInfo.InvariantCulture)),
            new GridViewColumn<ProcessHistoryDto>().Header("状态").Width(100).Text(row => row.IsRunning ? "运行中" : "已退出"),
            new GridViewColumn<ProcessHistoryDto>().Header("首次观测").Width(180).Text(row => row.FirstObservedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
            new GridViewColumn<ProcessHistoryDto>().Header("CPU 时间").Width(120).HeaderTextAlignment(TextAlignment.Right).Text(row => row.ObservedCpuTime100ns ?? "—"));
        return PageStack(new TextBlock().Text("本次会话应用历史记录").FontSize(ThemeFontSize.Medium).SemiBold(), LiveText(() => $"已观测 {snapshots.Current?.History.Count ?? 0} 个进程身份。"), grid);
    }

    private FrameworkElement BuildStartupPage()
    {
        var grid = new GridView { ItemsSource = startupItems, ShowGridLines = false, ZebraStriping = false };
        grid.Columns(
            new GridViewColumn<StartupDto>().Header("名称").StarWidth(2, minWidth: 220).Text(row => row.Name),
            new GridViewColumn<StartupDto>().Header("来源").Width(160).Text(row => row.Source),
            new GridViewColumn<StartupDto>().Header("状态").Width(120).Text(row => row.Enabled is true ? "已启用" : row.Enabled is false ? "已禁用" : "未知"),
            new GridViewColumn<StartupDto>().Header("命令").StarWidth(3, minWidth: 320).Text(row => row.Command));
        return PageStack(new TextBlock().Text("启动应用").FontSize(ThemeFontSize.Medium).SemiBold(), LiveText(() => $"Windows Run / RunOnce 与 Startup 文件夹：{snapshots.Current?.StartupItems.Count ?? 0} 项。"), grid);
    }

    private FrameworkElement BuildUsersPage()
    {
        var grid = new GridView { ItemsSource = userItems, ShowGridLines = false, ZebraStriping = false };
        grid.Columns(
            new GridViewColumn<UserDto>().Header("用户").StarWidth(2, minWidth: 220).Text(row => row.UserName ?? "未知用户"),
            new GridViewColumn<UserDto>().Header("会话").Width(90).HeaderTextAlignment(TextAlignment.Right).Text(row => row.SessionId.ToString(CultureInfo.InvariantCulture)),
            new GridViewColumn<UserDto>().Header("状态").Width(140).Text(row => row.State),
            new GridViewColumn<UserDto>().Header("进程").Width(100).HeaderTextAlignment(TextAlignment.Right).Text(row => row.ProcessCount.ToString(CultureInfo.InvariantCulture)),
            new GridViewColumn<UserDto>().Header("CPU").Width(100).HeaderTextAlignment(TextAlignment.Right).Text(row => Percent(row.CpuPercent)),
            new GridViewColumn<UserDto>().Header("内存").Width(120).HeaderTextAlignment(TextAlignment.Right).Text(row => Bytes(row.WorkingSetBytes)));
        return PageStack(new TextBlock().Text("用户").FontSize(ThemeFontSize.Medium).SemiBold(), LiveText(() => $"当前会话用户：{snapshots.Current?.Users.Count ?? 0} 个。"), grid);
    }

    private FrameworkElement BuildDetailsPage()
    {
        var grid = new GridView { ItemsSource = detailItems, ShowGridLines = false, ZebraStriping = false };
        grid.Columns(
            new GridViewColumn<ProcessDto>().Header("名称").StarWidth(2, minWidth: 260).Text(row => row.Name),
            new GridViewColumn<ProcessDto>().Header("PID").Width(90).HeaderTextAlignment(TextAlignment.Right).Text(row => row.Pid.ToString(CultureInfo.InvariantCulture)),
            new GridViewColumn<ProcessDto>().Header("父 PID").Width(100).HeaderTextAlignment(TextAlignment.Right).Text(row => row.ParentPid.ToString(CultureInfo.InvariantCulture)),
            new GridViewColumn<ProcessDto>().Header("会话").Width(90).HeaderTextAlignment(TextAlignment.Right).Text(row => row.SessionId?.ToString(CultureInfo.InvariantCulture) ?? "—"),
            new GridViewColumn<ProcessDto>().Header("优先级").Width(120).Text(row => row.Priority ?? "—"),
            new GridViewColumn<ProcessDto>().Header("路径").StarWidth(3, minWidth: 360).Text(row => row.Path ?? "不可用"));
        return PageStack(new TextBlock().Text("详细信息").FontSize(ThemeFontSize.Medium).SemiBold(), LiveText(() => $"真实进程身份：{snapshots.Current?.Processes.Count ?? 0} 条。"), grid);
    }

    private FrameworkElement BuildServicesPage()
    {
        var grid = new GridView { ItemsSource = serviceItems, ShowGridLines = false, ZebraStriping = false };
        grid.Columns(
            new GridViewColumn<ServiceDto>().Header("显示名称").StarWidth(2, minWidth: 280).Text(row => row.DisplayName),
            new GridViewColumn<ServiceDto>().Header("服务名").StarWidth(2, minWidth: 240).Text(row => row.Name),
            new GridViewColumn<ServiceDto>().Header("状态").Width(140).Text(row => row.State),
            new GridViewColumn<ServiceDto>().Header("PID").Width(90).HeaderTextAlignment(TextAlignment.Right).Text(row => row.Pid == 0 ? "—" : row.Pid.ToString(CultureInfo.InvariantCulture)));
        return PageStack(new TextBlock().Text("服务").FontSize(ThemeFontSize.Medium).SemiBold(), LiveText(() => $"SCM 服务：{snapshots.Current?.Services.Count ?? 0} 项。"), grid, new TextBlock().Text("服务状态由 Windows Service Control Manager 返回。"));
    }

    private FrameworkElement BuildSettingsPage()
    {
        var interval = new ComboBox().Items(["1 秒", "2 秒", "5 秒"]).SelectedIndex(preferences.IntervalMs switch { 2000 => 1, 5000 => 2, _ => 0 }).OnSelectionChanged(value =>
        {
            if (value is string text) _ = snapshots.ApplySettings(new SettingsRequest(text.StartsWith("5") ? 5000 : text.StartsWith("2") ? 2000 : 1000, snapshots.Settings.Paused));
        });
        var pausedState = new ObservableValue<bool>(snapshots.Settings.Paused);
        var paused = new CheckBox().Content("暂停自动刷新").BindIsChecked(pausedState).OnCheckedChanged(value => _ = snapshots.ApplySettings(new SettingsRequest(snapshots.Settings.IntervalMs, value)));
        return PageStack(new TextBlock().Text("设置").FontSize(ThemeFontSize.Medium).SemiBold(),
            SettingRow("采样间隔", "控制真实 Windows 采样频率。", interval),
            SettingRow("刷新状态", "暂停时保留最后成功快照并标注状态。", paused),
            new TextBlock().Text("界面由 MewUI Win32 + Direct2D 在当前 Host 进程内绘制。").FontSize(ThemeFontSize.Small));
    }

    private FrameworkElement BuildSystemInformationPage()
    {
        systemInfoSummary = new TextBlock().Text("正在读取 Windows 硬件概览…");
        var refresh = new Button().Content("刷新硬件信息").OnClick(() => _ = RefreshSystemInformation(true));
        return PageStack(new TextBlock().Text("系统信息").FontSize(ThemeFontSize.Medium).SemiBold(),
            new TextBlock().Text("硬件概览由 Windows 原生接口按需读取，不参与实时采样。"),
            refresh,
            systemInfoSummary,
            LiveText(() => snapshots.Current is { } current ? $"当前采样时间：{current.CapturedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}" : "当前采样时间：—"));
    }

    private async Task RefreshSystemInformation(bool refresh)
    {
        try
        {
            var info = await systemInformation.ReadAsync(refresh, CancellationToken.None);
            if (systemInfoSummary is null) return;
            var cpu = info.Processors.FirstOrDefault()?.Name ?? "不可用";
            var gpu = info.GraphicsAdapters.FirstOrDefault()?.Name ?? "不可用";
            var os = info.OperatingSystem.Caption ?? "不可用";
            var memory = info.Memory.InstalledBytes ?? "不可用";
            systemInfoSummary.Text = $"状态：{info.Status}\nCPU：{cpu}\n显卡：{gpu}\n内存：{memory}\n主板：{info.Motherboard.Manufacturer ?? "不可用"} {info.Motherboard.Product ?? string.Empty}\nWindows：{os} {info.OperatingSystem.DisplayVersion ?? string.Empty} {info.OperatingSystem.Build ?? string.Empty}";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (systemInfoSummary is not null) systemInfoSummary.Text = $"硬件信息暂不可用：{error.Message}";
        }
    }

    private static FrameworkElement PageStack(params FrameworkElement[] children) => new ScrollViewer().AutoVerticalScroll().NoHorizontalScroll().Content(new StackPanel().Vertical().Spacing(16).Children(children));

    private static FrameworkElement SettingRow(string title, string description, FrameworkElement control) => new Border().Padding(16).BorderThickness(1).CornerRadius(8).WithTheme((theme, border) => { border.Background = theme.Palette.ControlBackground; border.BorderBrush = theme.Palette.ControlBorder; }).Child(new Grid().Columns("*, Auto").Children(new StackPanel().Spacing(4).Children(new TextBlock().Text(title).SemiBold(), new TextBlock().Text(description).FontSize(ThemeFontSize.Small)), control.Column(1).CenterVertical()));

    private FrameworkElement MetricRow(string label, Func<string> value)
    {
        return new Border().Padding(16).BorderThickness(1).CornerRadius(8)
            .WithTheme((theme, border) => { border.Background = theme.Palette.ControlBackground; border.BorderBrush = theme.Palette.ControlBorder; })
            .Child(new Grid().Columns("*, Auto").Children(new TextBlock().Text(label), LiveText(value).Column(1).SemiBold()));
    }

    private TextBlock LiveText(Func<string> value)
    {
        var block = new TextBlock { Text = value() };
        liveText.Add((block, value));
        return block;
    }

    private void SelectPage(int index)
    {
        currentPage = Math.Clamp(index, 0, pages.Count - 1);
        for (var i = 0; i < pages.Count; i++) pages[i].IsVisible = i == currentPage;
        if (pageTitle is not null) pageTitle.Text = PageTitles[currentPage];
    }

    private void Poll()
    {
        UpdateSummary();
        UpdateProcessRows();
        UpdateOtherRows();
        foreach (var item in liveText) item.Block.Text = item.Get();
        if (statusText is not null) statusText.Text = snapshots.Current is null ? "正在预热 Windows 数据源…" : snapshots.Settings.Paused ? "已暂停" : $"已更新 {snapshots.Current.CapturedAtUtc.ToLocalTime():HH:mm:ss}";
    }

    private void UpdateSummary()
    {
        var current = snapshots.Current;
        if (current is null) return;
        if (cpuSummary is not null) cpuSummary.Text = $"CPU {Percent(current.System.CpuPercent)}";
        if (memorySummary is not null) memorySummary.Text = $"内存 {Percent(current.System.MemoryPercent)}";
        if (processSummary is not null) processSummary.Text = $"进程 {current.System.ProcessCount.ToString(CultureInfo.InvariantCulture)}";
    }

    private void UpdateProcessRows()
    {
        var current = snapshots.Current;
        if (current is null) return;
        processRows.Clear();
        processRows.AddRange(string.IsNullOrWhiteSpace(query) ? current.Processes : current.Processes.Where(row => row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Pid.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase) || (row.Publisher?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)));
        processItems.Invalidate();
        detailItems.Invalidate();
    }

    private void UpdateOtherRows()
    {
        var current = snapshots.Current;
        if (current is null) return;
        historyRows.Clear(); historyRows.AddRange(current.History); historyItems.Invalidate();
        startupRows.Clear(); startupRows.AddRange(current.StartupItems); startupItems.Invalidate();
        userRows.Clear(); userRows.AddRange(current.Users); userItems.Invalidate();
        serviceRows.Clear(); serviceRows.AddRange(current.Services); serviceItems.Invalidate();
    }

    private async void RunTask()
    {
        var owner = FindVisualRoot() as Window;
        if (owner is null) return;
        var executable = new TextBox().Placeholder("例如 C:\\Windows\\System32\\notepad.exe");
        var arguments = new TextBox().Placeholder("参数（可选）");
        var feedback = new TextBlock().FontSize(ThemeFontSize.Small);
        var dialog = new Window
        {
            Title = "运行新任务",
            StartupLocation = WindowStartupLocation.CenterOwner,
            WindowSize = WindowSize.FitContentSize(520, 280),
            Padding = new Thickness(20),
        };
        var launch = new Button().Content("运行");
        launch.OnClick(() =>
        {
            try
            {
                var result = actions.Start(new StartRequest(executable.Text, arguments.Text));
                feedback.Text = $"{result.Message} PID {result.Pid.ToString(CultureInfo.InvariantCulture)}";
                statusText!.Text = feedback.Text;
                dialog.Close();
            }
            catch (ApiException error)
            {
                feedback.Text = error.Message;
            }
        });
        var cancel = new Button().Content("取消").OnClick(dialog.Close);
        dialog.Content(new StackPanel().Vertical().Spacing(12).Children(
            new TextBlock().Text("程序路径").SemiBold(), executable,
            new TextBlock().Text("参数").SemiBold(), arguments,
            feedback,
            new StackPanel().Horizontal().Spacing(8).Children(cancel, launch)));
        await dialog.ShowDialogAsync(owner);
    }

    private async void EndSelected()
    {
        if (processGrid?.SelectedItem is not ProcessDto row || !row.CanTerminate || row.CreationTimeFileTime is not { } creation)
            return;
        var owner = FindVisualRoot() as Window;
        if (owner is null || !await MessageBox.ConfirmAsync(
                $"结束 {row.Name}（PID {row.Pid.ToString(CultureInfo.InvariantCulture)}）？",
                icon: PromptIconKind.Warning,
                detail: "Windows 将终止该进程，未保存的数据可能丢失。",
                owner: owner)) return;
        try
        {
            var result = actions.Terminate(row.Pid, new TerminateRequest(creation));
            statusText!.Text = result.Message;
        }
        catch (ApiException error)
        {
            statusText!.Text = error.Message;
        }
    }

    private static string Percent(double? value) => value is { } number && double.IsFinite(number) ? $"{number:0.0}%" : "—";
    private static string Bytes(long? value) => value is { } bytes && bytes >= 0 ? FormatBytes(bytes) : "—";
    private static string Rate(double? read, double? write) => read is { } r && write is { } w ? $"{FormatBytes(r)}/s · {FormatBytes(w)}/s" : "—";
    private static string FormatBytes(double bytes)
    {
        if (!double.IsFinite(bytes) || bytes < 0) return "—";
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var index = 0;
        while (bytes >= 1024 && index < units.Length - 1) { bytes /= 1024; index++; }
        return $"{bytes:0.0} {units[index]}";
    }
}
