# WinUI 3 玻璃外壳改造阶段设计

日期：2026-11-12。本文是本轮界面改造的阶段交接记录。当前 `main` 工作树的生产入口是
`Takao.Host.exe` 内嵌的 MewUI Win32 + Direct2D 窗口；Qt 文件仍保留在仓库中，但不属于当前
桌面运行链路。因此本轮“WinUI 3 风格”按 Windows 11 Fluent 语义实现，使用 MewUI 的
`ThemeVariant`、`Palette`、`Window.UsesBorderlessSurfaceChrome` 和 Win32 DWM backdrop 能力，
不引入 Web/Electron，也不复制 WinUI 私有协议。

## 已核对的基线

- `src/Takao.Host/Program.cs` 启动时固定 `ThemeVariant.Light`，没有用户可选的 light/dark/system
  偏好，也没有动态主题读回。
- `src/Takao.Host/MewUI/TaskManagerWindow.cs` 只设置窗口标题和尺寸；没有无边框标题栏、亚克力
  材质、窗口按钮命中区或拖拽区域。
- `BuildSidebar()` 以纯文本 `ListBox` 展示页面，缺少页面图标、选中指示、紧凑模式和可访问的
  图标替代文字；设置只是底部文本，不是可导航项。
- 当前 MewUI 页面已经接入真实 `SnapshotService`，但相较历史 Qt 页面合同有明显缺口：性能页只有
  四个摘要行；历史页没有清除/定位命令；启动页没有来源动作；用户页没有跨页定位；详细信息列
  不完整；服务页没有动作/详情；设置缺少密度、侧栏、置顶、主题、入口接管和更新分组；系统信息
  只有一段摘要，没有逐类硬件内容和复制反馈。
- MewUI 0.21.1 已公开 `ThemeVariant.System/Light/Dark`、`Palette`、`Window.AllowsTransparency`、
  `Window.UsesBorderlessSurfaceChrome`、`IWindowBackend.SetAllowsTransparency` 和
  `IWin32WindowSurface.TransparentComposition`。亚克力效果应先走这些能力，再由 CsWin32 调用
  `DwmSetWindowAttribute` 设置系统 backdrop；如果当前 Direct2D 后端无法保留 alpha，回退到不透明
  的 Fluent surface，并保留相同的边界和对比度。

## 设计约束

1. 主题偏好只允许 `light`、`dark`、`system`，JSON.NET 校验失败时回退 `system`，不把主题状态写入
   快照或 API 令牌。主题切换必须更新 Palette、窗口材质和标题/侧栏/内容对比度。
2. 标题栏高度 48px，产品图标 20px，窗口按钮 46px 命中区；拖拽区不覆盖搜索框、导航和按钮。
   关闭、最大化、最小化走真实 HWND 行为，焦点和 Esc 语义保持。
3. 玻璃材质只用于标题栏与侧栏等壳层；表格/图表内容保持不透明 surface，避免透明背景降低数据
   可读性。文本对比至少 4.5:1，焦点边界至少 3:1；系统减少透明度或 DWM 不可用时使用不透明
   主题回退。
4. 导航图标使用单一 Fluent/线性 SVG 族，16/20px；图标旁有可见标签，折叠时保留 accessible name
   和 tooltip，不能用 emoji 或只靠颜色表达当前页。
5. 真实采样、错误、预热和未知值语义不因换肤改变；禁止静态演示数据和把不可用值填为 0。

## 串行实施阶段

### 阶段 2：窗口壳、标题栏、主题、导航图标

唯一写业务代码的代理修改：

- `src/Takao.Host/MewUI/TaskManagerWindow.cs`：拆出 Shell/TitleBar/Nav 组件；加入标题栏窗口操作、
  搜索和摘要保留逻辑；导航项图标、选中条、紧凑模式、设置入口和键盘语义；将每页命令栏保留
  为单行。
- `src/Takao.Host/Models/Preferences.cs`、`src/Takao.Host/Services/PreferencesService.cs`：增加
  `Theme`（`light|dark|system`）字段与兼容默认；JSON.NET 校验、旧配置读回和原子保存。
- `src/Takao.Host/Program.cs`：根据偏好启动 `ThemeVariant`，订阅系统主题变化时重新应用 Palette；
  不改变 headless 行为。
- `src/Takao.Host/NativeMethods.txt` 及新增 `src/Takao.Host/Native/WindowChromeService.cs`：仅声明
  DWM backdrop/extend-frame 所需 CsWin32 API，读回失败时返回明确状态并走不透明回退。
- 如 MewUI 无内建图标源，新增 `src/Takao.Host/MewUI/FluentIcons.cs`，内嵌少量 SVG `IconSource`，
  不引入在线字体或新运行时依赖。

定向验证：`dotnet build Takao.sln -c Release --no-restore`；启动带 `--headless` 检查主题偏好不会
影响 API；桌面 1920×1080/100% 人工检查标题栏拖动、三主题、透明度关闭回退、Tab/Alt+数字和窗口
按钮；记录 `docs/ACCEPTANCE-RESULTS.md` 中 Q04/Q05/Q08/Q09 对应证据。

### 阶段 3：侧边栏主内容逐页补齐

只由下一代理修改 `TaskManagerWindow.cs`（必要时为页面拆分新的同目录类），按页面顺序逐项对照
`docs/WINDOWS-LAYOUT.md` 和历史 Qt `MainWindow.cpp`：

1. 进程：真实图标、状态/CPU/内存/I/O/网络/GPU/PID 列，运行/结束/效率/更多命令。
2. 性能：左侧 CPU、内存、每个磁盘/网卡/GPU 资源磁贴，右侧真实历史曲线和事实区，未知/预热保留。
3. 应用历史：会话起点、CPU/I/O 累计、清除统计和定位仍运行进程。
4. 启动应用：来源/命令/状态/位置列，复制命令、打开来源和官方 Windows 设置入口。
5. 用户：会话、状态、CPU、内存、进程数以及查看此会话进程的真实跨页定位。
6. 详细信息：用户、CPU、内存、优先级、线程、父 PID、会话和映像路径等真实列，保留身份保护。
7. 服务：名称/PID/显示名/状态列，启动/停止/重启/详情/定位关联进程命令只走既有 SCM 服务。
8. 设置：常规、视图、主题、任务管理器入口、偏好/帮助、更新和关于分组；控件状态反映真实读回。
9. 系统信息：CPU、显卡、主板、内存、磁盘、显示器、Windows 分组，刷新/复制反馈和缺失原因。

每页完成一个小提交再交给验收代理；不改变采集器、API 合同或操作身份校验。定向验证使用真实
快照，覆盖空态、预热、权限拒绝、搜索/排序/键盘和 1920×1080 页面截图；只清理本次创建的测试进程。

### 阶段 4：定向验收与修复

独立代理运行 Release 构建、1920×1080 真实窗口检查和受影响的 Q04/Q05/Q08/Q09；补充主题切换、
窗口关闭/端口释放、导航图标可访问名称、页面缺失元素对照表。未能运行提升窗口或 DWM 的项标记
“未执行”并写明原因，不能用静态构建通过代替截图或真实链路证据。

## UI/UX Pro Max 取舍

检索到的 glassmorphism 规则要求 10–20px 模糊、低不透明度和细边框，但同时标记对比度、键盘焦点、
减少透明度/动效为必验项。本产品采用低强度亚克力只做壳层，数据区不透明；图标使用单一线性族，
所有交互控件保留可见焦点和键盘等效路径。检索到的营销式 Hero/CTA 方案不适用于任务管理器，继续
遵循现有 Windows 标准布局。
