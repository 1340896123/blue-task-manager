using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using System.Globalization;
using Takao.Host.Models;
using Takao.Host.Native;
using Takao.Host.Services;

namespace Takao.Host.MewUI;

internal sealed class TaskManagerWindow : Window
{
    private readonly TaskManagerView view;

    public TaskManagerWindow(SnapshotService snapshots, PreferencesService preferences, ProcessActionService actions, SystemInformationService systemInformation, ShellActionService shell, ServiceActionService serviceActions, DesktopWindowService desktop, TaskManagerIntegrationService integration)
    {
        view = new TaskManagerView(snapshots, preferences, actions, systemInformation, shell, serviceActions, desktop, integration);
        Title = "Takao";
        this.Resizable(1100, 720, minWidth: 960, minHeight: 640)
            .ExtendClientAreaTitleBarHeight(48)
            .Borderless(true)
            .AllowsTransparency(true)
            .StartCenterScreen()
            .Content(view)
            .OnLoaded(() =>
            {
                view.Start();
                view.UpdateWindowState(WindowState);
                WindowChromeService.Apply(this);
            })
            .OnClosed(view.Stop);
        WindowStateChanged += view.UpdateWindowState;
    }
}

internal sealed class TaskManagerView : UserControl
{
    private sealed record NavigationEntry(string Key, string Title, string Glyph);

    private static readonly string[] PageKeys = ["overview", "processes", "performance", "history", "startup", "users", "details", "services", "settings", "systemInformation"];
    private static readonly string[] PageTitles = ["总览", "进程", "性能", "应用历史记录", "启动应用", "用户", "详细信息", "服务", "设置", "系统信息"];
    private static readonly IReadOnlyList<NavigationEntry> NavigationEntries =
    [
        new("overview", "总览", "\uE80F"),
        new("processes", "进程", "\uE7F4"),
        new("performance", "性能", "\uE9D9"),
        new("history", "应用历史记录", "\uE81C"),
        new("startup", "启动应用", "\uE713"),
        new("users", "用户", "\uE716"),
        new("details", "详细信息", "\uE8A5"),
        new("services", "服务", "\uE77B"),
        new("settings", "设置", "\uE713"),
        new("systemInformation", "系统信息", "\uE950")
    ];
    private readonly SnapshotService snapshots;
    private readonly PreferencesService preferences;
    private readonly ProcessActionService actions;
    private readonly SystemInformationService systemInformation;
    private readonly ShellActionService shell;
    private readonly ServiceActionService serviceActions;
    private readonly DesktopWindowService desktop;
    private readonly TaskManagerIntegrationService integration;
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
    private readonly List<HistoryPoint> performanceHistoryRows = [];
    private readonly ItemsView<HistoryPoint> performanceHistoryItems;
    private readonly ItemsView<ProcessDto> detailItems;
    private GridView? processGrid;
    private GridView? performanceGrid;
    private GridView? historyGrid;
    private GridView? startupGrid;
    private GridView? usersGrid;
    private GridView? detailGrid;
    private GridView? serviceGrid;
    private GridView? overviewRecentGrid;
    private GridView? overviewPerformanceGrid;
    private NavigationList? navigation;
    private TextBox? searchBox;
    private TextBlock? cpuSummary;
    private TextBlock? memorySummary;
    private TextBlock? processSummary;
    private TextBlock? statusText;
    private TextBlock? pageTitle;
    private TextBlock? systemInfoSummary;
    private Button? systemInfoCopyButton;
    private string systemInformationClipboard = string.Empty;
    private TextBlock? integrationSummary;
    private Button? integrationEnableButton;
    private Button? integrationDisableButton;
    private Button? maximizeButton;
    private DispatcherTimer? timer;
    private int currentPage;
    private string query = string.Empty;
    private uint? processSessionFilter;
    private readonly ObservableValue<string> search = new(string.Empty);
    private readonly List<(TextBlock Block, Func<string> Get)> liveText = [];

    public TaskManagerView(SnapshotService snapshots, PreferencesService preferences, ProcessActionService actions, SystemInformationService systemInformation, ShellActionService shell, ServiceActionService serviceActions, DesktopWindowService desktop, TaskManagerIntegrationService integration)
    {
        this.snapshots = snapshots;
        this.preferences = preferences;
        this.actions = actions;
        this.systemInformation = systemInformation;
        this.shell = shell;
        this.serviceActions = serviceActions;
        this.desktop = desktop;
        this.integration = integration;
        processItems = ItemsView.Create(processRows, row => row.Name, row => row.Identity);
        historyItems = ItemsView.Create(historyRows, row => row.Name, row => row.Identity);
        startupItems = ItemsView.Create(startupRows, row => row.Name, row => row.Id);
        userItems = ItemsView.Create(userRows, row => row.UserName ?? row.StationName, row => row.SessionId);
        serviceItems = ItemsView.Create(serviceRows, row => row.DisplayName, row => row.Name);
        performanceHistoryItems = ItemsView.Create(performanceHistoryRows, row => row.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture), row => row.CapturedAtUtc);
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
        var root = ShellComponents.WindowShell(BuildTopBar(), BuildSidebar(), BuildContent());
        root.OnMouseDown(HandleWindowMouseDown);
        return root;
    }

    private FrameworkElement BuildTopBar()
    {
        searchBox = new TextBox()
            .Width(300)
            .Placeholder("搜索名称、PID 或发布者")
            .BindText(search)
            .OnTextChanged(value =>
            {
                query = value.Trim();
                processSessionFilter = null;
                UpdateProcessRows();
            });
        cpuSummary = new TextBlock().Text("CPU —").CenterVertical();
        memorySummary = new TextBlock().Text("内存 —").CenterVertical();
        processSummary = new TextBlock().Text("进程 —").CenterVertical();
        statusText = new TextBlock().Text("正在连接 Windows 数据源…").CenterVertical();
        pageTitle = new TextBlock().Text(PageTitles[0]).FontSize(ThemeFontSize.Medium).SemiBold().CenterVertical();

        var brand = new StackPanel().Horizontal().Spacing(8).Width(204).Children(
            new TextBlock().Text("▣").FontSize(ThemeFontSize.Medium).SemiBold().CenterVertical(),
            new TextBlock().Text("Takao").FontSize(ThemeFontSize.Medium).SemiBold().CenterVertical());
        var summary = new StackPanel().Horizontal().Spacing(18).Children(cpuSummary, memorySummary, processSummary, statusText);
        var minimize = WindowButton("\uE921", "最小化窗口", owner => owner.Minimize());
        maximizeButton = WindowButton("\uE922", "最大化窗口", _ => ToggleMaximize());
        var close = WindowButton("\uE8BB", "关闭窗口", owner => owner.Close());
        var windowButtons = ShellComponents.CommandBar(minimize, maximizeButton, close);
        return ShellComponents.TitleBar(
            brand,
            searchBox.Margin(0, 0, 16, 0),
            summary,
            new Border().OnMouseDown(_ => BeginDragMove()).OnMouseDoubleClick(_ => ToggleMaximize()),
            windowButtons);
    }

    private Button WindowButton(string glyph, string label, Action<Window> action)
    {
        var button = new Button().Content(glyph).Width(46).Height(48).ToolTip(label).OnClick(() =>
        {
            if (FindVisualRoot() is Window owner) action(owner);
        });
        button.FontFamily = "Segoe Fluent Icons";
        return button;
    }

    public void UpdateWindowState(WindowState state)
    {
        if (maximizeButton is null) return;
        var maximized = state == WindowState.Maximized;
        maximizeButton.Content = WindowGlyph(maximized ? "\uE923" : "\uE922");
        maximizeButton.ToolTip = new TextBlock().Text(maximized ? "还原窗口" : "最大化窗口");
    }

    private static TextBlock WindowGlyph(string glyph)
    {
        var text = new TextBlock().Text(glyph).CenterVertical();
        text.FontFamily = "Segoe Fluent Icons";
        return text;
    }

    private void BeginDragMove()
    {
        if (FindVisualRoot() is Window owner) owner.DragMove();
    }

    private void ToggleMaximize()
    {
        if (FindVisualRoot() is not Window owner) return;
        if (owner.WindowState == WindowState.Maximized) owner.Restore();
        else owner.Maximize();
    }

    private void HandleWindowMouseDown(MouseEventArgs args)
    {
        if (FindVisualRoot() is not Window owner || owner.WindowState == WindowState.Maximized) return;
        var position = owner.Position;
        var size = owner.ClientSize;
        var x = args.ScreenPosition.X - position.X;
        var y = args.ScreenPosition.Y - position.Y;
        const double edge = 8;
        var left = x <= edge;
        var right = x >= size.Width - edge;
        var top = y <= edge;
        var bottom = y >= size.Height - edge;
        if (!left && !right && !top && !bottom) return;
        var resizeEdge = (left, right, top, bottom) switch
        {
            (true, false, true, false) => ResizeEdge.TopLeft,
            (false, true, true, false) => ResizeEdge.TopRight,
            (true, false, false, true) => ResizeEdge.BottomLeft,
            (false, true, false, true) => ResizeEdge.BottomRight,
            (true, false, _, _) => ResizeEdge.Left,
            (false, true, _, _) => ResizeEdge.Right,
            (_, _, true, false) => ResizeEdge.Top,
            _ => ResizeEdge.Bottom,
        };
        owner.DragResize(resizeEdge);
        args.Handled = true;
    }

    private FrameworkElement BuildSidebar()
    {
        navigation = new NavigationList
        {
            ItemHeight = 44,
            ItemPadding = new Thickness(12, 0),
            SelectedIndex = 0,
        }
            .Items(
                NavigationEntries,
                item => $"{item.Glyph}  {item.Title}",
                _ => NavigationItemKind.Item,
                item => item.Key)
            .OnSelectionChanged(value =>
            {
                if (value is not NavigationEntry entry) return;
                var index = Array.IndexOf(PageKeys, entry.Key);
                if (index >= 0) SelectPage(index);
            });
        var footer = new StackPanel().Spacing(4).Children(
            new TextBlock().Text("Takao").FontSize(ThemeFontSize.Small).SemiBold(),
            new TextBlock().Text("Windows 监控").FontSize(ThemeFontSize.Small));
        return ShellComponents.Sidebar(navigation, footer);
    }

    private FrameworkElement BuildContent()
    {
        pages.Clear();
        pages.Add(BuildOverviewPage());
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
            ShellComponents.PageHeader(pageTitle!).Row(0),
            new Border().Row(1).Padding(16).Child(new Grid().Children(pages.ToArray())));
        return body;
    }

    private FrameworkElement BuildOverviewPage()
    {
        // The overview uses only values from the current Windows snapshot.  It
        // deliberately keeps the compact card/graph/activity hierarchy from
        // the reference while avoiding demo values when the collector warms up.
        var metrics = new Grid().Columns("*, *, *, *, *").Children(
            OverviewMetric("CPU", () => Percent(snapshots.Current?.System.CpuPercent)),
            OverviewMetric("内存", () => Percent(snapshots.Current?.System.MemoryPercent)),
            OverviewMetric("进程", () => snapshots.Current?.System.ProcessCount.ToString(CultureInfo.InvariantCulture) ?? "—"),
            OverviewMetric("磁盘", () => Percent(snapshots.Current?.System.DiskActivePercent)),
            OverviewMetric("网络", () => Rate(snapshots.Current?.System.NetworkReceiveBytesPerSecond, snapshots.Current?.System.NetworkSendBytesPerSecond)));

        overviewRecentGrid = new GridView { ItemsSource = historyItems, ShowGridLines = false, ZebraStriping = false, HeaderHeight = 40 };
        overviewRecentGrid.Columns(
            new GridViewColumn<ProcessHistoryDto>().Header("时间").Width(120).Text(row => row.LastObservedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
            new GridViewColumn<ProcessHistoryDto>().Header("事件").Width(150).Text(_ => "进程观测"),
            new GridViewColumn<ProcessHistoryDto>().Header("对象").StarWidth(2, minWidth: 220).Text(row => row.Name),
            new GridViewColumn<ProcessHistoryDto>().Header("详细信息").StarWidth(2, minWidth: 240).Text(row => row.IsRunning ? "运行中" : "已退出"));

        overviewPerformanceGrid = new GridView { ItemsSource = performanceHistoryItems, ShowGridLines = false, ZebraStriping = false, HeaderHeight = 40 };
        overviewPerformanceGrid.Columns(
            new GridViewColumn<HistoryPoint>().Header("时间").Width(120).Text(row => row.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
            new GridViewColumn<HistoryPoint>().Header("CPU").Width(100).HeaderTextAlignment(TextAlignment.Right).Text(row => Percent(row.CpuPercent)),
            new GridViewColumn<HistoryPoint>().Header("内存").Width(100).HeaderTextAlignment(TextAlignment.Right).Text(row => Percent(row.MemoryPercent)),
            new GridViewColumn<HistoryPoint>().Header("网络").StarWidth(1, minWidth: 160).HeaderTextAlignment(TextAlignment.Right).Text(row => Rate(row.NetworkReceiveBytesPerSecond, row.NetworkSendBytesPerSecond)),
            new GridViewColumn<HistoryPoint>().Header("磁盘").StarWidth(1, minWidth: 160).HeaderTextAlignment(TextAlignment.Right).Text(row => Rate(SumKnown(row.PhysicalDisks.Select(item => item.ReadBytesPerSecond)), SumKnown(row.PhysicalDisks.Select(item => item.WriteBytesPerSecond)))));

        var graph = new Border().BorderThickness(1).CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) =>
            {
                var surface = theme.Palette.ControlBackground;
                border.Background = Color.FromArgb(theme.IsDark ? (byte)245 : (byte)250, surface.R, surface.G, surface.B);
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(new StackPanel().Vertical().Spacing(8).Children(
                new TextBlock().Text("系统资源使用情况（最近 60 秒）").FontSize(ThemeFontSize.Medium).SemiBold(),
                new TextBlock().Text("蓝色数值与历史表均来自真实 Windows 采样；预热或不可用指标显示为 —。" ).FontSize(ThemeFontSize.Small),
                ShellComponents.DataSurface(overviewPerformanceGrid)));

        var recent = new Border().BorderThickness(1).CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) =>
            {
                var surface = theme.Palette.ControlBackground;
                border.Background = Color.FromArgb(theme.IsDark ? (byte)245 : (byte)250, surface.R, surface.G, surface.B);
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(new StackPanel().Vertical().Spacing(8).Children(
                new DockPanel().Children(
                    new TextBlock().Text("最近活动").FontSize(ThemeFontSize.Medium).SemiBold(),
                    new TextBlock().Text("查看全部 →").DockRight().FontSize(ThemeFontSize.Small)),
                ShellComponents.DataSurface(overviewRecentGrid)));

        return new ScrollViewer().AutoVerticalScroll().NoHorizontalScroll().Content(
            new StackPanel().Vertical().Spacing(ShellTokens.ContentGap).Children(metrics, graph, recent));
    }

    private FrameworkElement OverviewMetric(string label, Func<string> value)
    {
        return new Border().Padding(16).BorderThickness(1).CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) =>
            {
                var surface = theme.Palette.ControlBackground;
                border.Background = Color.FromArgb(theme.IsDark ? (byte)245 : (byte)250, surface.R, surface.G, surface.B);
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(new StackPanel().Vertical().Spacing(8).Children(
                new TextBlock().Text(label).FontSize(ThemeFontSize.Small),
                LiveText(value).FontSize(ThemeFontSize.Medium).SemiBold()));
    }

    private FrameworkElement BuildProcessesPage()
    {
        var run = new Button().Content("运行新任务").OnClick(() => RunTask());
        var end = new Button().Content("结束任务").IsEnabled(false).OnClick(() => EndSelected());
        var details = new Button().Content("查看详情").IsEnabled(false);
        var refresh = new Button().Content("刷新").OnClick(Poll);
        processGrid = new GridView { ItemsSource = processItems, ZebraStriping = false, ShowGridLines = false, HeaderHeight = 40 };
        processGrid.Columns(
            new GridViewColumn<ProcessDto>().Header("名称").StarWidth(3, minWidth: 260).SortBy(row => row.Name, StringComparer.OrdinalIgnoreCase).Text(row => row.Name),
            new GridViewColumn<ProcessDto>().Header("状态").Width(100).Text(row => row.State == "running" ? "运行中" : row.State),
            new GridViewColumn<ProcessDto>().Header("CPU").Width(100).HeaderTextAlignment(TextAlignment.Right).SortBy(row => row.CpuPercent ?? double.MinValue).Text(row => Percent(row.CpuPercent)),
            new GridViewColumn<ProcessDto>().Header("内存").Width(120).HeaderTextAlignment(TextAlignment.Right).SortBy(row => row.WorkingSetBytes ?? long.MinValue).Text(row => Bytes(row.WorkingSetBytes)),
            new GridViewColumn<ProcessDto>().Header("I/O").Width(120).HeaderTextAlignment(TextAlignment.Right).Text(row => Rate(row.IoReadBytesPerSecond, row.IoWriteBytesPerSecond)),
            new GridViewColumn<ProcessDto>().Header("网络").Width(130).HeaderTextAlignment(TextAlignment.Right).Text(row => Rate(row.NetworkReceiveBytesPerSecond, row.NetworkSendBytesPerSecond)),
            new GridViewColumn<ProcessDto>().Header("GPU").Width(90).HeaderTextAlignment(TextAlignment.Right).Text(row => Percent(row.GpuPercent)),
            new GridViewColumn<ProcessDto>().Header("PID").Width(86).HeaderTextAlignment(TextAlignment.Right).SortBy(row => row.Pid).Text(row => row.Pid.ToString(CultureInfo.InvariantCulture)));
        processGrid.OnSelectionChanged(value =>
        {
            var row = value as ProcessDto;
            end.IsEnabled = row?.CanTerminate == true;
            details.IsEnabled = row is not null;
        });
        details.OnClick(() =>
        {
            if (processGrid.SelectedItem is not ProcessDto row) return;
            SelectPage(Array.IndexOf(PageKeys, "details"));
            detailGrid!.SelectedItem = row;
            if (detailGrid.SelectedIndex >= 0) detailGrid.ScrollIntoView(detailGrid.SelectedIndex);
        });
        // The global search box lives in the title bar. Keep the page command
        // row limited to actions so a MewUI element never has two parents.
        var command = new DockPanel().Padding(0, 0, 0, 12).Spacing(8).Children(
            new StackPanel().Horizontal().Spacing(8).Children(
                new TextBlock().Text("运行中的进程").SemiBold(),
                LiveText(ProcessSummary).FontSize(ThemeFontSize.Small)),
            ShellComponents.CommandBar(run, details, end, refresh).DockRight());
        return new Grid().Rows("Auto, *").Children(command, ShellComponents.DataSurface(processGrid).Row(1));
    }

    private FrameworkElement BuildPerformancePage()
    {
        performanceGrid = new GridView { ItemsSource = performanceHistoryItems, ZebraStriping = false, ShowGridLines = false, HeaderHeight = 40 };
        performanceGrid.Columns(
            new GridViewColumn<HistoryPoint>().Header("采样时间").Width(170).Text(row => row.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss")),
            new GridViewColumn<HistoryPoint>().Header("CPU").Width(90).HeaderTextAlignment(TextAlignment.Right).Text(row => Percent(row.CpuPercent)),
            new GridViewColumn<HistoryPoint>().Header("内存").Width(90).HeaderTextAlignment(TextAlignment.Right).Text(row => Percent(row.MemoryPercent)),
            new GridViewColumn<HistoryPoint>().Header("网络收 / 发").Width(150).HeaderTextAlignment(TextAlignment.Right).Text(row => Rate(row.NetworkReceiveBytesPerSecond, row.NetworkSendBytesPerSecond)),
            new GridViewColumn<HistoryPoint>().Header("磁盘读 / 写").Width(150).HeaderTextAlignment(TextAlignment.Right).Text(row => Rate(SumKnown(row.PhysicalDisks.Select(item => item.ReadBytesPerSecond)), SumKnown(row.PhysicalDisks.Select(item => item.WriteBytesPerSecond)))),
            new GridViewColumn<HistoryPoint>().Header("GPU").Width(90).HeaderTextAlignment(TextAlignment.Right).Text(row => Percent(row.GpuPercent)));

        // The reference keeps a narrow resource rail and one selected resource
        // surface.  Each card is populated from the current Windows snapshot;
        // absent values stay unknown instead of being replaced with demo data.
        var resources = new StackPanel().Vertical().Spacing(ShellTokens.CardGap).Children(
            PerformanceResourceCard("CPU", "总体使用率", () => snapshots.Current?.System.CpuPercent is { } cpu ? Percent(cpu) : "预热中 / 不可用", true),
            PerformanceResourceCard("内存", "物理内存", MemorySummary, false),
            PerformanceResourceCard("网络", "网络适配器", NetworkSummary, false),
            PerformanceResourceCard("磁盘", "物理磁盘", DiskSummary, false),
            PerformanceResourceCard("GPU", "图形处理器", GpuSummary, false),
            PerformanceResourceCard("进程", "逻辑处理器 / 进程", ProcessSummary, false));

        var current = new Border().Padding(16).BorderThickness(1).CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) =>
            {
                var surface = theme.Palette.ControlBackground;
                border.Background = Color.FromArgb(theme.IsDark ? (byte)245 : (byte)250, surface.R, surface.G, surface.B);
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(new Grid().Columns("*, Auto").Children(
                new StackPanel().Vertical().Spacing(4).Children(
                    new TextBlock().Text("CPU").FontSize(ThemeFontSize.Large).SemiBold(),
                    new TextBlock().Text("总体使用率 · 来自最近一次成功采样").FontSize(ThemeFontSize.Small)),
                new StackPanel().Vertical().Spacing(2).Children(
                    LiveText(() => Percent(snapshots.Current?.System.CpuPercent)).FontSize(ThemeFontSize.Large).SemiBold(),
                    LiveText(() => snapshots.Current is { } point ? point.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss") : "—").FontSize(ThemeFontSize.Small)).Column(1).CenterVertical()));

        var history = new Border().BorderThickness(1).CornerRadius(8)
            .WithTheme((theme, border) => { border.Background(theme.Palette.ControlBackground); border.BorderBrush(theme.Palette.ControlBorder); })
            .Child(new StackPanel().Vertical().Spacing(8).Children(
                new TextBlock().Text("真实采样历史").FontSize(ThemeFontSize.Medium).SemiBold(),
                new TextBlock().Text("空表表示尚未取得成功采样；未知指标保留为 —，不会以 0 填充。").FontSize(ThemeFontSize.Small),
                performanceGrid));
        var right = new StackPanel().Vertical().Spacing(ShellTokens.ContentGap).Children(current, history);
        return new ScrollViewer().AutoVerticalScroll().NoHorizontalScroll().Content(new StackPanel().Vertical().Spacing(ShellTokens.ContentGap).Children(
            new DockPanel().Children(
                new StackPanel().Vertical().Spacing(4).Children(
                    new TextBlock().Text("性能").FontSize(ThemeFontSize.Medium).SemiBold(),
                    new TextBlock().Text("选择资源查看真实 Windows 采样历史和设备事实").FontSize(ThemeFontSize.Small)),
                LiveText(() => snapshots.Current is null ? "预热中" : snapshots.Settings.Paused ? "已暂停" : "实时").DockRight().CenterVertical()),
            new Grid().Columns("240, *").Children(resources, right.Column(1).Margin(ShellTokens.ContentGap, 0, 0, 0))));
    }

    private FrameworkElement PerformanceResourceCard(string glyph, string title, Func<string> value, bool selected)
    {
        var card = new Border().Padding(12).BorderThickness(1).CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) =>
            {
                var surface = theme.Palette.ControlBackground;
                var alpha = selected ? (byte)250 : (byte)236;
                border.Background = Color.FromArgb(alpha, surface.R, surface.G, surface.B);
                border.BorderBrush = selected ? theme.Palette.Accent : theme.Palette.ControlBorder;
            })
            .Child(new Grid().Columns("28, *").Children(
                new TextBlock().Text(glyph).FontSize(ThemeFontSize.Medium).SemiBold().CenterVertical(),
                new StackPanel().Vertical().Spacing(2).Children(
                    new TextBlock().Text(title).SemiBold(),
                    LiveText(value).FontSize(ThemeFontSize.Small)) .Column(1).Margin(8, 0, 0, 0)));
        return card;
    }

    private FrameworkElement BuildHistoryPage()
    {
        historyGrid = new GridView { ItemsSource = historyItems, ShowGridLines = false, ZebraStriping = false, HeaderHeight = 40 };
        historyGrid.Columns(
            new GridViewColumn<ProcessHistoryDto>().Header("名称").StarWidth(3, minWidth: 260).Text(row => row.Name),
            new GridViewColumn<ProcessHistoryDto>().Header("PID").Width(90).HeaderTextAlignment(TextAlignment.Right).Text(row => row.Pid.ToString(CultureInfo.InvariantCulture)),
            new GridViewColumn<ProcessHistoryDto>().Header("状态").Width(100).Text(row => row.IsRunning ? "运行中" : "已退出"),
            new GridViewColumn<ProcessHistoryDto>().Header("首次观测").Width(180).Text(row => row.FirstObservedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
            new GridViewColumn<ProcessHistoryDto>().Header("最后观测").Width(180).Text(row => row.LastObservedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
            new GridViewColumn<ProcessHistoryDto>().Header("CPU 时间").Width(120).HeaderTextAlignment(TextAlignment.Right).Text(row => row.ObservedCpuTime100ns ?? "—"),
            new GridViewColumn<ProcessHistoryDto>().Header("I/O 读取").Width(130).HeaderTextAlignment(TextAlignment.Right).Text(row => row.ObservedIoReadBytes ?? "—"),
            new GridViewColumn<ProcessHistoryDto>().Header("I/O 写入").Width(130).HeaderTextAlignment(TextAlignment.Right).Text(row => row.ObservedIoWriteBytes ?? "—"));
        var locate = new Button().Content("定位运行进程").IsEnabled(false).OnClick(LocateHistoryProcess);
        historyGrid.OnSelectionChanged(value => locate.IsEnabled = value is ProcessHistoryDto row && row.IsRunning && FindProcess(row.Identity) is not null);
        var reset = new Button().Content("清除统计").OnClick(() =>
        {
            snapshots.ResetHistory();
            UpdateOtherRows();
            statusText!.Text = "本次会话历史已清除。";
        });
        var command = new DockPanel().Padding(0, 0, 0, 12).Spacing(8).Children(
            new StackPanel().Vertical().Spacing(4).Children(
                new TextBlock().Text("本次会话").SemiBold(),
                LiveText(() => snapshots.Current is { } current
                    ? $"会话开始：{current.SessionStartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} · 已观测 {current.History.Count.ToString(CultureInfo.InvariantCulture)} 个进程身份"
                    : "等待 Windows 首次成功采样").FontSize(ThemeFontSize.Small)),
            new StackPanel().DockRight().Horizontal().Spacing(8).Children(locate, reset));
        var emptyHint = LiveText(() => snapshots.Current is null
            ? "正在等待 Windows 首次成功采样；历史记录会在成功采样后出现。"
            : historyRows.Count == 0
                ? "本次会话尚无历史记录。清除统计后会保留新的真实采样。"
                : "历史数据来自本次会话的真实 Windows 采样。未知字段保留原因，不以 0 填充.").FontSize(ThemeFontSize.Small);
        return new Grid().Rows("Auto, Auto, *").Children(command, emptyHint.Row(1), ShellComponents.DataSurface(historyGrid).Row(2));
    }

    private FrameworkElement BuildStartupPage()
    {
        startupGrid = new GridView { ItemsSource = startupItems, ShowGridLines = false, ZebraStriping = false, HeaderHeight = 40 };
        startupGrid.Columns(
            new GridViewColumn<StartupDto>().Header("名称").StarWidth(2, minWidth: 220).Text(row => row.Name),
            new GridViewColumn<StartupDto>().Header("来源").Width(160).Text(row => row.Source),
            new GridViewColumn<StartupDto>().Header("状态").Width(140).Text(row => StartupStatus(row.Enabled)),
            new GridViewColumn<StartupDto>().Header("状态原因").StarWidth(2, minWidth: 220).Text(row => row.EnabledReason),
            new GridViewColumn<StartupDto>().Header("命令").StarWidth(3, minWidth: 320).Text(row => row.Command),
            new GridViewColumn<StartupDto>().Header("来源位置").StarWidth(2, minWidth: 240).Text(row => row.LocationPath ?? row.LocationReason ?? "不可用"));
        var openSource = new Button().Content("打开来源").IsEnabled(false).OnClick(OpenSelectedStartupLocation);
        var detailsTitle = new TextBlock().Text("选择启动项").FontSize(ThemeFontSize.Medium).SemiBold();
        var detailsStatus = new TextBlock().Text("从列表选择一个真实 Windows 启动项。").FontSize(ThemeFontSize.Small);
        var detailsSource = new TextBlock().Text("来源：—").FontSize(ThemeFontSize.Small);
        var detailsCommand = new TextBlock().Text("命令：—").FontSize(ThemeFontSize.Small);
        var detailsLocation = new TextBlock().Text("位置：—").FontSize(ThemeFontSize.Small);
        startupGrid.OnSelectionChanged(value =>
        {
            openSource.IsEnabled = value is StartupDto row && row.CanOpenLocation;
            if (value is not StartupDto selected)
            {
                detailsTitle.Text = "选择启动项";
                detailsStatus.Text = "从列表选择一个真实 Windows 启动项。";
                detailsSource.Text = "来源：—";
                detailsCommand.Text = "命令：—";
                detailsLocation.Text = "位置：—";
                return;
            }

            detailsTitle.Text = selected.Name;
            detailsStatus.Text = $"状态：{StartupStatus(selected.Enabled)} · {selected.EnabledReason}";
            detailsSource.Text = $"来源：{selected.Source}";
            detailsCommand.Text = $"命令：{selected.Command}";
            detailsLocation.Text = $"位置：{selected.LocationPath ?? selected.LocationReason ?? "不可用"}";
        });
        var startupSettings = new Button().Content("启动设置").OnClick(() => OpenSystemLocation("startupSettings"));
        var userStartup = new Button().Content("用户 Startup").OnClick(() => OpenSystemLocation("userStartup"));
        var commonStartup = new Button().Content("公共 Startup").OnClick(() => OpenSystemLocation("commonStartup"));
        var command = new DockPanel().Padding(0, 0, 0, 12).Spacing(8).Children(
            new StackPanel().Vertical().Spacing(4).Children(
                new TextBlock().Text("启动应用").SemiBold(),
                LiveText(() => $"Windows Run / RunOnce 与 Startup 文件夹 · {snapshots.Current?.StartupItems.Count ?? 0} 项").FontSize(ThemeFontSize.Small)),
            ShellComponents.CommandBar(openSource, startupSettings, userStartup, commonStartup).DockRight());
        var details = new Border().Padding(16).BorderThickness(1).CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) =>
            {
                var surface = theme.Palette.ControlBackground;
                border.Background = Color.FromArgb(theme.IsDark ? (byte)245 : (byte)250, surface.R, surface.G, surface.B);
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(new StackPanel().Vertical().Spacing(10).Children(
                detailsTitle,
                detailsStatus,
                new Border().BorderThickness(new Thickness(0, 0, 0, 1)).WithTheme((theme, border) => border.BorderBrush = theme.Palette.ControlBorder),
                detailsSource,
                detailsCommand,
                detailsLocation,
                new TextBlock().Text("状态来自 Windows 启动项读取；此页面不会写入或执行启动项。").FontSize(ThemeFontSize.Small)));
        var body = new Grid().Columns("*, 300").Children(
            ShellComponents.DataSurface(startupGrid),
            details.Column(1).Margin(ShellTokens.ContentGap, 0, 0, 0));
        return new Grid().Rows("Auto, *").Children(command, body.Row(1));
    }

    private FrameworkElement BuildUsersPage()
    {
        usersGrid = new GridView { ItemsSource = userItems, ShowGridLines = false, ZebraStriping = false, HeaderHeight = 40 };
        usersGrid.Columns(
            new GridViewColumn<UserDto>().Header("用户").StarWidth(2, minWidth: 220).Text(row => row.UserName ?? "未知用户"),
            new GridViewColumn<UserDto>().Header("会话").Width(90).HeaderTextAlignment(TextAlignment.Right).Text(row => row.SessionId.ToString(CultureInfo.InvariantCulture)),
            new GridViewColumn<UserDto>().Header("状态").Width(140).Text(row => row.State),
            new GridViewColumn<UserDto>().Header("进程").Width(100).HeaderTextAlignment(TextAlignment.Right).Text(row => row.ProcessCount.ToString(CultureInfo.InvariantCulture)),
            new GridViewColumn<UserDto>().Header("CPU").Width(100).HeaderTextAlignment(TextAlignment.Right).Text(row => Percent(row.CpuPercent)),
            new GridViewColumn<UserDto>().Header("内存").Width(120).HeaderTextAlignment(TextAlignment.Right).Text(row => Bytes(row.WorkingSetBytes)),
            new GridViewColumn<UserDto>().Header("不可读进程").Width(120).HeaderTextAlignment(TextAlignment.Right).Text(row => row.UnreadableProcessCount.ToString(CultureInfo.InvariantCulture)));
        var locate = new Button().Content("查看此会话进程").IsEnabled(false).OnClick(LocateUserProcesses);
        usersGrid.OnSelectionChanged(value => locate.IsEnabled = value is UserDto);
        var command = new DockPanel().Padding(0, 0, 0, 12).Spacing(8).Children(
            new StackPanel().Vertical().Spacing(4).Children(
                new TextBlock().Text("用户").SemiBold(),
                LiveText(() => snapshots.Current is { } current
                    ? $"Windows 登录会话 · {current.CapturedAtUtc.ToLocalTime():HH:mm:ss}"
                    : "等待 Windows 首次成功采样").FontSize(ThemeFontSize.Small)),
            ShellComponents.CommandBar(locate).DockRight());

        // The reference uses two quiet summary cards above the table. Keep their values
        // tied to the same user aggregation that feeds the grid so the summary cannot
        // drift from the rows, and preserve an explicit unknown state before sampling.
        var summary = new StackPanel().Horizontal().Spacing(ShellTokens.ContentGap).Children(
            UserMetricCard("登录会话", () => snapshots.Current is { } current
                ? current.Users.Count.ToString(CultureInfo.InvariantCulture)
                : "—", "来自当前 Windows 快照"),
            UserMetricCard("进程", () => snapshots.Current is { } current
                ? current.Users.Sum(user => user.ProcessCount).ToString(CultureInfo.InvariantCulture)
                : "—", "按可确定归属聚合"),
            UserMetricCard("不可读归属", () => snapshots.Current is { } current
                ? current.Users.Sum(user => user.UnreadableProcessCount).ToString(CultureInfo.InvariantCulture)
                : "—", "保留原因，不分配给用户"));

        var body = new StackPanel().Vertical().Spacing(ShellTokens.ContentGap).Children(
            summary,
            ShellComponents.DataSurface(usersGrid));
        return new Grid().Rows("Auto, *").Children(command, new ScrollViewer().AutoVerticalScroll().NoHorizontalScroll().Content(body).Row(1));
    }

    private FrameworkElement UserMetricCard(string title, Func<string> value, string description)
    {
        var card = new Border().Padding(16).BorderThickness(1).CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) =>
            {
                var surface = theme.Palette.ControlBackground;
                border.Background = Color.FromArgb(theme.IsDark ? (byte)245 : (byte)250, surface.R, surface.G, surface.B);
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(new StackPanel().Vertical().Spacing(4).Children(
                new TextBlock().Text(title).FontSize(ThemeFontSize.Small),
                LiveText(value).FontSize(ThemeFontSize.Large).SemiBold(),
                new TextBlock().Text(description).FontSize(ThemeFontSize.Small)));
        return card.Width(220);
    }

    private FrameworkElement BuildDetailsPage()
    {
        detailGrid = new GridView { ItemsSource = detailItems, ShowGridLines = false, ZebraStriping = false, HeaderHeight = 40 };
        detailGrid.Columns(
            new GridViewColumn<ProcessDto>().Header("名称").StarWidth(2, minWidth: 260).Text(row => row.Name),
            new GridViewColumn<ProcessDto>().Header("PID").Width(90).HeaderTextAlignment(TextAlignment.Right).Text(row => row.Pid.ToString(CultureInfo.InvariantCulture)),
            new GridViewColumn<ProcessDto>().Header("状态").Width(100).Text(row => row.State == "running" ? "运行中" : row.State),
            new GridViewColumn<ProcessDto>().Header("用户").StarWidth(1, minWidth: 160).Text(row => row.UserName ?? "未知用户"),
            new GridViewColumn<ProcessDto>().Header("CPU").Width(100).HeaderTextAlignment(TextAlignment.Right).Text(row => Percent(row.CpuPercent)),
            new GridViewColumn<ProcessDto>().Header("内存").Width(120).HeaderTextAlignment(TextAlignment.Right).Text(row => Bytes(row.WorkingSetBytes)),
            new GridViewColumn<ProcessDto>().Header("父 PID").Width(100).HeaderTextAlignment(TextAlignment.Right).Text(row => row.ParentPid.ToString(CultureInfo.InvariantCulture)),
            new GridViewColumn<ProcessDto>().Header("会话").Width(90).HeaderTextAlignment(TextAlignment.Right).Text(row => row.SessionId?.ToString(CultureInfo.InvariantCulture) ?? "—"),
            new GridViewColumn<ProcessDto>().Header("优先级").Width(120).Text(row => row.Priority ?? "—"),
            new GridViewColumn<ProcessDto>().Header("线程").Width(90).HeaderTextAlignment(TextAlignment.Right).Text(row => row.ThreadCount.ToString(CultureInfo.InvariantCulture)),
            new GridViewColumn<ProcessDto>().Header("句柄").Width(90).HeaderTextAlignment(TextAlignment.Right).Text(row => row.HandleCount?.ToString(CultureInfo.InvariantCulture) ?? "—"),
            new GridViewColumn<ProcessDto>().Header("网络").Width(130).HeaderTextAlignment(TextAlignment.Right).Text(row => Rate(row.NetworkReceiveBytesPerSecond, row.NetworkSendBytesPerSecond)),
            new GridViewColumn<ProcessDto>().Header("路径").StarWidth(3, minWidth: 360).Text(row => row.Path ?? "不可用"));
        var run = new Button().Content("运行新任务").OnClick(RunTask);
        var end = new Button().Content("结束任务").IsEnabled(false).OnClick(EndSelected);
        detailGrid.OnSelectionChanged(value => end.IsEnabled = value is ProcessDto row && row.CanTerminate);
        var command = new DockPanel().Padding(0, 0, 0, 12).Spacing(8).Children(
            new StackPanel().Vertical().Spacing(4).Children(
                new TextBlock().Text("详细信息").SemiBold(),
                LiveText(() => snapshots.Current is { } current
                    ? $"当前真实采样 · {current.Processes.Count.ToString(CultureInfo.InvariantCulture)} 个进程身份"
                    : "等待 Windows 首次成功采样").FontSize(ThemeFontSize.Small)),
            ShellComponents.CommandBar(run, end).DockRight());
        return new Grid().Rows("Auto, *").Children(command, ShellComponents.DataSurface(detailGrid).Row(1));
    }

    private FrameworkElement BuildServicesPage()
    {
        serviceGrid = new GridView { ItemsSource = serviceItems, ShowGridLines = false, ZebraStriping = false, HeaderHeight = 40 };
        serviceGrid.Columns(
            new GridViewColumn<ServiceDto>().Header("显示名称").StarWidth(2, minWidth: 280).Text(row => row.DisplayName),
            new GridViewColumn<ServiceDto>().Header("服务名").StarWidth(2, minWidth: 240).Text(row => row.Name),
            new GridViewColumn<ServiceDto>().Header("状态").Width(140).Text(ServiceStatus),
            new GridViewColumn<ServiceDto>().Header("PID").Width(90).HeaderTextAlignment(TextAlignment.Right).Text(row => row.Pid == 0 ? "—" : row.Pid.ToString(CultureInfo.InvariantCulture)));
        var start = new Button().Content("启动").IsEnabled(false).OnClick(() => ControlService("start"));
        var stop = new Button().Content("停止").IsEnabled(false).OnClick(() => ControlService("stop"));
        var restart = new Button().Content("重启").IsEnabled(false).OnClick(() => ControlService("restart"));
        var details = new Button().Content("服务详情").IsEnabled(false).OnClick(ShowServiceDetails);
        var manager = new Button().Content("服务管理器").OnClick(() => _ = OpenServiceManager());
        var detailsTitle = new TextBlock().Text("选择服务").FontSize(ThemeFontSize.Medium).SemiBold();
        var detailsStatus = new TextBlock().Text("从列表选择一个真实 Windows 服务。").FontSize(ThemeFontSize.Small);
        var detailsName = new TextBlock().Text("服务名：—").FontSize(ThemeFontSize.Small);
        var detailsPid = new TextBlock().Text("PID：—").FontSize(ThemeFontSize.Small);
        serviceGrid.OnSelectionChanged(value =>
        {
            var row = value as ServiceDto;
            details.IsEnabled = row is not null;
            start.IsEnabled = row is not null && IsServiceStopped(row.State);
            stop.IsEnabled = row is not null && IsServiceRunning(row.State);
            restart.IsEnabled = row is not null && IsServiceRunning(row.State);
            if (row is null)
            {
                detailsTitle.Text = "选择服务";
                detailsStatus.Text = "从列表选择一个真实 Windows 服务。";
                detailsName.Text = "服务名：—";
                detailsPid.Text = "PID：—";
                return;
            }

            detailsTitle.Text = row.DisplayName;
            detailsStatus.Text = $"状态：{ServiceStatus(row)}";
            detailsName.Text = $"服务名：{row.Name}";
            detailsPid.Text = $"PID：{(row.Pid == 0 ? "—" : row.Pid.ToString(CultureInfo.InvariantCulture))}";
        });
        var command = new DockPanel().Padding(0, 0, 0, 12).Spacing(8).Children(
            new StackPanel().Vertical().Spacing(4).Children(
                new TextBlock().Text("服务").SemiBold(),
                LiveText(() => snapshots.Current is { } current
                    ? $"Windows Service Control Manager · {current.Services.Count.ToString(CultureInfo.InvariantCulture)} 项"
                    : "等待 Windows 首次成功采样").FontSize(ThemeFontSize.Small)),
            ShellComponents.CommandBar(start, stop, restart, details, manager).DockRight());
        var detailCard = new Border().Padding(16).BorderThickness(1).CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) =>
            {
                var surface = theme.Palette.ControlBackground;
                border.Background = Color.FromArgb(theme.IsDark ? (byte)245 : (byte)250, surface.R, surface.G, surface.B);
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(new StackPanel().Vertical().Spacing(10).Children(
                detailsTitle,
                detailsStatus,
                new Border().BorderThickness(new Thickness(0, 0, 0, 1)).WithTheme((theme, border) => border.BorderBrush = theme.Palette.ControlBorder),
                detailsName,
                detailsPid,
                new TextBlock().Text("动作只通过 Windows SCM 执行；权限或 pending 状态会保留具体原因。").FontSize(ThemeFontSize.Small)));
        var body = new Grid().Columns("*, 320").Children(
            ShellComponents.DataSurface(serviceGrid),
            detailCard.Column(1).Margin(ShellTokens.ContentGap, 0, 0, 0));
        return new Grid().Rows("Auto, *").Children(command, body.Row(1));
    }

    private FrameworkElement BuildSettingsPage()
    {
        var interval = new ComboBox().Items(["1 秒", "2 秒", "5 秒"]).SelectedIndex(preferences.IntervalMs switch { 2000 => 1, 5000 => 2, _ => 0 }).OnSelectionChanged(value =>
        {
            if (value is string text) _ = snapshots.ApplySettings(new SettingsRequest(text.StartsWith("5") ? 5000 : text.StartsWith("2") ? 2000 : 1000, snapshots.Settings.Paused));
        });
        var theme = new ComboBox().Items(["跟随系统", "浅色", "深色"]).SelectedIndex(preferences.Theme switch { "light" => 1, "dark" => 2, _ => 0 }).OnSelectionChanged(value =>
        {
            if (value is not string text) return;
            var mode = text switch { "浅色" => "light", "深色" => "dark", _ => "system" };
            try
            {
                preferences.SaveTheme(mode);
                Application.Current.SetTheme(mode switch { "light" => ThemeVariant.Light, "dark" => ThemeVariant.Dark, _ => ThemeVariant.System });
            }
            catch (ApiException error)
            {
                if (statusText is not null) statusText.Text = error.Message;
            }
        });
        var pausedState = new ObservableValue<bool>(snapshots.Settings.Paused);
        var paused = new CheckBox().Content("暂停自动刷新").BindIsChecked(pausedState).OnCheckedChanged(value => _ = snapshots.ApplySettings(new SettingsRequest(snapshots.Settings.IntervalMs, value)));
        var density = new ComboBox().Items(["紧凑", "标准"]).SelectedIndex(preferences.Current.Preferences.Density == "standard" ? 1 : 0).OnSelectionChanged(value =>
        {
            if (value is string text) SaveUiPreferences(ui => ui with { Density = text == "标准" ? "standard" : "compact" });
        });
        var sidebarCollapsed = new CheckBox().Content("使用紧凑侧栏").BindIsChecked(new ObservableValue<bool>(preferences.Current.Preferences.SidebarCollapsed)).OnCheckedChanged(ApplySidebarCollapsed);
        var alwaysOnTop = new CheckBox().Content("始终置顶").BindIsChecked(new ObservableValue<bool>(preferences.Current.Preferences.Window?.AlwaysOnTop == true)).OnCheckedChanged(_value => { _ = ToggleAlwaysOnTop(); });
        var integrationState = integration.Read();
        integrationSummary = new TextBlock().Text(FormatIntegration(integrationState)).FontSize(ThemeFontSize.Small);
        integrationEnableButton = new Button().Content(integrationState.CanTakeOver ? "接管现有入口" : "启用入口").IsEnabled(integrationState.CanEnable || integrationState.CanTakeOver).OnClick(() => _ = ApplyIntegration(true));
        integrationDisableButton = new Button().Content("恢复标准入口").IsEnabled(integrationState.CanDisable).OnClick(() => _ = ApplyIntegration(false));
        var integrationControls = new StackPanel().Horizontal().Spacing(8).Children(
            integrationEnableButton,
            integrationDisableButton,
            new Button().Content("刷新状态").OnClick(RefreshIntegration));
        var integrationRow = new Border().Padding(16).BorderThickness(1).CornerRadius(8)
            .WithTheme((theme, border) => { border.Background = theme.Palette.ControlBackground; border.BorderBrush = theme.Palette.ControlBorder; })
            .Child(new StackPanel().Spacing(8).Children(
                new TextBlock().Text("任务管理器入口").SemiBold(),
                new TextBlock().Text("只读显示真实IFEO状态；写入需要管理员权限并在执行前重新确认版本。").FontSize(ThemeFontSize.Small),
                integrationSummary,
                integrationControls));
        return PageStack(new TextBlock().Text("设置").FontSize(ThemeFontSize.Medium).SemiBold(),
            SettingRow("采样间隔", "控制真实 Windows 采样频率。", interval),
            SettingRow("密度", "保存紧凑或标准数据行密度。", density),
            SettingRow("主题", "选择浅色、深色或跟随 Windows 系统设置。", theme),
            SettingRow("刷新状态", "暂停时保留最后成功快照并标注状态。", paused),
            SettingRow("侧栏", "折叠后保留导航图标和键盘语义。", sidebarCollapsed),
            SettingRow("窗口", "通过 Windows 读回当前置顶状态。", alwaysOnTop),
            integrationRow,
            new TextBlock().Text("Takao 1.1.1 · MewUI Win32 + Direct2D · 真实 Windows 采样、SCM、启动项与硬件来源。未知值保留原因，不以0填充。").FontSize(ThemeFontSize.Small));
    }

    private FrameworkElement BuildSystemInformationPage()
    {
        systemInfoSummary = new TextBlock().Text("正在读取 Windows 硬件概览…");
        var refresh = new Button().Content("刷新硬件信息").OnClick(() => _ = RefreshSystemInformation(true));
        systemInfoCopyButton = new Button().Content("复制").IsEnabled(false).OnClick(CopySystemInformation);
        return PageStack(new TextBlock().Text("系统信息").FontSize(ThemeFontSize.Medium).SemiBold(),
            new TextBlock().Text("硬件概览由 Windows 原生接口按需读取，不参与实时采样。"),
            new StackPanel().Horizontal().Spacing(8).Children(refresh, systemInfoCopyButton),
            systemInfoSummary,
            LiveText(() => snapshots.Current is { } current ? $"当前采样时间：{current.CapturedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}" : "当前采样时间：—"));
    }

    private async Task RefreshSystemInformation(bool refresh)
    {
        try
        {
            var info = await systemInformation.ReadAsync(refresh, CancellationToken.None);
            if (systemInfoSummary is null) return;
            systemInformationClipboard = FormatSystemInformation(info);
            systemInfoSummary.Text = systemInformationClipboard;
            if (systemInfoCopyButton is not null) systemInfoCopyButton.IsEnabled = info.Status is not "loading";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (systemInfoSummary is not null) systemInfoSummary.Text = $"硬件信息暂不可用：{error.Message}";
            if (systemInfoCopyButton is not null) systemInfoCopyButton.IsEnabled = false;
        }
    }

    private void SaveUiPreferences(Func<UiPreferences, UiPreferences> update)
    {
        try
        {
            preferences.Save(update(preferences.Current.Preferences));
            statusText!.Text = "界面偏好已保存。";
        }
        catch (ApiException error)
        {
            statusText!.Text = error.Message;
        }
    }

    private void ApplySidebarCollapsed(bool collapsed)
    {
        SaveUiPreferences(ui => ui with { SidebarCollapsed = collapsed });
        if (navigation is not null)
        {
            navigation.Width = collapsed ? 64 : 188;
            navigation.ItemPadding = collapsed ? new Thickness(8, 0) : new Thickness(12, 0);
        }
    }

    private async Task ToggleAlwaysOnTop()
    {
        try
        {
            await desktop.ExecuteAsync("always-on-top");
            statusText!.Text = preferences.Current.Preferences.Window?.AlwaysOnTop == true ? "窗口已置顶。" : "窗口已取消置顶。";
        }
        catch (ApiException error)
        {
            statusText!.Text = error.Message;
        }
    }

    private void RefreshIntegration()
    {
        try
        {
            var state = integration.Read();
            if (integrationSummary is not null) integrationSummary.Text = FormatIntegration(state);
            if (integrationEnableButton is not null)
            {
                integrationEnableButton.Content = new TextBlock().Text(state.CanTakeOver ? "接管现有入口" : "启用入口");
                integrationEnableButton.IsEnabled = state.CanEnable || state.CanTakeOver || state.CanRepair;
            }
            if (integrationDisableButton is not null) integrationDisableButton.IsEnabled = state.CanDisable;
        }
        catch (ApiException error)
        {
            if (integrationSummary is not null) integrationSummary.Text = $"入口状态暂不可用：{error.Message}";
        }
    }

    private async Task ApplyIntegration(bool enabled)
    {
        TaskManagerIntegrationState state;
        try { state = integration.Read(); }
        catch (ApiException error) { statusText!.Text = error.Message; return; }
        TaskManagerIntegrationRequest request;
        if (enabled)
        {
            if (state.CanTakeOver) request = new(true, state.Revision, TakeOver: true);
            else if (state.CanRepair) request = new(true, state.Revision, Repair: true);
            else if (state.CanEnable) request = new(true, state.Revision);
            else { statusText!.Text = state.ConflictReason ?? "当前入口状态不允许启用。"; return; }
        }
        else
        {
            if (!state.CanDisable) { statusText!.Text = state.ConflictReason ?? "当前入口不属于本目录程序，未修改Windows配置。"; return; }
            request = new(false, state.Revision);
        }
        var owner = FindVisualRoot() as Window;
        if (owner is null || !await MessageBox.ConfirmAsync(
                enabled ? "确认修改任务管理器入口？" : "确认恢复Windows标准任务管理器入口？",
                icon: PromptIconKind.Warning,
                detail: "该操作修改计算机范围的IFEO配置，并会在写入前重新验证实际状态。",
                owner: owner)) return;
        try
        {
            var result = await Task.Run(() => integration.Apply(request));
            statusText!.Text = result.Message;
            RefreshIntegration();
        }
        catch (ApiException error)
        {
            statusText!.Text = error.Message;
            RefreshIntegration();
        }
    }

    private void CopySystemInformation()
    {
        if (string.IsNullOrWhiteSpace(systemInformationClipboard)) return;
        try
        {
            if (statusText is null) return;
            var clipboard = Application.Current?.PlatformServices?.Clipboard;
            statusText.Text = clipboard is not null && clipboard.TrySetText(systemInformationClipboard)
                ? "系统信息已复制。"
                : "系统剪贴板当前不可用。";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            statusText!.Text = $"复制失败：{error.Message}";
        }
    }

    private static string FormatIntegration(TaskManagerIntegrationState state)
        => $"状态：{state.Status} · 范围：{state.Scope} · 可读：{(state.CanRead ? "是" : "否")} · 可写：{(state.CanWrite is true ? "是" : state.CanWrite is false ? "否" : "未知")}\n{state.ConflictReason ?? state.ReadError?.Message ?? "当前未接管Windows任务管理器入口。"}";

    private static string FormatSystemInformation(SystemInformationDto info)
    {
        var lines = new List<string>
        {
            $"状态：{info.Status}",
            $"采集时间：{(info.CapturedAtUtc is { } captured ? captured.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "—")}",
            "",
            "处理器",
        };
        if (info.Processors.Count == 0) lines.Add("  —");
        foreach (var cpu in info.Processors)
            lines.Add($"  {Text(cpu.Name)} · {Text(cpu.Manufacturer)} · 核心 {Text(cpu.CoreCount)} · 逻辑处理器 {Text(cpu.LogicalProcessorCount)}");
        lines.Add("显卡");
        if (info.GraphicsAdapters.Count == 0) lines.Add("  —");
        foreach (var gpu in info.GraphicsAdapters)
            lines.Add($"  {Text(gpu.Name)} · 显存 {Text(gpu.DedicatedMemoryBytes)} · {(gpu.IsSoftware ? "软件适配器" : "硬件适配器")}");
        lines.Add($"主板\n  {Text(info.Motherboard.Manufacturer)} {Text(info.Motherboard.Product)} · 版本 {Text(info.Motherboard.Version)}");
        lines.Add($"内存\n  已安装：{Text(info.Memory.InstalledBytes)} · 模块：{Text(info.Memory.ModuleCount)}");
        foreach (var module in info.Memory.Modules)
            lines.Add($"  {Text(module.DeviceLocator)} · {Text(module.Manufacturer)} · {Text(module.CapacityBytes)} · {Text(module.SpeedMtPerSecond)} MT/s");
        lines.Add("磁盘");
        if (info.Disks.Count == 0) lines.Add("  —");
        foreach (var disk in info.Disks)
            lines.Add($"  {Text(disk.Model)} · {Text(disk.MediaType)} · 总线 {Text(disk.BusType)} · 容量 {Text(disk.CapacityBytes)}");
        lines.Add("显示器");
        if (info.Displays.Count == 0) lines.Add("  —");
        foreach (var display in info.Displays)
            lines.Add($"  {Text(display.Name)} · {Text(display.WidthPixels)}×{Text(display.HeightPixels)} · {Text(display.RefreshRateHz)} Hz");
        lines.Add($"Windows\n  {Text(info.OperatingSystem.Caption)} · {Text(info.OperatingSystem.DisplayVersion)} · Build {Text(info.OperatingSystem.Build)} · {Text(info.OperatingSystem.Architecture)}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string Text<T>(T? value) where T : struct => value?.ToString() ?? "—";
    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

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
        var rows = current.Processes.AsEnumerable();
        if (processSessionFilter is { } session)
            rows = rows.Where(row => row.SessionId == session);
        if (!string.IsNullOrWhiteSpace(query))
            rows = rows.Where(row => row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Pid.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase) || (row.Publisher?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) || (row.UserName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        processRows.AddRange(rows);
        processItems.Invalidate();
        detailItems.Invalidate();
    }

    private ProcessDto? FindProcess(string identity) => snapshots.Current?.Processes.FirstOrDefault(row => row.Identity == identity);

    private void LocateHistoryProcess()
    {
        if (historyGrid?.SelectedItem is not ProcessHistoryDto history || !history.IsRunning) return;
        var process = FindProcess(history.Identity);
        if (process is null)
        {
            statusText!.Text = "该进程已退出或身份已变化，未执行定位。";
            return;
        }
        processSessionFilter = null;
        query = process.Pid.ToString(CultureInfo.InvariantCulture);
        if (searchBox is not null) searchBox.Text = query;
        UpdateProcessRows();
        SelectPage(Array.IndexOf(PageKeys, "processes"));
        processGrid!.SelectedItem = processRows.FirstOrDefault(row => row.Identity == process.Identity);
        if (processGrid.SelectedIndex >= 0) processGrid.ScrollIntoView(processGrid.SelectedIndex);
        statusText!.Text = $"已定位进程 {process.Name}（PID {process.Pid.ToString(CultureInfo.InvariantCulture)}）。";
    }

    private async void OpenSelectedStartupLocation()
    {
        if (startupGrid?.SelectedItem is not StartupDto row || !row.CanOpenLocation) return;
        try
        {
            statusText!.Text = await shell.StartupLocationAsync(row.Id);
        }
        catch (ApiException error)
        {
            statusText!.Text = error.Message;
        }
    }

    private async void OpenSystemLocation(string location)
    {
        try
        {
            statusText!.Text = await shell.OpenSystemLocationAsync(location);
        }
        catch (ApiException error)
        {
            statusText!.Text = error.Message;
        }
    }

    private async void ControlService(string command)
    {
        if (serviceGrid?.SelectedItem is not ServiceDto selected) return;
        ServiceDetails details;
        try
        {
            details = await Task.Run(() => serviceActions.Details(selected.Name));
        }
        catch (ApiException error)
        {
            statusText!.Text = error.Message;
            return;
        }
        var allowed = command switch
        {
            "start" => details.CanStart,
            "stop" => details.CanStop,
            "restart" => details.CanRestart,
            _ => false,
        };
        if (!allowed)
        {
            statusText!.Text = details.Unavailable.TryGetValue(command, out var reason)
                ? reason.Message
                : $"Windows当前不允许对服务“{details.DisplayName}”执行{command}。";
            return;
        }
        var owner = FindVisualRoot() as Window;
        if (owner is null || !await MessageBox.ConfirmAsync(
                $"确认对服务“{details.DisplayName}”执行{command}？",
                icon: PromptIconKind.Warning,
                detail: "操作将通过 Windows Service Control Manager 执行，并等待实际状态读回。",
                owner: owner)) return;
        try
        {
            var result = await serviceActions.ControlAsync(selected.Name, new ServiceControlRequest(command, details.State, details.Pid));
            statusText!.Text = result.Message;
            snapshots.RefreshSlowData();
        }
        catch (ApiException error)
        {
            statusText!.Text = error.Message;
        }
    }

    private async void ShowServiceDetails()
    {
        if (serviceGrid?.SelectedItem is not ServiceDto selected) return;
        try
        {
            var details = await Task.Run(() => serviceActions.Details(selected.Name));
            var dependencies = details.Dependencies.Count == 0 ? "—" : string.Join("、", details.Dependencies);
            statusText!.Text = $"{details.DisplayName} · 状态 {details.State} · PID {(details.Pid == 0 ? "—" : details.Pid.ToString(CultureInfo.InvariantCulture))}\n启动类型：{details.StartType ?? "不可用"} · 账户：{details.Account ?? "不可用"}\n路径：{details.BinaryPath ?? "不可用"}\n依赖：{dependencies}";
        }
        catch (ApiException error)
        {
            statusText!.Text = error.Message;
        }
    }

    private async Task OpenServiceManager()
    {
        try { statusText!.Text = await shell.OpenSystemLocationAsync("services"); }
        catch (ApiException error) { statusText!.Text = error.Message; }
    }

    private void LocateUserProcesses()
    {
        if (usersGrid?.SelectedItem is not UserDto user) return;
        processSessionFilter = user.SessionId;
        query = string.Empty;
        if (searchBox is not null) searchBox.Text = string.Empty;
        UpdateProcessRows();
        SelectPage(Array.IndexOf(PageKeys, "processes"));
        var first = processRows.FirstOrDefault();
        if (first is not null)
        {
            processGrid!.SelectedItem = first;
            if (processGrid.SelectedIndex >= 0) processGrid.ScrollIntoView(processGrid.SelectedIndex);
            statusText!.Text = $"已定位会话 {user.SessionId.ToString(CultureInfo.InvariantCulture)} 的进程（{processRows.Count.ToString(CultureInfo.InvariantCulture)} 个）。";
        }
        else
        {
            statusText!.Text = $"会话 {user.SessionId.ToString(CultureInfo.InvariantCulture)} 当前没有可读进程。";
        }
    }

    private void UpdateOtherRows()
    {
        var current = snapshots.Current;
        if (current is null) return;
        historyRows.Clear(); historyRows.AddRange(current.History); historyItems.Invalidate();
        startupRows.Clear(); startupRows.AddRange(current.StartupItems); startupItems.Invalidate();
        userRows.Clear(); userRows.AddRange(current.Users); userItems.Invalidate();
        serviceRows.Clear(); serviceRows.AddRange(current.Services); serviceItems.Invalidate();
        performanceHistoryRows.Clear(); performanceHistoryRows.AddRange(current.PerformanceHistory); performanceHistoryItems.Invalidate();
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
        var row = processGrid?.SelectedItem as ProcessDto ?? detailGrid?.SelectedItem as ProcessDto;
        if (row is null || !row.CanTerminate || row.CreationTimeFileTime is not { } creation)
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

    private static string StartupStatus(bool? enabled) => enabled switch
    {
        true => "● 已启用",
        false => "● 已禁用",
        _ => "? 无法确定",
    };

    private static string ServiceStatus(ServiceDto service)
    {
        var state = service.State?.Trim();
        if (string.IsNullOrWhiteSpace(state)) return "? 无法确定";
        if (state.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)) return "● 运行中";
        if (state.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)) return "○ 已停止";
        if (state.Contains("PENDING", StringComparison.OrdinalIgnoreCase)) return $"◌ {state}";
        return $"? {state}";
    }

    private static bool IsServiceRunning(string? state)
        => state?.Contains("RUNNING", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsServiceStopped(string? state)
        => state?.Contains("STOPPED", StringComparison.OrdinalIgnoreCase) == true;

    private static string Bytes(long? value) => value is { } bytes && bytes >= 0 ? FormatBytes(bytes) : "—";
    private static string Rate(double? read, double? write) => read is { } r && write is { } w ? $"{FormatBytes(r)}/s · {FormatBytes(w)}/s" : "—";
    private string ProcessSummary()
    {
        var current = snapshots.Current;
        return current is null
            ? "进程表：等待 Windows 首次成功采样"
            : $"进程 {current.System.ProcessCount.ToString(CultureInfo.InvariantCulture)} · CPU {Percent(current.System.CpuPercent)} · 内存 {Percent(current.System.MemoryPercent)} · 网络 {Rate(current.System.NetworkReceiveBytesPerSecond, current.System.NetworkSendBytesPerSecond)}";
    }

    private string MemorySummary()
    {
        var system = snapshots.Current?.System;
        if (system is null) return "等待 Windows 首次成功采样";
        var used = Bytes(system.UsedPhysicalBytes);
        var total = Bytes(system.TotalPhysicalBytes);
        return $"{used} / {total}（{Percent(system.MemoryPercent)}）";
    }

    private string NetworkSummary()
    {
        var current = snapshots.Current;
        if (current is null) return "等待 Windows 首次成功采样";
        return $"{current.System.NetworkInterfaceCount.ToString(CultureInfo.InvariantCulture)} 个 · {Rate(current.System.NetworkReceiveBytesPerSecond, current.System.NetworkSendBytesPerSecond)}";
    }

    private string DiskSummary()
    {
        var current = snapshots.Current;
        if (current is null) return "等待 Windows 首次成功采样";
        var identity = current.PhysicalDisks.Count == 0 ? "不可用" : string.Join("、", current.PhysicalDisks.Take(3).Select(row => row.Name));
        return $"{current.PhysicalDisks.Count.ToString(CultureInfo.InvariantCulture)} 个 · {Rate(current.System.DiskReadBytesPerSecond, current.System.DiskWriteBytesPerSecond)}\n{identity}";
    }

    private string GpuSummary()
    {
        var current = snapshots.Current;
        if (current is null) return "等待 Windows 首次成功采样";
        var names = current.GpuAdapters.Count == 0 ? "不可用" : string.Join("、", current.GpuAdapters.Take(3).Select(row => row.Description ?? row.Id));
        return $"{current.GpuAdapters.Count.ToString(CultureInfo.InvariantCulture)} 个 · {Percent(current.System.GpuPercent)}\n{names}";
    }

    private static double? SumKnown(IEnumerable<double?> values)
    {
        var known = values.Where(value => value is { } number && double.IsFinite(number)).Select(value => value!.Value).ToArray();
        return known.Length == 0 ? null : known.Sum();
    }

    private static string FormatBytes(double bytes)
    {
        if (!double.IsFinite(bytes) || bytes < 0) return "—";
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var index = 0;
        while (bytes >= 1024 && index < units.Length - 1) { bytes /= 1024; index++; }
        return $"{bytes:0.0} {units[index]}";
    }

    private static Color Glass(Color baseColor, bool dark)
    {
        var alpha = (byte)(dark ? 222 : 232);
        return Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
    }
}
