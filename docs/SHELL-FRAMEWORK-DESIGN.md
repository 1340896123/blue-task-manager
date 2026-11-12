# Takao 公共主框架设计

日期：2026-11-12。本文把用户提供的任务管理器截图作为布局参考，定义 MewUI 主窗口中所有页面共用的壳体。截图中的进程名称、PID、资源数值和图标只用于说明层级与密度，不作为业务数据或静态内容来源。

## 目标

公共框架负责窗口级交互和页面级布局一致性：窗口标题区、全局搜索与资源摘要、左侧导航、页头标题/副标题、命令栏、数据承载面和底部设置入口由同一套组件创建。页面只提供标题、命令描述、内容控件和真实状态回调，不能重复创建第二套壳体。

目标布局沿用 Windows 任务管理器结构：顶部应用区约 56px，左侧导航约 204px（紧凑态 64px），页面内容左右 24px、上方 16px；页头约 72px，命令按钮高度 44px，表头 40px，数据行 32～40px。默认窗口继续使用 1100×720 逻辑像素，最小 960×640；1920×1080 是显示验收尺寸。

## 组件边界

### `WindowShell`

`WindowShell` 是 `TaskManagerView.OnBuild` 的唯一根布局，使用两行两列：标题区跨两列，第二行放导航和页面宿主。它保存 `Grid`、导航边界和页面宿主引用，使紧凑侧栏、窗口状态和主题变化能够更新布局，而不是只改变内部控件宽度。

它只负责布局与状态转发：窗口状态、主题、搜索文本、当前页、采样状态和关闭生命周期由 `TaskManagerWindow`/`TaskManagerView` 持有；真实采集、SCM、启动项和操作服务继续由既有依赖注入提供。

### `CustomTitleBar`

标题栏包含产品图标与名称、拖拽区、搜索入口/搜索框、全局摘要和三个窗口命令。拖拽区必须调用 `Window.DragMove`，双击调用最大化/还原；最小化、最大化/还原和关闭调用 `Window.Minimize`、`Maximize`/`Restore`、`Close`。按钮固定 46px 命中宽度，提供中文工具提示和可见焦点；窗口处于最大化时按钮图标随 `WindowStateChanged` 更新。

标题栏使用 `Borderless(true)` + `ExtendClientAreaTitleBarHeight(48)`，窗口边缘通过 `DragResize` 保留缩放。搜索框和摘要不能进入窗口按钮列；按钮区域预留固定列，避免窗口宽度变化时覆盖输入。DWM backdrop 仍是增强效果，失败必须保留不透明表面。

### `GlobalSummaryBar`

摘要栏统一显示 CPU、物理内存、进程数和采样状态。数值由 `SnapshotService.Current` 更新，预热/暂停/断连显示文字状态和“—”，不得用 0 代替未知。刷新只更新文本，不重建搜索框或抢焦点。搜索框保持单一实例，页面切换时保留文本和光标；搜索行为由页面过滤器决定，公共框架只发布变更事件。

### `SidebarNavigation`

导航项是不可变的 `NavigationEntry(Key, Title, Glyph, Kind)` 数据，统一生成线性 Fluent 字形、可见中文标签、工具提示和选中状态。默认项顺序为进程、性能、应用历史记录、启动应用、用户、详细信息、服务、系统信息；设置作为底部 footer，避免和业务页混在同一组。

导航项高度 44px、内边距 8～12px，选中态使用浅蓝底和独立蓝色指示条，焦点边界独立于选中态。紧凑态只隐藏标签并保留图标、工具提示、键盘名称和 `aria-current` 等可访问语义；不能只把 `NavigationList.Width` 改为 64px 而留下 204px 空白列。

### `PageHost` 与 `PageHeader`

`PageHost` 维护一次性构建的页面集合，切换页只改变 `IsVisible`，不因采样刷新重新创建控件。每页使用相同的 `PageHeader`：页面标题、可选副标题/采样状态和唯一命令栏。标题字号约 28px，副标题 14px，标题栏底部有轻分隔线；命令栏从页面声明中生成，避免每个页面重复拼接 DockPanel。

页面命令必须包含稳定的文字、图标、禁用原因、忙态和真实反馈。危险动作沿用现有确认框与身份重验；公共框架不能把按钮状态推断为业务成功。

### `CommandBar`

命令栏按钮统一 44px 高度、4px 圆角、灰色表面、黑色文字和 8px 图标间距。每页最多一个主动作，其余使用次级命令；窄宽度时保持按钮边界并把低频命令收进“更多”，不能裁切文字。按钮创建后只挂载到一个父元素，避免 MewUI 多父级异常。

### `DataSurface` / `GridHost`

`DataSurface` 提供白色不透明数据面、8px 面板圆角、轻边框和内容裁切；透明/玻璃只用于窗口壳层。`GridHost` 负责 `GridView` 的统一表头高度、行高、数值右对齐、未知值显示、选中/焦点和内部水平滚动。数据源仍使用现有 `ItemsView` 和真实快照；不能在公共组件中添加示例行或静态曲线。

### `SettingsFooter`

设置入口固定在导航底部，使用与导航一致的图标、选中语义和 44px 命中区。设置页自身继续使用纵向 SettingsCard：采样、密度、主题、侧栏、置顶、入口接管、版本/能力说明。设置状态必须从 `PreferencesService`/真实服务读回；公共 footer 不保存额外状态。

## 共享 Token

| Token | 值 |
|---|---|
| `shellBackground` | 浅色 `#F3F3F3`，深色使用主题 Palette |
| `contentBackground` | 浅色 `#F9F9F9` |
| `surface` | `#FFFFFF`；表格和图表保持不透明 |
| `textPrimary` / `textSecondary` | `#1A1A1A` / `#5F5F5F` |
| `accent` / `focus` | `#0067C0` / `#005FB8` |
| `borderSubtle` / `borderControl` | `#E5E5E5` / `#D1D1D1` |
| `titleBarHeight` | 48px |
| `sidebarWidth` / `sidebarCompactWidth` | 204px / 64px |
| `navHeight` / `commandHeight` | 44px / 44px |
| `pageHeaderHeight` | 72px |
| `panelRadius` / `controlRadius` | 8px / 4px |
| `space` | 4px 基准，常用 8/12/16/24px |

暗色和跟随系统主题只读取 MewUI `Theme.Palette`，组件不能散落新色值。正文和说明目标对比度至少 4.5:1，焦点和有意义图标至少 3:1；减少透明度或 DWM 不可用时使用同一 Token 的不透明表面。

## 当前缺口

- `TaskManagerWindow.cs` 同时承担窗口、导航、页头、九页内容、服务动作和格式化逻辑，公共壳体还没有独立的 `WindowShell`、`PageHeader` 或 `DataSurface` 类型。
- 标题栏已经是自绘按钮，但拖拽区、窗口按钮、搜索和摘要仍由 `BuildTopBar` 一次性拼接；按钮的可访问名称、最大化状态和按钮列需要继续集中处理。
- 根 Grid 的导航列固定为 204px；紧凑侧栏只改变内部宽度会留下空白，需让 `ColumnDefinitions[0]` 与导航内容同步。
- 设置入口当前作为导航数据的一项，底部 footer 语义、页面选中和紧凑态工具提示需要统一。
- 各页面重复创建 `DockPanel`、`Border`、`GridView` 和命令按钮；页面命令栏与数据面的 Token 还没有公共工厂。
- 全局搜索只直接过滤进程/详细信息，公共框架需要保持单一输入并允许页面声明过滤目标，不能把搜索文本错误地应用到启动项或服务页。

## 串行实施顺序

1. 新增 `MewUI/ShellTokens.cs` 与 `MewUI/ShellComponents.cs`，只承载 Token、按钮、标题栏、摘要和数据面工厂；先保持现有业务回调不变。
2. 将 `OnBuild` 和 `BuildTopBar` 迁移到 `WindowShell`/`CustomTitleBar`，完成拖拽、窗口按钮、搜索/摘要列和主题回调；构建并启动真实 Host。
3. 将 `BuildSidebar` 迁移到 `SidebarNavigation`，让紧凑态同时更新根 Grid 列宽、标签可见性、工具提示和键盘焦点；验证设置 footer 与页面选择。
4. 将页面标题/命令行迁移到 `PageHeader`/`CommandBar`，再将表格外层迁移到 `DataSurface`/`GridHost`；每组页面完成后独立提交。
5. 只在公共组件稳定后调整页面细节。页面不得借此改变采集字段、API 合同、操作身份保护或未知值语义。

## 定向验收

- `dotnet build Takao.sln -c Release --no-restore`：公共组件与 MewUI 编译检查。
- 1920×1080/100% 真实 Host：标题栏按钮、拖拽/双击最大化、边缘缩放、搜索焦点、导航选中、设置 footer、窄宽度紧凑列宽。
- 代表页面：进程页真实快照表格、性能页真实采样状态、设置页三主题/密度/侧栏偏好；检查刷新不重建壳体、不抢焦点、不填充模拟数据。
- 记录 `git SHA`、命令、退出码和截图绝对路径；无法运行原生窗口时明确标记未执行，不使用其它分辨率截图替代。

