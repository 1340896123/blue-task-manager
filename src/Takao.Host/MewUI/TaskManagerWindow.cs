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

    public TaskManagerWindow(SnapshotService snapshots, PreferencesService preferences, ProcessActionService actions, SystemInformationService systemInformation, ShellActionService shell, ServiceActionService serviceActions, DesktopWindowService desktop, TaskManagerIntegrationService integration, ProcessIconService processIcons)
    {
        view = new TaskManagerView(snapshots, preferences, actions, systemInformation, shell, serviceActions, desktop, integration, processIcons);
        Title = "Takao";
        // Keep the native window icon in sync with the branded icon shown in
        // the custom title bar. The transparent borderless window does not
        // inherit the executable icon automatically on every backend.
        var iconPath = Path.Combine(AppContext.BaseDirectory, "branding", "takao.ico");
        if (File.Exists(iconPath)) Icon = IconSource.FromFile(iconPath);
        // MewUI's default Window style supplies an opaque theme background.
        // Keep the native surface transparent so the shell's acrylic layers
        // and the DWM backdrop remain visible on every page.
        Background = Color.Transparent;
        Padding = Thickness.Zero;
        // Borderless windows already use the full client surface. Extending the
        // client area again offsets the custom title bar outside the Win32
        // surface, so keep the two modes mutually exclusive.
        this.Fixed(1100, 720)
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
    private static readonly NavigationEntry[] MainNavigationEntries =
        NavigationEntries.Where(item => item.Key != "settings").ToArray();
    private static readonly NavigationEntry[] FooterNavigationEntries =
        NavigationEntries.Where(item => item.Key == "settings").ToArray();
    private readonly SnapshotService snapshots;
    private readonly PreferencesService preferences;
    private readonly ProcessActionService actions;
    private readonly SystemInformationService systemInformation;
    private readonly ShellActionService shell;
    private readonly ServiceActionService serviceActions;
    private readonly DesktopWindowService desktop;
    private readonly TaskManagerIntegrationService integration;
    private readonly ProcessIconService processIcons;
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
    private readonly List<(TextBlock Block, Func<string> Get)> processInspectorLiveText = [];
    private GridView? processGrid;
    private GridView? performanceGrid;
    private GridView? historyGrid;
    private GridView? startupGrid;
    private GridView? usersGrid;
    private GridView? detailGrid;
    private GridView? serviceGrid;
    private TransitionContentControl? processInspectorTransition;
    private Button? processInspectorEndButton;
    private NavigationList? navigation;
    private NavigationList? footerNavigation;
    private TextBox? searchBox;
    private TextBlock? cpuSummary;
    private TextBlock? memorySummary;
    private TextBlock? processSummary;
    private TextBlock? statusText;
    private TextBlock? pageTitle;
    private TextBlock? systemInfoSummary;
    private TextBlock? systemInfoProcessors;
    private TextBlock? systemInfoGraphics;
    private TextBlock? systemInfoMotherboard;
    private TextBlock? systemInfoMemory;
    private TextBlock? systemInfoDisks;
    private TextBlock? systemInfoDisplays;
    private TextBlock? systemInfoWindows;
    private TextBlock? overviewSystemInfoProcessors;
    private TextBlock? overviewSystemInfoGraphics;
    private TextBlock? overviewSystemInfoMotherboard;
    private TextBlock? overviewSystemInfoMemory;
    private TextBlock? overviewSystemInfoDisks;
    private TextBlock? overviewSystemInfoDisplays;
    private TextBlock? overviewSystemInfoWindows;
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
    private ProcessDto? selectedProcess;
    private readonly ObservableValue<string> search = new(string.Empty);
    private readonly List<(TextBlock Block, Func<string> Get)> liveText = [];
    private readonly Dictionary<string, ImageSource?> iconSources = new(StringComparer.Ordinal);
    private readonly HashSet<string> iconLoads = new(StringComparer.Ordinal);
    private bool iconLoadingStopped;

    public TaskManagerView(SnapshotService snapshots, PreferencesService preferences, ProcessActionService actions, SystemInformationService systemInformation, ShellActionService shell, ServiceActionService serviceActions, DesktopWindowService desktop, TaskManagerIntegrationService integration, ProcessIconService processIcons)
    {
        this.snapshots = snapshots;
        this.preferences = preferences;
        this.actions = actions;
        this.systemInformation = systemInformation;
        this.shell = shell;
        this.serviceActions = serviceActions;
        this.desktop = desktop;
        this.integration = integration;
        this.processIcons = processIcons;
        // Keep a local inherited value on the page host as well as the theme
        // default so every page and generated row uses the bundled font.
        FontFamily = AppTypography.DefaultFontFamily;
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

    public void Stop()
    {
        iconLoadingStopped = true;
        timer?.Stop();
    }

    protected override Element OnBuild()
    {
        var root = ShellComponents.WindowShell(BuildTopBar(), BuildSidebar(), BuildContent());
        root.OnMouseDown(HandleWindowMouseDown);
        return root;
    }

    private FrameworkElement BuildTopBar()
    {
        searchBox = new TextBox
        {
            Width = 300,
            MinWidth = 240,
            MaxWidth = 480,
            Height = 36,
            VerticalAlignment = VerticalAlignment.Center,
        }
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

        var brandIcon = new Image
        {
            Width = 20,
            Height = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Source = ImageSource.FromFile(Path.Combine(AppContext.BaseDirectory, "branding", "takao-256.png")),
        };
        var brand = new StackPanel().Horizontal().Spacing(8).Width(204).Children(
            brandIcon,
            new TextBlock().Text("Takao").FontSize(ThemeFontSize.Medium).SemiBold().CenterVertical())
            .OnMouseDown(HandleTitleBarMouseDown)
            .OnMouseDoubleClick(HandleTitleBarDoubleClick);
        var summary = new StackPanel().Horizontal().Spacing(12).Children(cpuSummary, memorySummary, processSummary)
            .OnMouseDown(HandleTitleBarMouseDown)
            .OnMouseDoubleClick(HandleTitleBarDoubleClick);
        var minimize = WindowButton("\uE921", "最小化窗口", owner => owner.Minimize());
        maximizeButton = WindowButton("\uE922", "最大化窗口", _ => ToggleMaximize());
        var close = WindowButton("\uE8BB", "关闭窗口", owner => owner.Close());
        var windowButtons = new StackPanel().Horizontal().Spacing(0).Children(minimize, maximizeButton, close);
        return ShellComponents.TitleBar(
            brand,
            searchBox.Margin(0, 0, 16, 0),
            summary.Margin(0, 0, ShellTokens.TitleBarSummaryGap, 0),
            new Border { Background = Color.Transparent }
                .OnMouseDown(args =>
                {
                    if (args.Button != MouseButton.Left) return;
                    BeginDragMove();
                    args.Handled = true;
                })
                .OnMouseDoubleClick(args =>
                {
                    if (args.Button != MouseButton.Left) return;
                    ToggleMaximize();
                    args.Handled = true;
                }),
            windowButtons);
    }

    private Button WindowButton(string glyph, string label, Action<Window> action)
    {
        var mouseFallbackPending = false;
        var button = new Button().Content(WindowGlyph(glyph)).Width(46).Height(48).ToolTip(label).OnMouseDown(_ => TraceInput($"down:{label}"));
        button.OnMouseUp(args =>
        {
            TraceInput($"up:{label}");
            var position = args.GetPosition(button);
            if (args.Button != MouseButton.Left || position.X < 0 || position.Y < 0
                || position.X >= button.RenderSize.Width || position.Y >= button.RenderSize.Height) return;

            // MewUI 0.21.x can miss pointer-release activation for controls in a
            // custom title bar. Evaluate the release in the button's own local
            // coordinate space, while keeping Click for keyboard/future backends.
            mouseFallbackPending = true;
            void InvokeAction()
            {
                if (!mouseFallbackPending) return;
                mouseFallbackPending = false;
                if (FindVisualRoot() is Window owner) action(owner);
            }

            var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
            if (dispatcher is null) InvokeAction();
            else dispatcher.BeginInvoke(InvokeAction);
        });
        button.OnClick(() =>
        {
            TraceInput($"click:{label}");
            // If Button raised Click for this pointer release, the deferred
            // compatibility fallback has already been superseded.
            if (mouseFallbackPending)
            {
                mouseFallbackPending = false;
                return;
            }
            if (FindVisualRoot() is Window owner) action(owner);
        });
        button.StyleName = BuiltInStyles.FlatButton;
        button.Padding = Thickness.Zero;
        button.CornerRadius = 0;
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.VerticalAlignment = VerticalAlignment.Center;
        return button;
    }

    private static void TraceInput(string message)
    {
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "takao-input.log"), $"{DateTime.UtcNow:O} {message}{Environment.NewLine}"); } catch { }
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
        var text = new TextBlock().Text(glyph).FontSize(16).CenterVertical();
        text.FontFamily = AppTypography.IconFontFamily;
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        return text;
    }

    private void BeginDragMove()
    {
        if (FindVisualRoot() is Window owner) owner.DragMove();
    }

    private void HandleTitleBarMouseDown(MouseEventArgs args)
    {
        if (args.Button != MouseButton.Left) return;
        BeginDragMove();
        args.Handled = true;
    }

    private void HandleTitleBarDoubleClick(MouseEventArgs args)
    {
        if (args.Button != MouseButton.Left) return;
        ToggleMaximize();
        args.Handled = true;
    }

    private void ToggleMaximize()
    {
        if (FindVisualRoot() is not Window owner || !owner.CanMaximize) return;
        if (owner.WindowState == WindowState.Maximized) owner.Restore();
        else owner.Maximize();
    }

    private void HandleWindowMouseDown(MouseEventArgs args)
    {
        if (FindVisualRoot() is not Window owner) return;
        var windowSize = owner.WindowSize;
        if (owner.WindowState == WindowState.Maximized || (windowSize.MinWidth == windowSize.MaxWidth && windowSize.MinHeight == windowSize.MaxHeight)) return;
        var size = owner.ClientSize;
        // MouseEventArgs.ScreenPosition is in device pixels while the window
        // geometry is expressed in DIPs. Convert through the window backend so
        // resize hit testing remains correct at 125%/150% display scaling.
        var clientPoint = owner.ScreenToClient(args.ScreenPosition);
        var x = clientPoint.X;
        var y = clientPoint.Y;
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
        navigation = BuildNavigation(MainNavigationEntries, selectedIndex: 0);
        footerNavigation = BuildNavigation(FooterNavigationEntries, selectedIndex: -1);

        return ShellComponents.Sidebar(navigation, footerNavigation);
    }

    private NavigationList BuildNavigation(IReadOnlyList<NavigationEntry> entries, int selectedIndex)
    {
        return new NavigationList
        {
            Background = Color.Transparent,
            ItemHeight = ShellTokens.NavigationItemHeight,
            ItemPadding = new Thickness(12, 0),
            SelectedIndex = selectedIndex,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        }
            .Items(
                entries,
                _ => new StackPanel().Horizontal().Spacing(12).CenterVertical().Children(
                    new TextBlock().FontSize(ThemeFontSize.Medium).CenterVertical(),
                    new TextBlock().CenterVertical()),
                (element, item, _, _) =>
                {
                    var row = (StackPanel)element;
                    var icon = (TextBlock)row.Children[0];
                    var title = (TextBlock)row.Children[1];
                    icon.Text = item.Glyph;
                    icon.FontFamily = AppTypography.IconFontFamily;
                    title.Text = item.Title;
                },
                _ => NavigationItemKind.Item,
                item => item.Key)
            .OnSelectionChanged(value =>
            {
                if (value is not NavigationEntry entry) return;
                var index = Array.IndexOf(PageKeys, entry.Key);
                if (index >= 0) SelectPage(index);
            });
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

    private GridViewColumn<TItem> ProcessNameColumn<TItem>(string header, string iconKind, Func<TItem, string> idSelector, Func<TItem, string> nameSelector)
    {
        return new GridViewColumn<TItem>().Header(header).Template(
            context =>
            {
                var fallback = new TextBlock()
                    .Text(ProcessFallbackGlyph(iconKind))
                    .FontFamily(AppTypography.IconFontFamily)
                    .FontSize(15)
                    .CenterHorizontal()
                    .CenterVertical()
                    .Register(context, "Fallback");
                var icon = new Image()
                    .Width(20)
                    .Height(20)
                    .CenterHorizontal()
                    .CenterVertical()
                    .Register(context, "Icon");
                var iconCell = new Grid().Width(24).Height(24).Children(fallback, icon).CenterVertical();
                var name = new TextBlock()
                    .CenterVertical()
                    .TextTrimming(TextTrimming.CharacterEllipsis)
                    .Register(context, "Name");
                return new Grid().Columns("24, *").Children(iconCell, name.Column(1).Margin(8, 0, 0, 0));
            },
            (Grid _, TItem row, int _, TemplateContext context) =>
            {
                var source = GetIconSource(iconKind, idSelector(row));
                var icon = context.Get<Image>("Icon");
                var fallback = context.Get<TextBlock>("Fallback");
                icon.Source = source;
                icon.IsVisible = source is not null;
                fallback.IsVisible = source is null;
                context.Get<TextBlock>("Name").Text = nameSelector(row) ?? string.Empty;
            });
    }

    private static string ProcessFallbackGlyph(string iconKind) => iconKind switch
    {
        "startup" => "\uE713",
        "service" => "\uE77B",
        _ => "\uE7F4",
    };

    private ImageSource? GetIconSource(string kind, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var key = string.Concat(kind, '\0', id);
        if (iconSources.TryGetValue(key, out var source)) return source;
        if (Application.IsRunning && iconLoads.Add(key)) _ = LoadIconAsync(kind, id, key);
        return null;
    }

    private async Task LoadIconAsync(string kind, string id, string key)
    {
        var snapshot = snapshots.Current;
        if (snapshot is null) return;

        byte[]? png = null;
        try
        {
            png = await processIcons.ReadApplicationAsync(kind, id, snapshot).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The fallback Fluent glyph remains visible when Windows cannot expose
            // a resource (protected process, missing executable, or inaccessible service).
        }

        if (iconLoadingStopped || !Application.IsRunning) return;
        IDispatcher? dispatcher;
        try { dispatcher = Application.Current.Dispatcher; }
        catch (InvalidOperationException) { return; }
        if (dispatcher is null) return;
        dispatcher.BeginInvoke(() =>
        {
            if (iconLoadingStopped) return;
            ImageSource? source = null;
            try
            {
                if (png is { Length: > 0 }) source = ImageSource.FromBytes(png);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                source = null;
            }
            iconSources[key] = source;
            InvalidateIconRows();
        });
    }

    private void InvalidateIconRows()
    {
        processItems.Invalidate();
        detailItems.Invalidate();
        historyItems.Invalidate();
        startupItems.Invalidate();
        serviceItems.Invalidate();
    }

    // MewUI's GridView row is an ICommandArgumentSource, so putting one menu
    // on the realized row lets every command receive the exact item that was
    // right-clicked, including virtualized and recycled rows.
    private void ConfigureProcessContextMenu(GridView grid)
    {
        var details = new Command("takao.process.details", "查看详细信息");
        var end = new Command("takao.process.end", "结束任务");
        var openLocation = new Command("takao.process.openLocation", "打开文件位置");
        var copyName = new Command("takao.process.copyName", "复制进程名称");
        var copyPid = new Command("takao.process.copyPid", "复制 PID");
        var copyPath = new Command("takao.process.copyPath", "复制文件路径");
        var menu = new ContextMenu()
            .Item(details)
            .Item(end)
            .Item(openLocation)
            .Separator()
            .Item(copyName)
            .Item(copyPid)
            .Item(copyPath);

        grid.Commands.Register(details, (ProcessDto row) => ShowProcessDetails(row));
        grid.Commands.Register(end, (ProcessDto row) => EndSelected(row), (ProcessDto row) => row.CanTerminate);
        grid.Commands.Register(openLocation, (ProcessDto row) => OpenProcessLocation(row),
            (ProcessDto row) => !string.IsNullOrWhiteSpace(row.Path) && row.CreationTimeFileTime is not null);
        grid.Commands.Register(copyName, (ProcessDto row) => CopyToClipboard(row.Name, $"已复制进程名称：{row.Name}"));
        grid.Commands.Register(copyPid, (ProcessDto row) => CopyToClipboard(row.Pid.ToString(CultureInfo.InvariantCulture), $"已复制 PID {row.Pid.ToString(CultureInfo.InvariantCulture)}。"));
        grid.Commands.Register(copyPath, (ProcessDto row) => CopyToClipboard(row.Path!, "已复制进程文件路径。"),
            (ProcessDto row) => !string.IsNullOrWhiteSpace(row.Path));
        grid.PrepareContainer<ProcessDto>((row, _, _, _) => row.ContextMenu = menu);
    }

    private void ConfigurePerformanceContextMenu(GridView grid)
    {
        var copySample = new Command("takao.performance.copySample", "复制采样数据");
        var copyTime = new Command("takao.performance.copyTime", "复制采样时间");
        var menu = new ContextMenu().Item(copySample).Item(copyTime);
        grid.Commands.Register(copySample, (HistoryPoint row) => CopyToClipboard(FormatPerformanceRow(row), "已复制采样数据。"));
        grid.Commands.Register(copyTime, (HistoryPoint row) => CopyToClipboard(row.CapturedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), "已复制采样时间。"));
        grid.PrepareContainer<HistoryPoint>((row, _, _, _) => row.ContextMenu = menu);
    }

    private void ConfigureHistoryContextMenu(GridView grid)
    {
        var locate = new Command("takao.history.locate", "定位运行进程");
        var copyName = new Command("takao.history.copyName", "复制进程名称");
        var copyPid = new Command("takao.history.copyPid", "复制 PID");
        var menu = new ContextMenu().Item(locate).Separator().Item(copyName).Item(copyPid);
        grid.Commands.Register(locate, (ProcessHistoryDto row) => LocateHistoryProcess(row),
            (ProcessHistoryDto row) => row.IsRunning && FindProcess(row.Identity) is not null);
        grid.Commands.Register(copyName, (ProcessHistoryDto row) => CopyToClipboard(row.Name, $"已复制进程名称：{row.Name}"));
        grid.Commands.Register(copyPid, (ProcessHistoryDto row) => CopyToClipboard(row.Pid.ToString(CultureInfo.InvariantCulture), $"已复制 PID {row.Pid.ToString(CultureInfo.InvariantCulture)}。"));
        grid.PrepareContainer<ProcessHistoryDto>((row, _, _, _) => row.ContextMenu = menu);
    }

    private void ConfigureStartupContextMenu(GridView grid)
    {
        var openSource = new Command("takao.startup.openSource", "打开来源");
        var copyCommand = new Command("takao.startup.copyCommand", "复制启动命令");
        var copyLocation = new Command("takao.startup.copyLocation", "复制来源位置");
        var menu = new ContextMenu().Item(openSource).Separator().Item(copyCommand).Item(copyLocation);
        grid.Commands.Register(openSource, (StartupDto row) => OpenSelectedStartupLocation(row), (StartupDto row) => row.CanOpenLocation);
        grid.Commands.Register(copyCommand, (StartupDto row) => CopyToClipboard(row.Command, "已复制启动命令。"));
        grid.Commands.Register(copyLocation, (StartupDto row) => CopyToClipboard(row.LocationPath ?? row.LocationReason ?? "不可用", "已复制启动项来源位置。"));
        grid.PrepareContainer<StartupDto>((row, _, _, _) => row.ContextMenu = menu);
    }

    private void ConfigureUsersContextMenu(GridView grid)
    {
        var locate = new Command("takao.users.locateProcesses", "查看此会话进程");
        var copyUser = new Command("takao.users.copyUser", "复制用户名称");
        var copySession = new Command("takao.users.copySession", "复制会话 ID");
        var menu = new ContextMenu().Item(locate).Separator().Item(copyUser).Item(copySession);
        grid.Commands.Register(locate, (UserDto row) => LocateUserProcesses(row));
        grid.Commands.Register(copyUser, (UserDto row) => CopyToClipboard(row.UserName ?? row.StationName, "已复制用户名称。"));
        grid.Commands.Register(copySession, (UserDto row) => CopyToClipboard(row.SessionId.ToString(CultureInfo.InvariantCulture), "已复制会话 ID。"));
        grid.PrepareContainer<UserDto>((row, _, _, _) => row.ContextMenu = menu);
    }

    private void ConfigureServiceContextMenu(GridView grid)
    {
        var start = new Command("takao.service.start", "启动服务");
        var stop = new Command("takao.service.stop", "停止服务");
        var restart = new Command("takao.service.restart", "重启服务");
        var details = new Command("takao.service.details", "查看服务详情");
        var copyName = new Command("takao.service.copyName", "复制服务名");
        var menu = new ContextMenu().Item(start).Item(stop).Item(restart).Item(details).Separator().Item(copyName);
        grid.Commands.Register(start, (ServiceDto row) => ControlService("start", row), (ServiceDto row) => IsServiceStopped(row.State));
        grid.Commands.Register(stop, (ServiceDto row) => ControlService("stop", row), (ServiceDto row) => IsServiceRunning(row.State));
        grid.Commands.Register(restart, (ServiceDto row) => ControlService("restart", row), (ServiceDto row) => IsServiceRunning(row.State));
        grid.Commands.Register(details, (ServiceDto row) => ShowServiceDetails(row));
        grid.Commands.Register(copyName, (ServiceDto row) => CopyToClipboard(row.Name, "已复制服务名。"));
        grid.PrepareContainer<ServiceDto>((row, _, _, _) => row.ContextMenu = menu);
    }

    private static string FormatPerformanceRow(HistoryPoint row)
        => string.Join("\t", row.CapturedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Percent(row.CpuPercent), Percent(row.MemoryPercent),
            Rate(row.NetworkReceiveBytesPerSecond, row.NetworkSendBytesPerSecond),
            Rate(SumKnown(row.PhysicalDisks.Select(item => item.ReadBytesPerSecond)), SumKnown(row.PhysicalDisks.Select(item => item.WriteBytesPerSecond))),
            Percent(row.GpuPercent));

    private FrameworkElement BuildOverviewPage()
    {
        // Keep the overview focused on the live summary and the same hardware
        // cards shown on the dedicated system information page.
        var metrics = new Grid().Columns("*, *, *, *, *").Children(
            OverviewMetric("CPU", () => Percent(snapshots.Current?.System.CpuPercent)),
            OverviewMetric("内存", () => Percent(snapshots.Current?.System.MemoryPercent)),
            OverviewMetric("进程", () => snapshots.Current?.System.ProcessCount.ToString(CultureInfo.InvariantCulture) ?? "—"),
            OverviewMetric("磁盘", () => Percent(snapshots.Current?.System.DiskActivePercent)),
            OverviewMetric("网络", () => Rate(snapshots.Current?.System.NetworkReceiveBytesPerSecond, snapshots.Current?.System.NetworkSendBytesPerSecond)));

        overviewSystemInfoProcessors = NewSystemInfoDetailsBlock();
        overviewSystemInfoGraphics = NewSystemInfoDetailsBlock();
        overviewSystemInfoMotherboard = NewSystemInfoDetailsBlock();
        overviewSystemInfoMemory = NewSystemInfoDetailsBlock();
        overviewSystemInfoDisks = NewSystemInfoDetailsBlock();
        overviewSystemInfoDisplays = NewSystemInfoDetailsBlock();
        overviewSystemInfoWindows = NewSystemInfoDetailsBlock();
        var systemInfoCards = BuildSystemInformationCards(
            overviewSystemInfoProcessors,
            overviewSystemInfoGraphics,
            overviewSystemInfoMotherboard,
            overviewSystemInfoMemory,
            overviewSystemInfoDisks,
            overviewSystemInfoDisplays,
            overviewSystemInfoWindows);

        return new StackPanel().Vertical().Spacing(ShellTokens.ContentGap).Children(metrics, systemInfoCards);
    }

    private FrameworkElement OverviewMetric(string label, Func<string> value)
    {
        return new Border().Padding(8, 10).BorderThickness(1).CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) =>
            {
                var surface = theme.Palette.ControlBackground;
                border.Background = Color.FromArgb(ShellTokens.SurfaceAlpha, surface.R, surface.G, surface.B);
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
        processGrid = new GridView
        {
            Background = Color.Transparent,
            ItemsSource = processItems,
            ZebraStriping = false,
            ShowGridLines = false,
            HeaderHeight = 40,
            RowHeight = 40,
            CellPadding = new Thickness(8, 4),
        };
        processGrid.Columns(
            ProcessNameColumn<ProcessDto>("名称", "process", row => row.Identity, row => row.Name).StarWidth(3).SortBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
            new GridViewColumn<ProcessDto>().Header("状态").Width(100).Text(row => row.State == "running" ? "运行中" : row.State),
            new GridViewColumn<ProcessDto>().Header("CPU").Width(100).HeaderTextAlignment(TextAlignment.Left).SortBy(row => row.CpuPercent ?? double.MinValue).Text(row => Percent(row.CpuPercent)),
            new GridViewColumn<ProcessDto>().Header("内存").Width(120).HeaderTextAlignment(TextAlignment.Left).SortBy(row => row.WorkingSetBytes ?? long.MinValue).Text(row => Bytes(row.WorkingSetBytes)),
            new GridViewColumn<ProcessDto>().Header("I/O").Width(120).HeaderTextAlignment(TextAlignment.Left).Text(row => Rate(row.IoReadBytesPerSecond, row.IoWriteBytesPerSecond)),
            new GridViewColumn<ProcessDto>().Header("网络").Width(130).HeaderTextAlignment(TextAlignment.Left).Text(row => Rate(row.NetworkReceiveBytesPerSecond, row.NetworkSendBytesPerSecond)),
            new GridViewColumn<ProcessDto>().Header("GPU").Width(90).HeaderTextAlignment(TextAlignment.Left).Text(row => Percent(row.GpuPercent)),
            new GridViewColumn<ProcessDto>().Header("PID").Width(86).HeaderTextAlignment(TextAlignment.Left).SortBy(row => row.Pid).Text(row => row.Pid.ToString(CultureInfo.InvariantCulture)));
        ConfigureProcessContextMenu(processGrid);
        processGrid.OnSelectionChanged(value =>
        {
            var row = value as ProcessDto;
            end.IsEnabled = row?.CanTerminate == true;
            details.IsEnabled = row is not null;
            SetSelectedProcess(row);
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
        var command = new DockPanel().LastChildFill(false).Padding(0, 0, 0, 12).Spacing(8).Children(
            new StackPanel().Vertical().Spacing(4).Children(
                new TextBlock().Text("运行中的进程").SemiBold(),
                LiveText(ProcessSummary).FontSize(ThemeFontSize.Small)),
            ShellComponents.CommandBar(run, details, end, refresh).DockRight());
        processInspectorTransition = new TransitionContentControl
        {
            Transition = ContentTransition.CreateSlide(SlideDirection.Left, 220, 0, Aprillz.MewUI.Animation.Easing.Default),
            Content = BuildProcessInspectorEmpty(),
        };
        var inspector = new Border().Padding(16).BorderThickness(1).CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) =>
            {
                var surface = theme.Palette.ControlBackground;
                border.Background = Color.FromArgb(ShellTokens.SurfaceAlpha, surface.R, surface.G, surface.B);
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(new Grid().Rows("Auto, *").Children(
                new StackPanel().Vertical().Spacing(2).Children(
                    new TextBlock().Text("进程详情").FontSize(ThemeFontSize.Medium).SemiBold(),
                    new TextBlock().Text("选择列表中的进程查看实时信息").FontSize(ThemeFontSize.Small)),
                processInspectorTransition.Row(1)));
        var body = new Grid().Columns("*, 320").Spacing(ShellTokens.ContentGap).Children(
            ShellComponents.DataSurface(processGrid),
            inspector.Column(1));
        return new Grid().Rows("Auto, *").Children(command, body.Row(1));
    }

    private FrameworkElement BuildProcessInspectorEmpty()
    {
        processInspectorLiveText.Clear();
        return new StackPanel().Vertical().Spacing(10).Center().Children(
            new TextBlock().Text("\uE7F4").FontFamily(AppTypography.IconFontFamily).FontSize(30).CenterHorizontal(),
            new TextBlock().Text("选择进程查看详情").SemiBold().CenterHorizontal(),
            new TextBlock().Text("从左侧列表选择一行，实时查看资源占用和进程信息。")
                .FontSize(ThemeFontSize.Small)
                .TextWrapping(TextWrapping.Wrap)
                .CenterHorizontal());
    }

    private FrameworkElement BuildProcessInspector(ProcessDto row)
    {
        processInspectorLiveText.Clear();
        var title = new StackPanel().Horizontal().Spacing(10).Children(
            new TextBlock().Text("\uE7F4").FontFamily(AppTypography.IconFontFamily).FontSize(24).CenterVertical(),
            new StackPanel().Vertical().Spacing(2).Children(
                new TextBlock().Text(row.Name).FontSize(ThemeFontSize.Medium).SemiBold(),
                ProcessInspectorText(() => selectedProcess is { } current
                    ? $"PID {current.Pid.ToString(CultureInfo.InvariantCulture)} · {ProcessState(current)}"
                    : "进程已退出").FontSize(ThemeFontSize.Small)));

        var metrics = new Grid().Columns("*, *").Rows("Auto, Auto, Auto").Spacing(8).Children(
            ProcessInspectorMetric("CPU", () => Percent(selectedProcess?.CpuPercent)),
            ProcessInspectorMetric("内存", () => Bytes(selectedProcess?.WorkingSetBytes)).Column(1),
            ProcessInspectorMetric("I/O", () => Rate(selectedProcess?.IoReadBytesPerSecond, selectedProcess?.IoWriteBytesPerSecond)).Row(1),
            ProcessInspectorMetric("网络", () => Rate(selectedProcess?.NetworkReceiveBytesPerSecond, selectedProcess?.NetworkSendBytesPerSecond)).Row(1).Column(1),
            ProcessInspectorMetric("GPU", () => Percent(selectedProcess?.GpuPercent)).Row(2),
            ProcessInspectorMetric("线程", () => selectedProcess?.ThreadCount.ToString(CultureInfo.InvariantCulture) ?? "—").Row(2).Column(1));

        processInspectorEndButton = new Button()
            .Content("结束任务")
            .IsEnabled(row.CanTerminate)
            .OnClick(() => EndSelected());

        var information = new StackPanel().Vertical().Spacing(8).Children(
            ProcessInspectorInfo("用户", () => selectedProcess?.UserName ?? "未知用户"),
            ProcessInspectorInfo("会话", () => selectedProcess?.SessionId?.ToString(CultureInfo.InvariantCulture) ?? "—"),
            ProcessInspectorInfo("父进程", () => selectedProcess?.ParentPid.ToString(CultureInfo.InvariantCulture) ?? "—"),
            ProcessInspectorInfo("句柄", () => selectedProcess?.HandleCount?.ToString(CultureInfo.InvariantCulture) ?? "—"),
            ProcessInspectorInfo("优先级", () => selectedProcess?.Priority ?? "—"),
            ProcessInspectorInfo("路径", () => selectedProcess?.Path ?? "不可用"),
            ProcessInspectorInfo("发布者", () => selectedProcess?.Publisher ?? "未知"));

        return new ScrollViewer().Background(Color.Transparent).AutoVerticalScroll().NoHorizontalScroll().Content(
            new StackPanel().Vertical().Spacing(12).Children(
                title,
                new Border().BorderThickness(new Thickness(0, 0, 0, 1))
                    .WithTheme((theme, border) => border.BorderBrush = theme.Palette.ControlBorder),
                metrics,
                new TextBlock().Text("进程信息").SemiBold(),
                information,
                new TextBlock().Text("实时数据随 Windows 采样刷新；不可用字段保留为 —。")
                    .FontSize(ThemeFontSize.Small)
                    .TextWrapping(TextWrapping.Wrap),
                new StackPanel().Horizontal().Spacing(8).Children(processInspectorEndButton)));
    }

    private FrameworkElement ProcessInspectorMetric(string label, Func<string> value)
    {
        return new Border().Padding(10).BorderThickness(1).CornerRadius(ShellTokens.ControlRadius)
            .WithTheme((theme, border) =>
            {
                border.Background = theme.Palette.ControlBackground.WithAlpha(ShellTokens.SurfaceAlpha);
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(new StackPanel().Vertical().Spacing(3).Children(
                new TextBlock().Text(label).FontSize(ThemeFontSize.Small),
                ProcessInspectorText(value).FontSize(ThemeFontSize.Medium).SemiBold()));
    }

    private FrameworkElement ProcessInspectorInfo(string label, Func<string> value)
    {
        return new Grid().Columns("72, *").Spacing(8).Children(
            new TextBlock().Text(label).FontSize(ThemeFontSize.Small).CenterVertical(),
            ProcessInspectorText(value).Column(1).TextWrapping(TextWrapping.Wrap));
    }

    private TextBlock ProcessInspectorText(Func<string> value)
    {
        var block = new TextBlock { Text = value() };
        processInspectorLiveText.Add((block, value));
        return block;
    }

    private void SetSelectedProcess(ProcessDto? row)
    {
        var identityChanged = selectedProcess?.Identity != row?.Identity;
        selectedProcess = row;
        if (processInspectorEndButton is not null)
            processInspectorEndButton.IsEnabled = row?.CanTerminate == true;
        if (!identityChanged || processInspectorTransition is null) return;

        processInspectorTransition.Content = row is null
            ? BuildProcessInspectorEmpty()
            : BuildProcessInspector(row);
    }

    private void RefreshSelectedProcess()
    {
        if (selectedProcess is null) return;
        var current = snapshots.Current?.Processes.FirstOrDefault(row => row.Identity == selectedProcess.Identity);
        if (current is null)
        {
            SetSelectedProcess(null);
            return;
        }

        selectedProcess = current;
        if (processInspectorEndButton is not null)
            processInspectorEndButton.IsEnabled = current.CanTerminate;
    }

    private static string ProcessState(ProcessDto process) => process.State == "running" ? "运行中" : process.State;

    private FrameworkElement BuildPerformancePage()
    {
        performanceGrid = new GridView
        {
            Background = Color.Transparent,
            ItemsSource = performanceHistoryItems,
            ZebraStriping = false,
            ShowGridLines = false,
            HeaderHeight = 40,
            RowHeight = 40,
            // Keep the compact history table inside the right-hand surface.
            // The previous fixed widths added up to 740 DIP while this surface
            // is about 600 DIP at the default window size, so the last columns
            // were clipped behind the viewport's horizontal scroll range.
            CellPadding = new Thickness(6, 4),
        };
        performanceGrid.Columns(
            PerformanceHistoryColumn("采样时间", 1.25, row => row.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss")),
            PerformanceHistoryColumn("CPU", 0.72, row => Percent(row.CpuPercent)),
            PerformanceHistoryColumn("内存", 0.72, row => Percent(row.MemoryPercent)),
            PerformanceHistoryColumn("网络收 / 发", 1.75, row => Rate(row.NetworkReceiveBytesPerSecond, row.NetworkSendBytesPerSecond)),
            PerformanceHistoryColumn("磁盘读 / 写", 1.75, row => Rate(SumKnown(row.PhysicalDisks.Select(item => item.ReadBytesPerSecond)), SumKnown(row.PhysicalDisks.Select(item => item.WriteBytesPerSecond)))),
            PerformanceHistoryColumn("GPU", 0.72, row => Percent(row.GpuPercent)));
        ConfigurePerformanceContextMenu(performanceGrid);

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
                border.Background = Color.FromArgb(ShellTokens.SurfaceAlpha, surface.R, surface.G, surface.B);
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(new Grid().Columns("*, Auto").Children(
                new StackPanel().Vertical().Spacing(4).Children(
                    new TextBlock().Text("CPU").FontSize(ThemeFontSize.Large).SemiBold(),
                    new TextBlock().Text("总体使用率 · 来自最近一次成功采样").FontSize(ThemeFontSize.Small)),
                new StackPanel().Vertical().Spacing(2).Children(
                    LiveText(() => Percent(snapshots.Current?.System.CpuPercent)).FontSize(ThemeFontSize.Large).SemiBold(),
                    LiveText(() => snapshots.Current is { } point ? point.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss") : "—").FontSize(ThemeFontSize.Small)).Column(1).CenterVertical()));

        var history = new Border().Padding(16).BorderThickness(1).CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) => { border.Background = theme.Palette.ControlBackground.WithAlpha(ShellTokens.SurfaceAlpha); border.BorderBrush = theme.Palette.ControlBorder; })
            .Child(new StackPanel().Vertical().Spacing(8).Children(
                new TextBlock().Text("真实采样历史").FontSize(ThemeFontSize.Medium).SemiBold(),
                new TextBlock().Text("空表表示尚未取得成功采样；未知指标保留为 —，不会以 0 填充。").FontSize(ThemeFontSize.Small),
                performanceGrid));
        var right = new StackPanel().Vertical().Spacing(ShellTokens.ContentGap).Children(current, history);
        return new StackPanel().Vertical().Spacing(ShellTokens.ContentGap).Children(
            new DockPanel().LastChildFill(false).Children(
                new StackPanel().Vertical().Spacing(4).Children(
                    new TextBlock().Text("选择资源查看真实 Windows 采样历史和设备事实").FontSize(ThemeFontSize.Small)),
                LiveText(() => snapshots.Current is null ? "预热中" : snapshots.Settings.Paused ? "已暂停" : "实时").DockRight().CenterVertical()),
            new Grid().Columns("240, *").Children(resources, right.Column(1).Margin(ShellTokens.ContentGap, 0, 0, 0)));
    }

    private static GridViewColumn<HistoryPoint> PerformanceHistoryColumn(string header, double weight, Func<HistoryPoint, string> value)
    {
        return new GridViewColumn<HistoryPoint>().Header(header).StarWidth(weight).HeaderTextAlignment(TextAlignment.Left).Template(
            _ => new TextBlock()
                .CenterVertical()
                .TextTrimming(TextTrimming.CharacterEllipsis),
            (TextBlock text, HistoryPoint row) =>
            {
                var content = value(row);
                text.Text = content;
                // Preserve the complete sample value when a narrow window has
                // to ellipsize the cell. Tooltips are available to pointer and
                // keyboard users without widening the whole data surface.
                text.ToolTip(content);
            });
    }

    private FrameworkElement PerformanceResourceCard(string glyph, string title, Func<string> value, bool selected)
    {
        var card = new Border().Padding(12).BorderThickness(1).CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) =>
            {
                var surface = theme.Palette.ControlBackground;
                var alpha = selected ? ShellTokens.SelectedSurfaceAlpha : ShellTokens.SurfaceAlpha;
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
        historyGrid = new GridView
        {
            Background = Color.Transparent,
            ItemsSource = historyItems,
            ShowGridLines = false,
            ZebraStriping = false,
            HeaderHeight = 40,
            // Keep the fixed row viewport tall enough for the platform font's
            // full ascent/descent at non-100% display scaling.  The default
            // 28 DIP row clips the lower portions of CJK glyphs and bold text.
            RowHeight = 40,
            CellPadding = new Thickness(8, 4),
        };
        historyGrid.Columns(
            ProcessNameColumn<ProcessHistoryDto>("名称", "history", row => row.Identity, row => row.Name).StarWidth(3),
            new GridViewColumn<ProcessHistoryDto>().Header("PID").Width(90).HeaderTextAlignment(TextAlignment.Left).Text(row => row.Pid.ToString(CultureInfo.InvariantCulture)),
            new GridViewColumn<ProcessHistoryDto>().Header("状态").Width(100).Text(row => row.IsRunning ? "运行中" : "已退出"),
            new GridViewColumn<ProcessHistoryDto>().Header("首次观测").Width(180).Text(row => row.FirstObservedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
            new GridViewColumn<ProcessHistoryDto>().Header("最后观测").Width(180).Text(row => row.LastObservedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
            new GridViewColumn<ProcessHistoryDto>().Header("CPU 时间").Width(120).HeaderTextAlignment(TextAlignment.Left).Text(row => row.ObservedCpuTime100ns ?? "—"),
            new GridViewColumn<ProcessHistoryDto>().Header("I/O 读取").Width(130).HeaderTextAlignment(TextAlignment.Left).Text(row => row.ObservedIoReadBytes ?? "—"),
            new GridViewColumn<ProcessHistoryDto>().Header("I/O 写入").Width(130).HeaderTextAlignment(TextAlignment.Left).Text(row => row.ObservedIoWriteBytes ?? "—"));
        ConfigureHistoryContextMenu(historyGrid);
        var locate = new Button().Content("定位运行进程").IsEnabled(false).OnClick(() => LocateHistoryProcess());
        historyGrid.OnSelectionChanged(value => locate.IsEnabled = value is ProcessHistoryDto row && row.IsRunning && FindProcess(row.Identity) is not null);
        var reset = new Button().Content("清除统计").OnClick(() =>
        {
            snapshots.ResetHistory();
            UpdateOtherRows();
            statusText!.Text = "本次会话历史已清除。";
        });
        var command = new DockPanel().LastChildFill(false).Padding(0, 0, 0, 12).Spacing(8).Children(
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
                : "历史数据来自本次会话的真实 Windows 采样。未知字段保留原因，不以 0 填充.").FontSize(ThemeFontSize.Small).TextWrapping(TextWrapping.Wrap);
        return new Grid().Rows("Auto, Auto, *").Children(command, emptyHint.Row(1), ShellComponents.DataSurface(historyGrid).Row(2));
    }

    private FrameworkElement BuildStartupPage()
    {
        startupGrid = new GridView
        {
            Background = Color.Transparent,
            ItemsSource = startupItems,
            ShowGridLines = false,
            ZebraStriping = false,
            HeaderHeight = 40,
            RowHeight = 40,
            CellPadding = new Thickness(8, 4),
        };
        startupGrid.Columns(
            ProcessNameColumn<StartupDto>("名称", "startup", row => row.Id, row => row.Name).StarWidth(2),
            new GridViewColumn<StartupDto>().Header("来源").Width(160).Text(row => row.Source),
            new GridViewColumn<StartupDto>().Header("状态").Width(140).Text(row => StartupStatus(row.Enabled)),
            new GridViewColumn<StartupDto>().Header("状态原因").StarWidth(2).Text(row => row.EnabledReason),
            new GridViewColumn<StartupDto>().Header("命令").StarWidth(3).Text(row => row.Command),
            new GridViewColumn<StartupDto>().Header("来源位置").StarWidth(2).Text(row => row.LocationPath ?? row.LocationReason ?? "不可用"));
        ConfigureStartupContextMenu(startupGrid);
        var openSource = new Button().Content("打开来源").IsEnabled(false).OnClick(() => OpenSelectedStartupLocation());
        var detailsTitle = new TextBlock().Text("选择启动项").FontSize(ThemeFontSize.Medium).SemiBold();
        var detailsStatus = new TextBlock().Text("从列表选择一个真实 Windows 启动项。").FontSize(ThemeFontSize.Small).TextWrapping(TextWrapping.Wrap);
        var detailsSource = new TextBlock().Text("来源：—").FontSize(ThemeFontSize.Small).TextWrapping(TextWrapping.Wrap);
        var detailsCommand = new TextBlock().Text("命令：—").FontSize(ThemeFontSize.Small).TextWrapping(TextWrapping.Wrap);
        var detailsLocation = new TextBlock().Text("位置：—").FontSize(ThemeFontSize.Small).TextWrapping(TextWrapping.Wrap);
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
        var command = new DockPanel().LastChildFill(false).Padding(0, 0, 0, 12).Spacing(8).Children(
            new StackPanel().Vertical().Spacing(4).Children(
                new TextBlock().Text("启动应用").SemiBold(),
                LiveText(() => $"Windows Run / RunOnce 与 Startup 文件夹 · {snapshots.Current?.StartupItems.Count ?? 0} 项").FontSize(ThemeFontSize.Small)),
            ShellComponents.CommandBar(openSource, startupSettings, userStartup, commonStartup).DockRight());
        var details = new Border().Padding(16).BorderThickness(1).CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) =>
            {
                var surface = theme.Palette.ControlBackground;
                border.Background = Color.FromArgb(ShellTokens.SurfaceAlpha, surface.R, surface.G, surface.B);
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
        usersGrid = new GridView
        {
            Background = Color.Transparent,
            ItemsSource = userItems,
            ShowGridLines = false,
            ZebraStriping = false,
            HeaderHeight = 40,
            RowHeight = 40,
            CellPadding = new Thickness(8, 4),
        };
        usersGrid.Columns(
            new GridViewColumn<UserDto>().Header("用户").StarWidth(2).Text(row => row.UserName ?? "未知用户"),
            new GridViewColumn<UserDto>().Header("会话").Width(90).HeaderTextAlignment(TextAlignment.Left).Text(row => row.SessionId.ToString(CultureInfo.InvariantCulture)),
            new GridViewColumn<UserDto>().Header("状态").Width(140).Text(row => row.State),
            new GridViewColumn<UserDto>().Header("进程").Width(100).HeaderTextAlignment(TextAlignment.Left).Text(row => row.ProcessCount.ToString(CultureInfo.InvariantCulture)),
            new GridViewColumn<UserDto>().Header("CPU").Width(100).HeaderTextAlignment(TextAlignment.Left).Text(row => Percent(row.CpuPercent)),
            new GridViewColumn<UserDto>().Header("内存").Width(120).HeaderTextAlignment(TextAlignment.Left).Text(row => Bytes(row.WorkingSetBytes)),
            new GridViewColumn<UserDto>().Header("不可读进程").Width(120).HeaderTextAlignment(TextAlignment.Left).Text(row => row.UnreadableProcessCount.ToString(CultureInfo.InvariantCulture)));
        ConfigureUsersContextMenu(usersGrid);
        var locate = new Button().Content("查看此会话进程").IsEnabled(false).OnClick(() => LocateUserProcesses());
        usersGrid.OnSelectionChanged(value => locate.IsEnabled = value is UserDto);
        var command = new DockPanel().LastChildFill(false).Padding(0, 0, 0, 12).Spacing(8).Children(
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
        return new Grid().Rows("Auto, *").Children(command, body.Row(1));
    }

    private FrameworkElement UserMetricCard(string title, Func<string> value, string description)
    {
        var card = new Border().Padding(16).BorderThickness(1).CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) =>
            {
                var surface = theme.Palette.ControlBackground;
                border.Background = Color.FromArgb(ShellTokens.SurfaceAlpha, surface.R, surface.G, surface.B);
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
        detailGrid = new GridView
        {
            Background = Color.Transparent,
            ItemsSource = detailItems,
            ShowGridLines = false,
            ZebraStriping = false,
            HeaderHeight = 40,
            RowHeight = 40,
            CellPadding = new Thickness(8, 4),
        };
        detailGrid.Columns(
            ProcessNameColumn<ProcessDto>("名称", "process", row => row.Identity, row => row.Name).StarWidth(2),
            new GridViewColumn<ProcessDto>().Header("PID").Width(90).HeaderTextAlignment(TextAlignment.Left).Text(row => row.Pid.ToString(CultureInfo.InvariantCulture)),
            new GridViewColumn<ProcessDto>().Header("状态").Width(100).Text(row => row.State == "running" ? "运行中" : row.State),
            new GridViewColumn<ProcessDto>().Header("用户").StarWidth(1).Text(row => row.UserName ?? "未知用户"),
            new GridViewColumn<ProcessDto>().Header("CPU").Width(100).HeaderTextAlignment(TextAlignment.Left).Text(row => Percent(row.CpuPercent)),
            new GridViewColumn<ProcessDto>().Header("内存").Width(120).HeaderTextAlignment(TextAlignment.Left).Text(row => Bytes(row.WorkingSetBytes)),
            new GridViewColumn<ProcessDto>().Header("父 PID").Width(100).HeaderTextAlignment(TextAlignment.Left).Text(row => row.ParentPid.ToString(CultureInfo.InvariantCulture)),
            new GridViewColumn<ProcessDto>().Header("会话").Width(90).HeaderTextAlignment(TextAlignment.Left).Text(row => row.SessionId?.ToString(CultureInfo.InvariantCulture) ?? "—"),
            new GridViewColumn<ProcessDto>().Header("优先级").Width(120).Text(row => row.Priority ?? "—"),
            new GridViewColumn<ProcessDto>().Header("线程").Width(90).HeaderTextAlignment(TextAlignment.Left).Text(row => row.ThreadCount.ToString(CultureInfo.InvariantCulture)),
            new GridViewColumn<ProcessDto>().Header("句柄").Width(90).HeaderTextAlignment(TextAlignment.Left).Text(row => row.HandleCount?.ToString(CultureInfo.InvariantCulture) ?? "—"),
            new GridViewColumn<ProcessDto>().Header("网络").Width(130).HeaderTextAlignment(TextAlignment.Left).Text(row => Rate(row.NetworkReceiveBytesPerSecond, row.NetworkSendBytesPerSecond)),
            new GridViewColumn<ProcessDto>().Header("路径").StarWidth(3).Text(row => row.Path ?? "不可用"));
        ConfigureProcessContextMenu(detailGrid);
        var run = new Button().Content("运行新任务").OnClick(RunTask);
        var end = new Button().Content("结束任务").IsEnabled(false).OnClick(() => EndSelected());
        detailGrid.OnSelectionChanged(value => end.IsEnabled = value is ProcessDto row && row.CanTerminate);
        var command = new DockPanel().LastChildFill(false).Padding(0, 0, 0, 12).Spacing(8).Children(
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
        serviceGrid = new GridView
        {
            Background = Color.Transparent,
            ItemsSource = serviceItems,
            ShowGridLines = false,
            ZebraStriping = false,
            HeaderHeight = 40,
            RowHeight = 40,
            CellPadding = new Thickness(8, 4),
        };
        serviceGrid.Columns(
            ProcessNameColumn<ServiceDto>("显示名称", "service", row => row.Name, row => row.DisplayName).StarWidth(2),
            new GridViewColumn<ServiceDto>().Header("服务名").StarWidth(2).Text(row => row.Name),
            new GridViewColumn<ServiceDto>().Header("状态").Width(140).Text(ServiceStatus),
            new GridViewColumn<ServiceDto>().Header("PID").Width(90).HeaderTextAlignment(TextAlignment.Left).Text(row => row.Pid == 0 ? "—" : row.Pid.ToString(CultureInfo.InvariantCulture)));
        ConfigureServiceContextMenu(serviceGrid);
        var start = new Button().Content("启动").IsEnabled(false).OnClick(() => ControlService("start"));
        var stop = new Button().Content("停止").IsEnabled(false).OnClick(() => ControlService("stop"));
        var restart = new Button().Content("重启").IsEnabled(false).OnClick(() => ControlService("restart"));
        var details = new Button().Content("服务详情").IsEnabled(false).OnClick(() => ShowServiceDetails());
        var manager = new Button().Content("服务管理器").OnClick(() => _ = OpenServiceManager());
        var detailsTitle = new TextBlock().Text("选择服务").FontSize(ThemeFontSize.Medium).SemiBold();
        var detailsStatus = new TextBlock().Text("从列表选择一个真实 Windows 服务。").FontSize(ThemeFontSize.Small).TextWrapping(TextWrapping.Wrap);
        var detailsName = new TextBlock().Text("服务名：—").FontSize(ThemeFontSize.Small).TextWrapping(TextWrapping.Wrap);
        var detailsPid = new TextBlock().Text("PID：—").FontSize(ThemeFontSize.Small).TextWrapping(TextWrapping.Wrap);
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
        var command = new DockPanel().LastChildFill(false).Padding(0, 0, 0, 12).Spacing(8).Children(
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
                border.Background = Color.FromArgb(ShellTokens.SurfaceAlpha, surface.R, surface.G, surface.B);
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(new StackPanel().Vertical().Spacing(10).Children(
                detailsTitle,
                detailsStatus,
                new Border().BorderThickness(new Thickness(0, 0, 0, 1)).WithTheme((theme, border) => border.BorderBrush = theme.Palette.ControlBorder),
                detailsName,
                detailsPid,
                new TextBlock().Text("动作只通过 Windows SCM 执行；权限或 pending 状态会保留具体原因。").FontSize(ThemeFontSize.Small).TextWrapping(TextWrapping.Wrap)));
        var body = new Grid().Columns("*, 320").Children(
            ShellComponents.DataSurface(serviceGrid),
            detailCard.Column(1).Margin(ShellTokens.ContentGap, 0, 0, 0));
        return new Grid().Rows("Auto, *").Children(command, body.Row(1));
    }

    private FrameworkElement BuildSettingsPage()
    {
        // Settings follows the WinUI SettingsCard rhythm from the reference:
        // one readable column, 16px card padding and a constrained control
        // rail.  Every value is still wired to the existing persisted
        // preference or real Windows action below.
        var interval = new ComboBox().Width(240).Items(["1 秒", "2 秒", "5 秒"]).SelectedIndex(preferences.IntervalMs switch { 2000 => 1, 5000 => 2, _ => 0 }).OnSelectionChanged(value =>
        {
            if (value is string text) _ = snapshots.ApplySettings(new SettingsRequest(text.StartsWith("5") ? 5000 : text.StartsWith("2") ? 2000 : 1000, snapshots.Settings.Paused));
        });
        var theme = new ComboBox().Width(240).Items(["跟随系统", "浅色", "深色"]).SelectedIndex(preferences.Theme switch { "light" => 1, "dark" => 2, _ => 0 }).OnSelectionChanged(value =>
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
        var density = new ComboBox().Width(240).Items(["紧凑", "标准"]).SelectedIndex(preferences.Current.Preferences.Density == "standard" ? 1 : 0).OnSelectionChanged(value =>
        {
            if (value is string text) SaveUiPreferences(ui => ui with { Density = text == "标准" ? "standard" : "compact" });
        });
        var sidebarCollapsed = new CheckBox().Content("使用紧凑侧栏").BindIsChecked(new ObservableValue<bool>(preferences.Current.Preferences.SidebarCollapsed)).OnCheckedChanged(ApplySidebarCollapsed);
        var alwaysOnTop = new CheckBox().Content("始终置顶").BindIsChecked(new ObservableValue<bool>(preferences.Current.Preferences.Window?.AlwaysOnTop == true)).OnCheckedChanged(_value => { _ = ToggleAlwaysOnTop(); });
        var integrationState = integration.Read();
        integrationSummary = new TextBlock().Text(FormatIntegration(integrationState)).FontSize(ThemeFontSize.Small).TextWrapping(TextWrapping.Wrap);
        integrationEnableButton = new Button().Content(integrationState.CanTakeOver ? "接管现有入口" : "启用入口").IsEnabled(integrationState.CanEnable || integrationState.CanTakeOver).OnClick(() => _ = ApplyIntegration(true));
        integrationDisableButton = new Button().Content("恢复标准入口").IsEnabled(integrationState.CanDisable).OnClick(() => _ = ApplyIntegration(false));
        var integrationControls = new StackPanel().Horizontal().Spacing(8).Children(
            integrationEnableButton,
            integrationDisableButton,
            new Button().Content("刷新状态").OnClick(RefreshIntegration));
        var integrationCard = new Border().Padding(16).BorderThickness(1).CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) => { border.Background = theme.Palette.ControlBackground.WithAlpha(ShellTokens.SurfaceAlpha); border.BorderBrush = theme.Palette.ControlBorder; })
            .Child(new StackPanel().Vertical().Spacing(8).Children(
                new Grid().Columns("*, Auto").Children(
                    new StackPanel().Vertical().Spacing(4).Children(
                        new TextBlock().Text("任务管理器入口").SemiBold(),
                        new TextBlock().Text("只读显示真实 IFEO 状态；写入需要管理员权限并在执行前重新确认版本。").FontSize(ThemeFontSize.Small).TextWrapping(TextWrapping.Wrap)),
                    integrationControls.Column(1).CenterVertical()),
                integrationSummary));

        var version = typeof(TaskManagerWindow).Assembly.GetName().Version?.ToString() ?? "版本信息不可用";
        var updateInfo = SettingCard("当前版本", $"Takao {version} · MewUI Win32 + Direct2D。正式桌面后台服务负责检查、下载和安装。", new TextBlock().Text("已安装").FontSize(ThemeFontSize.Small));
        var fontInfo = SettingCard("界面字体", "HarmonyOS Sans SC 随应用发布并在进程内加载，不会安装到 Windows 系统字体目录。", new TextBlock().Text("HarmonyOS Sans SC").FontSize(ThemeFontSize.Small));

        var sourceStatus = LiveText(() => snapshots.Current is null
            ? "正在预热 Windows 原生采样；尚未取得可展示的快照。"
            : snapshots.Settings.Paused
                ? $"已暂停，保留最后成功快照（{snapshots.Current.CapturedAtUtc.ToLocalTime():HH:mm:ss}）。"
                : $"最近成功采样 {snapshots.Current.CapturedAtUtc.ToLocalTime():HH:mm:ss}；未知字段保留原因。")
            .FontSize(ThemeFontSize.Small);
        var sourceLinks = new StackPanel().Horizontal().Spacing(8).Children(
            new Button().Content("打开系统信息").OnClick(() => SelectPage(Array.IndexOf(PageKeys, "systemInformation"))),
            new Button().Content("打开启动应用").OnClick(() => SelectPage(Array.IndexOf(PageKeys, "startup"))),
            new Button().Content("打开服务").OnClick(() => SelectPage(Array.IndexOf(PageKeys, "services"))));
        var sourceCard = new Border().Padding(16).BorderThickness(1).CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) => { border.Background = theme.Palette.ControlBackground.WithAlpha(ShellTokens.SurfaceAlpha); border.BorderBrush = theme.Palette.ControlBorder; })
            .Child(new StackPanel().Vertical().Spacing(8).Children(
                new TextBlock().Text("来源与能力").SemiBold(),
                new TextBlock().Text("数据来自 Windows 原生进程、性能、启动项、SCM 和硬件接口；页面只显示实际可用能力。").FontSize(ThemeFontSize.Small).TextWrapping(TextWrapping.Wrap),
                sourceStatus,
                sourceLinks));

        var sampling = SettingGroup("采样与显示", "控制实时采样和数据行的阅读密度。",
            SettingCard("采样间隔", "控制真实 Windows 采样频率。", interval),
            SettingCard("暂停采样", "暂停时保留最后成功快照，并在页头标注暂停状态。", paused));
        var appearance = SettingGroup("外观与窗口", "调整界面层级和窗口行为；保存后立即生效。",
            SettingCard("密度", "保存紧凑或标准数据行密度。", density),
            SettingCard("主题", "选择浅色、深色或跟随 Windows 系统设置。", theme),
            fontInfo,
            SettingCard("侧栏", "折叠后保留导航图标和键盘语义。", sidebarCollapsed),
            SettingCard("窗口", "通过 Windows 读回当前置顶状态。", alwaysOnTop));
        var entry = SettingGroup("任务管理器入口", "安全读取或恢复本机任务管理器入口，动作按实际权限启用。", integrationCard);
        var update = SettingGroup("版本与更新", "安装版本与更新状态由正式桌面后台服务提供。", updateInfo);
        var source = SettingGroup("来源与能力", "查看真实 Windows 来源和当前采样状态。", sourceCard);
        return new Grid().Columns("*, *").Spacing(ShellTokens.ContentGap).Children(
            new StackPanel().Vertical().Spacing(ShellTokens.ContentGap).Children(sampling, appearance),
            new StackPanel().Vertical().Spacing(ShellTokens.ContentGap).Children(entry, update, source).Column(1));
    }

    private static TextBlock NewSystemInfoDetailsBlock()
        => new TextBlock().Text("正在等待 Windows 硬件读取…").FontSize(ThemeFontSize.Small)
            .TextWrapping(TextWrapping.NoWrap).TextTrimming(TextTrimming.CharacterEllipsis);

    private static FrameworkElement BuildSystemInformationCards(
        TextBlock processors,
        TextBlock graphics,
        TextBlock motherboard,
        TextBlock memory,
        TextBlock disks,
        TextBlock displays,
        TextBlock windows)
    {
        return new Grid().Columns("*, *").Rows("Auto, Auto, Auto, Auto").Children(
            SystemInfoCard("处理器", "CPU、核心和逻辑处理器", processors).Row(0).Column(0),
            SystemInfoCard("显卡", "真实图形适配器与显存", graphics).Row(0).Column(1),
            SystemInfoCard("主板", "主板制造商、产品与版本", motherboard).Row(1).Column(0),
            SystemInfoCard("内存", "容量、模块与配置速度", memory).Row(1).Column(1),
            SystemInfoCard("磁盘", "型号、介质、总线与容量", disks).Row(2).Column(0),
            SystemInfoCard("显示器", "分辨率、刷新率与物理尺寸", displays).Row(2).Column(1),
            SystemInfoCard("Windows", "系统版本与架构", windows).Row(3).ColumnSpan(2));
    }

    private FrameworkElement BuildSystemInformationPage()
    {
        systemInfoSummary = new TextBlock().Text("正在读取 Windows 硬件概览…").FontSize(ThemeFontSize.Small);
        systemInfoProcessors = NewSystemInfoDetailsBlock();
        systemInfoGraphics = NewSystemInfoDetailsBlock();
        systemInfoMotherboard = NewSystemInfoDetailsBlock();
        systemInfoMemory = NewSystemInfoDetailsBlock();
        systemInfoDisks = NewSystemInfoDetailsBlock();
        systemInfoDisplays = NewSystemInfoDetailsBlock();
        systemInfoWindows = NewSystemInfoDetailsBlock();
        var refresh = new Button().Content("刷新硬件信息").OnClick(() => _ = RefreshSystemInformation(true));
        systemInfoCopyButton = new Button().Content("复制").IsEnabled(false).OnClick(CopySystemInformation);
        var command = new DockPanel().LastChildFill(false).Padding(0, 0, 0, 12).Spacing(8).Children(
            new StackPanel().Vertical().Spacing(4).Children(
                new TextBlock().Text("系统信息").SemiBold(),
                new TextBlock().Text("硬件概览由 Windows 原生接口按需读取，不参与实时采样。").FontSize(ThemeFontSize.Small).TextWrapping(TextWrapping.Wrap)),
            ShellComponents.CommandBar(refresh, systemInfoCopyButton).DockRight());

        var cards = BuildSystemInformationCards(
            systemInfoProcessors,
            systemInfoGraphics,
            systemInfoMotherboard,
            systemInfoMemory,
            systemInfoDisks,
            systemInfoDisplays,
            systemInfoWindows);
        var content = new StackPanel().Vertical().Spacing(ShellTokens.ContentGap).Children(
            new Border().Padding(12, 10).BorderThickness(1).CornerRadius(ShellTokens.ControlRadius)
                .WithTheme((theme, border) =>
                {
                    border.Background = theme.Palette.ControlBackground.WithAlpha(ShellTokens.SurfaceAlpha);
                    border.BorderBrush = theme.Palette.ControlBorder;
                })
                .Child(systemInfoSummary),
            cards);
        return new StackPanel().Vertical().Spacing(ShellTokens.ContentGap).Children(command, content);
    }

    private static FrameworkElement SystemInfoCard(string title, string description, TextBlock details)
    {
        return new Border().Padding(16).Margin(0, 0, ShellTokens.CardGap, ShellTokens.CardGap)
            .BorderThickness(1).CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) =>
            {
                border.Background = theme.Palette.ControlBackground.WithAlpha(ShellTokens.SurfaceAlpha);
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(new StackPanel().Vertical().Spacing(8).Children(
                new StackPanel().Vertical().Spacing(2).Children(
                    new TextBlock().Text(title).FontSize(ThemeFontSize.Medium).SemiBold(),
                    new TextBlock().Text(description).FontSize(ThemeFontSize.Small)
                        .TextWrapping(TextWrapping.NoWrap).TextTrimming(TextTrimming.CharacterEllipsis)),
                details.TextWrapping(TextWrapping.Wrap).LineSpacing(2)));
    }

    private async Task RefreshSystemInformation(bool refresh)
    {
        try
        {
            var info = await systemInformation.ReadAsync(refresh, CancellationToken.None);
            if (systemInfoSummary is null) return;
            systemInformationClipboard = FormatSystemInformation(info);
            systemInfoSummary.Text = $"状态：{SystemInformationStatus(info.Status)} · 采集时间：{FormatCaptureTime(info.CapturedAtUtc)}";
            SetSystemInfoDetails(systemInfoProcessors, overviewSystemInfoProcessors, FormatProcessors(info));
            SetSystemInfoDetails(systemInfoGraphics, overviewSystemInfoGraphics, FormatGraphics(info));
            SetSystemInfoDetails(systemInfoMotherboard, overviewSystemInfoMotherboard, FormatMotherboard(info));
            SetSystemInfoDetails(systemInfoMemory, overviewSystemInfoMemory, FormatMemory(info));
            SetSystemInfoDetails(systemInfoDisks, overviewSystemInfoDisks, FormatDisks(info));
            SetSystemInfoDetails(systemInfoDisplays, overviewSystemInfoDisplays, FormatDisplays(info));
            SetSystemInfoDetails(systemInfoWindows, overviewSystemInfoWindows, FormatWindows(info));
            if (systemInfoCopyButton is not null) systemInfoCopyButton.IsEnabled = info.Status is not "loading";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (systemInfoSummary is not null) systemInfoSummary.Text = $"硬件信息暂不可用：{error.Message}";
            if (systemInfoCopyButton is not null) systemInfoCopyButton.IsEnabled = false;
        }
    }

    private static void SetSystemInfoDetails(TextBlock? primary, TextBlock? overview, string text)
    {
        var summary = CompactSystemInfoSummary(text);
        if (primary is not null) primary.Text = summary;
        if (overview is not null) overview.Text = summary;
    }

    private static string CompactSystemInfoSummary(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length <= 2 ? string.Join(" · ", lines) : string.Join(" · ", lines.Take(2)) + " …";
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
        foreach (var item in new[] { navigation, footerNavigation })
        {
            if (item is null) continue;
            item.Width = collapsed ? ShellTokens.SidebarCompactWidth : ShellTokens.SidebarWidth - 16;
            item.ItemPadding = collapsed ? new Thickness(8, 0) : new Thickness(12, 0);
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

    private void CopyToClipboard(string text, string successMessage)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            if (statusText is null) return;
            var clipboard = Application.Current?.PlatformServices?.Clipboard;
            statusText.Text = clipboard is not null && clipboard.TrySetText(text)
                ? successMessage
                : "系统剪贴板当前不可用。";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            statusText!.Text = $"复制失败：{error.Message}";
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

    private static string SystemInformationStatus(string status) => status switch
    {
        "ok" => "已读取",
        "loading" => "正在预热",
        "unavailable" => "不可用",
        _ => status,
    };

    private static string FormatCaptureTime(DateTime? capturedAtUtc)
        => capturedAtUtc is { } captured
            ? captured.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : "—（尚未取得成功读取）";

    private static string FormatProcessors(SystemInformationDto info)
    {
        if (info.Processors.Count == 0) return UnavailableSummary(info.Unavailable, "处理器");
        return string.Join(Environment.NewLine, info.Processors.Select(cpu =>
            $"{Text(cpu.Name)}\n制造商：{Text(cpu.Manufacturer)} · 核心：{Text(cpu.CoreCount)} · 逻辑处理器：{Text(cpu.LogicalProcessorCount)}"));
    }

    private static string FormatGraphics(SystemInformationDto info)
    {
        if (info.GraphicsAdapters.Count == 0) return UnavailableSummary(info.Unavailable, "显卡");
        return string.Join(Environment.NewLine, info.GraphicsAdapters.Select(gpu =>
            $"{Text(gpu.Name)}\n显存：{Text(gpu.DedicatedMemoryBytes)} · {(gpu.IsSoftware ? "软件适配器" : "硬件适配器")}"));
    }

    private static string FormatMotherboard(SystemInformationDto info)
        => $"制造商：{Text(info.Motherboard.Manufacturer)}\n产品：{Text(info.Motherboard.Product)}\n版本：{Text(info.Motherboard.Version)}";

    private static string FormatMemory(SystemInformationDto info)
    {
        var header = $"已安装：{Text(info.Memory.InstalledBytes)} · 模块：{Text(info.Memory.ModuleCount)}";
        if (info.Memory.Modules.Count == 0) return header;
        var modules = info.Memory.Modules.Select(module =>
            $"{Text(module.DeviceLocator)} · {Text(module.Manufacturer)} · {Text(module.CapacityBytes)} · {Text(module.ConfiguredSpeedMtPerSecond ?? module.SpeedMtPerSecond)} MT/s");
        return header + Environment.NewLine + string.Join(Environment.NewLine, modules);
    }

    private static string FormatDisks(SystemInformationDto info)
    {
        if (info.Disks.Count == 0) return UnavailableSummary(info.Unavailable, "磁盘");
        return string.Join(Environment.NewLine, info.Disks.Select(disk =>
            $"{Text(disk.Model)} · {Text(disk.MediaType)} · {Text(disk.BusType)}\n容量：{Text(disk.CapacityBytes)} · 设备号：{Text(disk.DeviceNumber)}"));
    }

    private static string FormatDisplays(SystemInformationDto info)
    {
        if (info.Displays.Count == 0) return UnavailableSummary(info.Unavailable, "显示器");
        return string.Join(Environment.NewLine, info.Displays.Select(display =>
            $"{Text(display.Name)}\n{Text(display.WidthPixels)} × {Text(display.HeightPixels)} · {Text(display.RefreshRateHz)} Hz · {Text(display.WidthMillimeters)} × {Text(display.HeightMillimeters)} mm"));
    }

    private static string FormatWindows(SystemInformationDto info)
        => $"{Text(info.OperatingSystem.Caption)} · {Text(info.OperatingSystem.DisplayVersion)}\nBuild：{Text(info.OperatingSystem.Build)} · 架构：{Text(info.OperatingSystem.Architecture)}";

    private static string UnavailableSummary(IReadOnlyDictionary<string, Unavailable> reasons, string label)
    {
        if (reasons.Count == 0) return $"{label}：—（尚未取得成功读取）";
        var reason = reasons.Values.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Message));
        return $"{label}：—\n{reason?.Message ?? "Windows 未提供该字段。"}";
    }

    private static string Text<T>(T? value) where T : struct => value?.ToString() ?? "—";
    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static FrameworkElement PageStack(params FrameworkElement[] children) => new ScrollViewer().Background(Color.Transparent).AutoVerticalScroll().NoHorizontalScroll().Content(new StackPanel().Vertical().Spacing(16).Children(children));

    private static FrameworkElement SettingGroup(string title, string description, params FrameworkElement[] cards)
    {
        return new StackPanel().Vertical().Spacing(8).Children(
            new StackPanel().Vertical().Spacing(2).Children(
                new TextBlock().Text(title).FontSize(ThemeFontSize.Medium).SemiBold(),
                new TextBlock().Text(description).FontSize(ThemeFontSize.Small).TextWrapping(TextWrapping.Wrap)),
            new StackPanel().Vertical().Spacing(4).Children(cards));
    }

    private static FrameworkElement SettingCard(string title, string description, FrameworkElement control)
    {
        return new Border().Padding(8, 10).BorderThickness(1).CornerRadius(ShellTokens.PanelRadius)
            .WithTheme((theme, border) =>
            {
                border.Background = theme.Palette.ControlBackground.WithAlpha(ShellTokens.SurfaceAlpha);
                border.BorderBrush = theme.Palette.ControlBorder;
            })
            .Child(new Grid().Columns("*, Auto").Children(
                new StackPanel().Vertical().Spacing(4).Children(
                    new TextBlock().Text(title).SemiBold(),
                    new TextBlock().Text(description).FontSize(ThemeFontSize.Small)
                        .TextWrapping(TextWrapping.NoWrap).TextTrimming(TextTrimming.CharacterEllipsis)),
                control.Column(1).CenterVertical()));
    }

    private static FrameworkElement SettingRow(string title, string description, FrameworkElement control) => SettingCard(title, description, control);

    private FrameworkElement MetricRow(string label, Func<string> value)
    {
        return new Border().Padding(16).BorderThickness(1).CornerRadius(8)
            .WithTheme((theme, border) => { border.Background = theme.Palette.ControlBackground.WithAlpha(ShellTokens.SurfaceAlpha); border.BorderBrush = theme.Palette.ControlBorder; })
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

        var pageKey = PageKeys[currentPage];
        var mainIndex = Array.FindIndex(MainNavigationEntries, item => item.Key == pageKey);
        if (navigation is not null && navigation.SelectedIndex != mainIndex)
            navigation.SelectedIndex = mainIndex;

        var footerIndex = pageKey == "settings" ? 0 : -1;
        if (footerNavigation is not null && footerNavigation.SelectedIndex != footerIndex)
            footerNavigation.SelectedIndex = footerIndex;
    }

    private void Poll()
    {
        UpdateSummary();
        UpdateProcessRows();
        RefreshSelectedProcess();
        UpdateOtherRows();
        foreach (var item in liveText) item.Block.Text = item.Get();
        foreach (var item in processInspectorLiveText) item.Block.Text = item.Get();
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

    private void ShowProcessDetails(ProcessDto row)
    {
        if (processGrid is not null) processGrid.SelectedItem = row;
        if (detailGrid is not null)
        {
            detailGrid.SelectedItem = row;
            if (detailGrid.SelectedIndex >= 0) detailGrid.ScrollIntoView(detailGrid.SelectedIndex);
        }
        SetSelectedProcess(row);
        SelectPage(Array.IndexOf(PageKeys, "details"));
    }

    private async void OpenProcessLocation(ProcessDto row)
    {
        if (row.CreationTimeFileTime is not { } creation || string.IsNullOrWhiteSpace(row.Path)) return;
        try
        {
            statusText!.Text = await shell.ProcessFileAsync(row.Pid, new FileActionRequest(creation, "openLocation"));
        }
        catch (ApiException error)
        {
            statusText!.Text = error.Message;
        }
    }

    private void LocateHistoryProcess(ProcessHistoryDto? selected = null)
    {
        var history = selected ?? historyGrid?.SelectedItem as ProcessHistoryDto;
        if (history is null || !history.IsRunning) return;
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

    private async void OpenSelectedStartupLocation(StartupDto? selected = null)
    {
        var row = selected ?? startupGrid?.SelectedItem as StartupDto;
        if (row is null || !row.CanOpenLocation) return;
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

    private async void ControlService(string command, ServiceDto? selected = null)
    {
        var service = selected ?? serviceGrid?.SelectedItem as ServiceDto;
        if (service is null) return;
        ServiceDetails details;
        try
        {
            details = await Task.Run(() => serviceActions.Details(service.Name));
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
            var result = await serviceActions.ControlAsync(service.Name, new ServiceControlRequest(command, details.State, details.Pid));
            statusText!.Text = result.Message;
            snapshots.RefreshSlowData();
        }
        catch (ApiException error)
        {
            statusText!.Text = error.Message;
        }
    }

    private async void ShowServiceDetails(ServiceDto? selected = null)
    {
        var service = selected ?? serviceGrid?.SelectedItem as ServiceDto;
        if (service is null) return;
        try
        {
            var details = await Task.Run(() => serviceActions.Details(service.Name));
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

    private void LocateUserProcesses(UserDto? selected = null)
    {
        var user = selected ?? usersGrid?.SelectedItem as UserDto;
        if (user is null) return;
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

    private async void EndSelected(ProcessDto? selected = null)
    {
        var row = selected ?? selectedProcess ?? processGrid?.SelectedItem as ProcessDto ?? detailGrid?.SelectedItem as ProcessDto;
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

