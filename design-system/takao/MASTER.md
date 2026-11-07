# Takao — Windows Blue Design System

## 2026-11-07 顶部汇总与提示清理

按 [SUMMARY-CLEANUP.md](../../docs/SUMMARY-CLEANUP.md) 覆盖原单行淡灰汇总与内容区InfoBar/toast：顶部56px保持，三组无卡片两行指标使用caption12px/secondary标签和`SummaryMetric=24px`、600、accent数值；数字采用numericFont，CPU/内存保留百分号、进程明确标签。汇总单组宽度建议`summaryMetricWidth=112px`，组间`space3=12px`，通过搜索自适应与窄导航保证现有最小窗口不溢出，刷新不移动其它组。缺失显示“—”及原因，不归零。

删除全部内容布局内通知条及关闭按钮、空白占位和新增成功浮层；真实状态使用已有页头caption位置，失败使用既有字段错误或页头状态，必要时复用有界原生非阻塞错误框。状态有文字、详情可访问，焦点和安全确认保持。搜索框内部placeholder保留。其余Fluent语义色、字阶、默认窗口与1080P显示验收沿用现行规范。

## 2026-11-04 紧凑默认窗口

正常启动尺寸Token为 `defaultWindowWidth=1100`、`defaultWindowHeight=720`（逻辑像素），最小960×640，保留用户缩放/最大化。旧默认1600×950、1380×860及旧测试残留1920×1080迁移为新默认，其余自选尺寸保持。1080P为显示测试环境要求，不要求默认窗口全屏；本节覆盖下方旧窗口画面尺寸条款。保持既有字阶、导航/命令命中尺寸和真实业务内容，数据区按需内部滚动。

## 2026-11-04 当前 Fluent / WinUI 视觉映射

本节与 [FLUENT-DESIGN.md](../../docs/FLUENT-DESIGN.md) 为本轮全部八页的现行视觉规范，优先于下方同日早先Qt映射与历史排版数值。Qt 6.8.3 Widgets/C++17只映射Windows 11 Fluent习惯，不迁移WinUI、不承诺Mica/Acrylic；.NET10/CsWin32/JSON.NET、真实回环HTTP、已有页面/动作/身份保护保持。官方固定源码、技能不匹配后的桌面fallback、逐页调整及FUI01～FUI18验收均见该文档。

- 浅色语义：window/navigation底 `#F3F3F3`，content `#F9F9F9`，surface `#FFFFFF`，surfaceSubtle `#F6F6F6`；textPrimary `#1A1A1A`、textSecondary `#5F5F5F`、textDisabled `#747474`。轻分隔strokeSubtle `#E5E5E5`、控件轮廓strokeControl `#D1D1D1`、必要底边/关键边界strokeKeyline `#8A8A8A`，不再让所有面板共用重灰边框。
- accent/hover/pressed `#0067C0/#005FB8/#00539F`，蓝底白字；中性hover/pressed `#EDEDED/#E4E4E4`，selection/selectionHover `#E8EEF5/#DEE8F2`，独立2px focus `#005FB8`。success `#107C10/#DFF6DD`、warning `#8A4B00/#FFF4CE`、danger `#A4262C/#FDECEE`、info `#005FB8/#E8EEF5` 为前景/背景对；状态有文字。图表继续白底、grid `#ECEFF2`、蓝实线 `#0067C0` / 灰虚线 `#58636F`。
- 字体使用本机Segoe UI Variable Text/Segoe UI与系统Microsoft YaHei UI中文fallback；caption12/16、body14/20、section20/28、pageTitle28/36、resourceTitle/metric32/40（px/行高），正文400、强调600；数字等宽，不下载在线字体。
- 4/8px节奏；侧栏204/收起56px，导航与主命令44px命中；顶部56px、搜索480×36px、页头命令栏min56px、内容边距左右24/上16/下24px；当前普通列头40px，若采用已有同帧system来源的可选汇总列头则64px，数据32/40px。保留现有真实全局CPU/内存摘要，不虚构字段或新增汇总卡片。控件圆角4px、面板/弹层8px，卡片无常驻阴影。
- 导航默认透明无框，中性选中底+3px蓝指示，焦点独立；单页一个标题/命令栏，至多一个蓝主动作；表格白底、轻分隔、清晰排序与选中，所有已有交互保留。性能左资源240px、与主图间16px；主/次图例保留线型和文字，未知/断流不补点。
- 设置改为五组SettingsCard式布局：采样与显示、外观与窗口、任务管理器入口、版本与更新、来源与能力；内容靠左max1080px、组间24/标题下12/卡片间4px、卡片min76px/内边距16px；左标题与说明、右控件240px，路径max440px可选可换行。替代通栏重框QGroupBox和拉满输入的QFormLayout；入口与真实状态不删减。
- 全部选择器展开为原生列表弹层：至少控件等宽、32px行、4px内边距、8px圆角/轻边框，深字配浅蓝选中与中性hover；1080P下当前2/3/6项完整可见，不使用旧菜单上下滚动箭头、单行高度或深蓝白字。键盘Home/End/方向键/Enter/Esc及可访问语义保留。
- 仅1920×1080物理像素/100%测试缩放，正文/说明≥4.5:1，信息图标/焦点/曲线/关键边界≥3:1；原生键盘/可访问名称、busy与真实结果、未知原因、焦点恢复均须验证。FUI与Q系列共同记录真实证据，历史截图不挪用。

## 2026-11-04 Qt 6 Widgets 当前映射与1080P验收

桌面改为Qt 6 Widgets + .NET；已有Windows中性浅色、蓝色强调、八页侧栏、单一页头/命令栏、性能资源双栏方向保持。原CSS/Tailwind Token是历史表达，当前在Qt集中主题（QPalette/QSS/绘图常量）映射相同语义，禁止页面散落颜色/字号；不会通过Qt WebEngine复用网页。后续旧Electron/React/DOM条款只保留设计意图，具体实现合同见 [QT-MIGRATION.md](../../docs/QT-MIGRATION.md)。

Qt当前使用以下角色：surface白色，app/sidebar `#F3F3F3`，text `#1A1A1A`，text-secondary `#5F5F5F`，accent/chart-line `#0067C0`，focus `#005FB8`，control-border `#8A8A8A`，selection `#E8EEF5`，chart-grid `#ECEFF2`，chart-secondary `#58636F`（虚线）；success/warning/danger沿用下方语义色。正文、说明、表格文本均≥12px，桌面正文13～14px、命令14px、页标题24px/600、资源数值32px，系统Segoe UI/Microsoft YaHei UI本地字体；标题层级和控件密度在八页一致。

间距按4/8px节奏；侧栏204px，主操作/导航命中高度44px；高密度数据行32/40px保留Qt model/view有界绘制，不能把手机触控44pt规则机械放大数据表。数字右对齐、等宽数字，未知“—”附可读原因；图例/数值/状态文字不得仅依赖颜色。图表白底、蓝实线/深灰虚线，实际时间缺口不补点；无需装饰性位移动画。

使用原生可访问输入/按钮/菜单/对话框，图标控制提供中文accessibleName，装饰图标不重复命名，焦点2px清晰可见；Tab顺序与视觉顺序一致，模态关闭回到发起控件，Shift+F10/菜单键与右键等效，列宽拖动有菜单/键盘替代。单页标题与表格不得随刷新抖动、丢失搜索焦点或选中身份。

本轮按用户指定ui-ux-pro-max验收：正文与说明≥4.5:1，有意义图标/焦点/曲线≥3:1，动作有忙态/真实结果、不可用原因与恢复入口；仅测试1920×1080物理像素/100%测试缩放，覆盖下方旧960/1380/125%要求。沿用浅色产品范围，不增设暗色或移动端验收；保留历史记录，不用历史截图宣称Qt通过。

## 2026-10-31 启动应用状态徽标

启动页状态列与详情共用紧凑的图标加文字徽标：已启用使用 success/success-bg 和 CircleCheck；已禁用使用 text-secondary/surface-subtle 和 CircleMinus；无法确定使用 warning/warning-bg 和 CircleHelp。所有状态保留中文文字及真实 enabledReason，图标 aria-hidden，不增加 aria-live。复用 status-badge 的 space-half/space-1/space-2、radius-small，并以 body/line-height-body 和 icon-sm 显示，徽标小于既有32/40px行高；业务“已禁用”不使用控件 disabled 或整行透明度。沿用现有颜色且文字对比目标≥4.5:1，不增加设计 Token；本节覆盖2026-10-28“不新增徽标”的旧限制。范围与只编译、不打开 UI 的验证边界见 [STARTUP-STATUS.md](../../docs/STARTUP-STATUS.md)。

## 2026-10-31 启动应用右键菜单

启动项行菜单复用既有 ContextMenu 的 surface、control-border、shadow-popover、radius-control、width-context-menu、height-menu-item、body、hover、disabled 和可见 focus Token，不新增色值或几何量。鼠标右键与 Shift+F10/ContextMenu 等效，菜单固定目标；方向键、Home/End、Enter/Escape、外部关闭和有条件的触发行焦点恢复保持。离线、忙态及无法可靠定位有禁用原因。规格及本轮仅 C# 测试、前端编译检查的边界见 [STARTUP-CONTEXT-MENU.md](../../docs/STARTUP-CONTEXT-MENU.md)，不以源码审查替代 UI 运行时证据。

产品标题统一为 **Takao**，标题区与原生窗口、安装显示名一致；不另加中文译名或版本徽标。命名及稳定兼容身份见 [CONVENTIONS.md](../../docs/CONVENTIONS.md)。

## 2026-10-28 启动应用状态

启动页状态列及详情复用现有body/caption/text-secondary、MissingValue、表格选中/焦点Token，显示实际“已启用”“已禁用”或“无法确定”并可读来源原因；不新增颜色/徽标或视觉Token，不只用颜色区分。保留单一命令栏、官方启动设置、全部列排序和Windows标准表格布局。来源及S系列见 [STARTUP-STATUS.md](../../docs/STARTUP-STATUS.md)。

## 2026-10-27 白色简约图表（当前图表配色优先）

全部性能主图、逐逻辑处理器图及资源磁贴小曲线采用纯白图面（surface）、中性浅灰网格（chart-grid `#ECEFF2`）与边界（border），主序列为Windows蓝色实线（chart-line `#0067C0`），次序列为深灰虚线（chart-secondary `#58636F`）。大小曲线、数据点和图例同步读取序列Token；小曲线使用中性边界。轴、单位、图例文字和等待说明沿用text-secondary。保持既有线宽、布局、单位和真实时间缺口，磁盘继续仅显示读取/写入；表格热度和外壳Token仍按各自语义使用。曲线对白底对比≥3:1、标签≥4.5:1；主/次线实测5.674/6.124:1，标签6.385:1。完整角色、状态与C系列验收见 [CHART-PALETTE.md](../../docs/CHART-PALETTE.md)。

## 2026-10-27 磁盘型号、类型与吞吐展示

性能页保留资源双栏；磁盘右侧只保留一张读取/写入吞吐图，复用既有实线/虚线、双图例、字节/秒速率轴和chart Token。移除活动时间事实项、活动率大图、小曲线及说明文案；左磁贴小曲线改用同一真实读/写历史，摘要分两行显示读/写速率，名称及类型/总线仍可辨认。磁盘磁贴最小高度为height-resource-tile加space-6（96px），允许内容自然增高；其他资源保持72px，避免双速率文字挤压。磁盘型号在资源标题处以既有subtitle/body层级显示完整可选择文本，事实列表明确“型号”“类型”“总线类型”。搜索覆盖设备字段。长型号按既有overflow-wrap换行；缺失使用现有MissingValue与可访问原因，不新增配色、字号、徽标或卡片墙。复用secondary/accent/space/事实列表Token，按960×640、1380×860、实际Electron125%和reduced-motion检查。来源/边界及D01～D08见 [DISK-IDENTITY.md](../../docs/DISK-IDENTITY.md)。

## 2026-10-26 GitHub更新入口

确认有更新时在展开侧栏底部设置右侧放44px Download图标，复用Lucide及现有accent/hover/focus/disabled/空间Token；悬浮/聚焦可读版本，下载进度具文字和aria语义。紧凑侧栏可上下排列；无更新不留空占位。完成后复用Modal，说明版本/校验/关闭重启，提供“立即更新”“下次启动自动更新”，初始焦点后者；等待现有业务Modal结束再显示。960×640/125%正文内滚、按钮可达，reduced-motion去除非必要旋转。行为/错误/安装资格及U系列见 [GITHUB-UPDATES.md](../../docs/GITHUB-UPDATES.md)。

## 2026-10-26 已有入口接管

复用现有设置行、宽 Modal、accent 主动作、中性次动作、warning 状态、body/caption/path 和空间 Token。其他工具/特殊配置提供“接管现有入口…”；确认清晰展示当前命令与本目录目标，说明覆盖现有入口、取消只恢复 Windows 标准任务管理器且不恢复旧工具。初始焦点为“暂不更改”，原生 Escape/Tab/焦点恢复保持；忙/错误/成功有文本反馈。真实路径可折行，960×640/125%时对话框内部滚动、底部操作可达；无新增色值、字号或视觉体系。首次提示可前往设置明确接管，合同及 T 系列见 [TASK-MANAGER-TAKEOVER.md](../../docs/TASK-MANAGER-TAKEOVER.md)。

## 2026-10-25 首次启动替换提示

正式桌面首次提示复用既有 Modal/宽对话框、surface、accent、caption、body、subtitle、space 和 command-button Token；标题“替换标准任务管理器？”，一个蓝色主动作与中性“暂不替换”。标题/正文/影响说明/实际状态/底部动作分层，真实路径可折行，读取/写入/错误均有文本反馈。原生 dialog 的可见焦点、Tab 约束、Escape 暂不和焦点恢复保持；初始焦点放暂不，避免回车误改全机设置。960×640/125%时限制视口内高度并内部滚动，底部动作始终可达，遵循 reduced-motion；不新增插画、渐变、颜色或字号。行为及 F 系列见 [FIRST-LAUNCH-INTEGRATION.md](../../docs/FIRST-LAUNCH-INTEGRATION.md)。

2026-10-26 补充：首次真实入口读取完成前不挂载对话框，避免已接管时先闪现；当前目录正常启用时静默保存已完成选择，不展示“继续使用”确认。其他状态及读取错误继续采用既有提示、焦点与反馈，无新视觉 Token。

## 2026-10-25 网络吞吐增量

进程/详细信息网络列复用既有数值、caption、热度、缺失值与表头Token，显示真实接收+发送B/s～GiB/s，原合计列key/宽度/排序偏好保持。提示和详情概览可读接收/发送/合计、独立来源时刻与失败原因；未知用“—”并说明，健康完整窗静默才是0。ETW进程与硬件网卡总量口径分别说明，无新配色/字号/页面/大卡片。既有硬件图的当前值、实线接收/虚线发送、单位与真实时间缺口保持。规格及N系列见 [NETWORK-THROUGHPUT.md](../../docs/NETWORK-THROUGHPUT.md)。

## 2026-10-24 标题与数据视觉层级（当前排版优先）

按用户“设计太单一、标题不突出”的反馈，采用 Fluent UI 的清晰字阶加强既有 Windows 布局。完整审查、来源、组件映射与 V 系列验收见 [VISUAL-HIERARCHY.md](../../docs/VISUAL-HIERARCHY.md)。本节仅覆盖对应排版/表头表面规则，48px 标题区、56px 命令栏、36/58px 表头、32/40px 数据行及真实数据语义继续有效。

```css
--font-size-title: 24px;
--font-size-section: 16px;
--line-height-title: 32px;
--line-height-section: 24px;
--color-table-header: #F6F8FB;
--color-table-header-selected: var(--color-selection);
```

- 每页 h1 使用 title/semibold/title 行高；设置一级分组改为 h2，section/semibold/section 行高，标题下12px、组间24px。设置项 body/medium，说明 caption/secondary；可展开分组标题保持同一视觉层级和原生交互。
- 汇总表头 subtitle/semibold/accent；列名 body/medium/text，表头为 table-header，排序列 table-header-selected、accent/semibold，并保留方向箭头与 aria-sort。全部正常文字对比≥4.5:1，静态强调不能覆盖键盘焦点或改变几何量。
- 性能关键事实 metric/semibold，标签 caption/secondary；资源名称保持既有32px。应用名 control/semibold，选中导航和资源名称 accent/semibold；已有计数改为无边框普通次级文本，不能呈现按钮外观。
- 可使用既有3px accent短竖线标记页面标题；不得引入营销页头、额外工具栏、卡片墙、渐变或采样闪烁。

## 2026-10-24 CPU 图表模式与 HWiNFO 增量

- CPU主图双击切换总体/逐逻辑处理器；可见原生选择控件有相同状态、键盘操作与焦点，复用height-control/body/caption/accent/selection。小图固定0～100%，有真实处理器组/索引及当前值；未知原因不只依赖颜色。
- 逐CPU网格复用space-2/3、chart-line/chart-grid/control-border，单格最小宽度140px、高度112px，映射为 `--width-cpu-chart-min:140px`、`--height-cpu-chart:112px`。只在资源内容区纵向滚动，不挤出页面。
- HWiNFO单资源入口复用resource-tile，显示状态/读取数量，不造小曲线；右侧用既有密度表格与分组/来源说明，数值tabular-nums，单位可读，min/max/avg明确是HWiNFO会话。无新配色、字号、阴影或独立卡片墙。完整合同见 [HWINFO-CPU-GRAPHS.md](../../docs/HWINFO-CPU-GRAPHS.md)。

## 2026-10-24 Windows 11 标准布局（当前优先）

用户要求全部八页使用 Windows 标准任务管理器布局。具体逐页结构、列顺序、命令栏及研究来源见 [WINDOWS-LAYOUT.md](../../docs/WINDOWS-LAYOUT.md)。以下几何增量覆盖旧版允许的响应式图表网格、横向资源类别、重复页头、多层操作栏及常驻底部状态栏。已有颜色/数据状态/调宽排序/身份保护继续有效；Windows 蓝色用于选择、图表和热度，主体改为 Windows 中性浅色结构面。

```css
--height-titlebar: 48px;
--height-toolbar: 56px;
--height-table-header: 58px; /* 仅带资源汇总的进程/用户 */
--height-table-header-plain: 36px;
--height-control: 32px;
--height-controls: 32px;
--height-nav: 40px;
--height-nav-toggle: 48px;
--height-statusbar: 0px; /* 取消常驻底栏，不作为浮层定位的唯一间距 */
--width-sidebar: 204px;
--width-sidebar-collapsed: 48px;
--font-size-title: 20px;
--width-search: 400px;
--width-search-min: 240px;
--width-search-max: 480px;
--height-search: 32px;
--width-resource-list: 220px;
--width-resource-list-compact: 184px;
--height-resource-tile: 72px;
--width-resource-sparkline: 64px;
--height-resource-sparkline: 40px;
--height-performance-chart: 280px;
--height-performance-chart-compact: 220px;
--height-setting-row: 56px;
--width-settings: 960px;
--color-app: #F3F3F3;
--color-titlebar: #F3F3F3;
--color-sidebar: #F3F3F3;
--color-surface-subtle: #FAFAFA;
--color-text: #1A1A1A;
--color-text-secondary: #5F5F5F;
--color-text-disabled: #747474;
--color-border: #E5E5E5;
--color-control-border: #8A8A8A;
--color-selection: #E8EEF5;
--color-hover: #ECECEC;
```

- 外壳仅标题栏+内容两行；设置仍在导航底部。普通连接信息放更多的“采样状态”；暂停/冻结/断连/过期/保存失败用有条件InfoBar，不因移除底栏丢失状态和最后成功时刻。浮层从视口底部按space定位，不能随statusbar=0贴边。
- 普通页一条命令栏；次命令、筛选、树、列、导出、重置和口径在更多/弹出层内。命令不可换行形成第二栏；窄窗按重要性溢出。更多视图操作不依赖选中行。
- 菜单/导航图标20px，行内/更多图标16px；标题栏产品标识20px。表格行继续32/40px，不改虚拟估算与列宽持久化。普通表头采用plain Token，DataTable估算必须读取对应的实际表头高度。
- 性能统一左侧逐资源磁贴+右图/事实两列；左列CPU、内存、每磁盘、每网卡、每GPU，真实小图与大图同源。禁止横向资源页签/指标卡片墙。
- 设置直接使用常规/视图/入口/偏好/关于等Windows设置分组，只有一个页头。正文13px/说明12px遵循桌面密度；不套用技能营销模板、远程字体、手机safe-area或新增暗色主题。

## 1.1组件与Token增量

本节扩展既有视觉而不改变参考布局/Windows蓝色方向。实现先映射这些CSS变量/Tailwind语义值；组件禁止新硬编码Hex/RGB/阴影/动画。已有Token适用于全部新增菜单、详情、资源选择器、树及CSV反馈。

```css
--width-context-menu: 248px;
--width-detail-panel: 820px;
--width-dialog-wide: 720px;
--width-column-name: 272px;
--width-column-name-detail: 240px;
--width-column-pid: 80px;
--width-column-number: 112px;
--width-column-path: 420px;
--width-column-text: 180px;
--width-resource-list: 200px;
--height-menu-item: 32px;
--height-tab: 40px;
--height-chip: 24px;
--height-chart: 168px;
--space-tree-indent: 20px;
--size-tree-toggle: 24px;
--size-process-icon: 16px;
--stroke-icon: 1.75px;
--border-width: 1px;
--focus-width: 2px;
--focus-offset: 2px;
--z-sticky: 2;
--z-menu: 20;
--z-toast: 30;
--z-modal: 40;
--shadow-popover: 0 8px 24px rgb(15 35 60 / 16%);
--font-size-subtitle: 20px;
--font-size-resource: 22px;
--font-size-large-metric: 32px;
--height-detail-content: 420px;
--height-popover-max: 420px;
--width-detail-label: 172px;
--height-controls: 44px;
--font-weight-medium: 500;
--space-7: 28px;
--space-8: 32px;
--space-half: 2px;
--radius-small: 3px;
--width-settings: 950px;
--width-setting-control: 130px;
--width-dialog: 480px;
--height-switch: 24px;
--width-switch: 44px;
--size-switch-thumb: 14px;
--size-process-icon-frame: 21px;
--width-selection-edge: 3px;
--height-empty-state: 240px;
--width-readable: 470px;
--width-search: 460px;
--width-window-control: 46px;
--stroke-chart: 1.8px;
--dash-chart-secondary: 6 4;
--line-height-relaxed: 1.6;
--shadow-selection: inset 3px 0 var(--color-accent);
--width-brand: 280px;
--width-brand-min: 224px;
--width-brand-compact: 208px;
--width-brand-small: 160px;
--width-search-compact: 420px;
--width-action-min: 88px;
--width-toast: 660px;
--size-status-dot: 6px;
```

- 详情宽度`min(--width-detail-panel, viewport - 2×--space-6)`；内部页签、内容滚动，长路径换行/可选择。窄窗口不挤压后方列表；关闭后恢复发起行焦点与滚动。详情里普通标签用caption/body，数值用tabular-nums，不堆24px指标卡。
- 右键/更多菜单使用surface、control-border、shadow-popover、radius-control；普通项text/hover，危险项danger，禁用项text-disabled+原因。菜单方向键/Home/End/Enter/Escape、Shift+F10可用；菜单开时固定目标；菜单不能盖住后还漂移跟随动态行。
- 树展开按钮24px命中区，SVG使用icon-sm，缩进只由space-tree-indent乘层级；深树名称列内部裁切/提示，不能扩大整个页面。层级语义/展开状态可访问；名称和PID不可被列选择隐藏。
- 冻结/暂停分别使用warning文字+warning-bg，明确状态/时刻/恢复操作；连接成功使用success。锁定/运行状态不能仅用色点；热度仅真实有效指标按原色阶。
- 密度切换唯一来源为height-row-compact/standard，虚拟估算与CSS统一读取；菜单、快捷键提示、列选择器、筛选器统一control尺寸与space-2/3/4，不新增每页不同规格。
- 主命令保持可见；低频操作合并“更多”且有菜单名称。窄窗口工具栏允许折到已命名菜单，不能横向溢出页面或隐藏唯一入口。
- 图标优先真实Shell应用图标；失败使用统一AppWindow，禁止猜品牌。位图固定size-process-icon并保留布局，延迟加载不引起行高变化。业务徽标与导航仍统一Lucide线性SVG。
- 资源曲线遵守原chart Token；GPU/物理磁盘/适配器选中状态复用selection+accent。图例说明实际来源/单位与当前接口，不以空曲线冒充0。
- GPU资源名称优先显示Windows DXGI真实描述；选择器主文字复用font-size-body/font-weight-semibold，LUID、PDH phys索引与软件标志使用既有caption/text-secondary。标题复用subtitle，长名称按既有overflow-wrap换行，不增加自定义字号/颜色。名称失败才回退LUID并显示原因；保留次级LUID区分同名设备，不猜Windows GPU编号；GPU合计口径包含软件适配器，不一概称实体硬件。
- 无障碍验收继续桌面范围：960×640、1380×860、125%缩放、reduced-motion、键盘与焦点；不套用手机safe-area或强行新增暗色主题。所有新增字号≥12px；颜色对比标准沿用正文4.5:1、边界/焦点3:1。

版本1.1（含原1.0基础与本轮增量）；适用于全部业务页与设置。来源：用户参考图的信息结构 + Windows Fluent蓝色方向 + 已核验的UI/UX Pro Max通用可访问性/实时监控建议。系统查询返回的营销模板未采纳，详见docs/RESEARCH.md。本文件是视觉Token的唯一源，页面覆盖不得削弱状态正确性和可访问性。

## 1. 视觉原则

信息优先、密集可扫读、蓝色表达选中和资源热度。顶栏/侧栏/主数据区结构接近参考图；不使用巨型营销标题、玻璃模糊、渐变背景或装饰性图表。默认浅色Windows蓝色，暗色不属于本版必须功能；没有完整暗色Token和独立对比检查前不暴露主题切换。

## 2. 颜色 Token

所有值通过全局CSS变量声明，再映射Tailwind语义名称；组件禁止散落Hex。普通正文与次级正文在对应面上至少4.5:1；有意义图标/焦点边界至少3:1；弱分割线可以低对比，但不能承担可点击边界或唯一状态。

| Token | 值 | 用途 |
|---|---|---|
| --color-app | #F3F7FC | 应用背景 |
| --color-titlebar | #EAF2FC | 标题区 |
| --color-sidebar | #EDF4FD | 侧栏 |
| --color-surface | #FFFFFF | 主表、卡片、弹窗 |
| --color-surface-subtle | #F7FAFE | 列头、空态区域 |
| --color-text | #152A43 | 主要文字 |
| --color-text-secondary | #4B617B | 次要文字 |
| --color-text-disabled | #61758A | 禁用解释（非交互）；白底对比度4.75:1 |
| --color-border | #D5E0EE | 普通分割线 |
| --color-control-border | #778BA3 | 需要明确边界的输入/控件 |
| --color-accent | #0067C0 | 主操作、选中条、链接 |
| --color-accent-hover | #005AA8 | 主操作悬浮 |
| --color-accent-pressed | #004C8F | 主操作按下 |
| --color-on-accent | #FFFFFF | 主按钮文字 |
| --color-selection | #E1EFFE | 选中行/导航底色 |
| --color-hover | #EDF5FF | 行悬浮 |
| --color-focus | #005FB8 | 2px外轮廓 |
| --color-success | #0F6C38 | 连接成功、健康状态 |
| --color-success-bg | #E6F4EA | 成功标签面 |
| --color-warning | #835400 | 预热、受限、过期文字 |
| --color-warning-bg | #FFF4D6 | 警告提示面 |
| --color-danger | #B4232C | 错误/结束任务 |
| --color-danger-bg | #FDECEE | 错误提示面 |
| --color-heat-0 | #F5F9FF | 真实资源热度0级 |
| --color-heat-1 | #E8F2FE | 真实资源热度1级 |
| --color-heat-2 | #D4E8FC | 真实资源热度2级 |
| --color-heat-3 | #B8D9FA | 真实资源热度3级 |
| --color-heat-4 | #8FC0F1 | 真实资源热度4级 |
| --color-chart-line | #0067C0 | CPU/主曲线 |
| --color-chart-secondary | #58636F | 深灰次曲线，虚线并配明确图例 |
| --color-chart-grid | #ECEFF2 | 图表中性浅灰网格；图面使用surface、边界使用border |
| --color-overlay | rgb(15 35 60 / 32%) | 弹窗遮罩 |

热度以绝对实际CPU/GPU百分比0/5/20/50/80映射，内存可按当前可见最大值的比例但须保持数值可读；未知指标不着热度。热度、在线状态都保留文字或数值，不能只凭颜色判断。

## 3. 排版与尺寸

```css
--font-ui: "Segoe UI Variable", "Segoe UI", "Microsoft YaHei UI", sans-serif;
--font-mono: "Cascadia Code", Consolas, monospace;
--font-size-caption: 12px;
--font-size-body: 13px;
--font-size-control: 14px;
--font-size-title: 18px;
--font-size-metric: 24px;
--line-height-body: 1.45;
--font-weight-regular: 400;
--font-weight-semibold: 600;
--space-1: 4px;
--space-2: 8px;
--space-3: 12px;
--space-4: 16px;
--space-5: 20px;
--space-6: 24px;
--radius-control: 5px;
--radius-panel: 8px;
--radius-window: 10px;
--height-titlebar: 52px;
--height-toolbar: 56px;
--height-table-header: 58px;
--height-row-compact: 32px;
--height-row-standard: 40px;
--height-control: 36px;
--height-nav: 44px;
--height-statusbar: 28px;
--width-sidebar: 220px;
--width-sidebar-collapsed: 64px;
--icon-sm: 16px;
--icon-md: 20px;
--shadow-dialog: 0 16px 48px rgb(15 35 60 / 20%);
--duration-fast: 100ms;
--duration-normal: 160ms;
--ease-standard: cubic-bezier(.2, 0, 0, 1);
```

正文13px是按参考桌面密度作出的明确取舍，禁止小于12px；主要按钮14px。所有资源数字启用tabular-nums，重要单位不依赖tooltip。避免在线字体依赖。

## 4. 布局规格

- 框架高度100vh；标题栏固定上方；侧栏在标题栏下占满剩余高度；内容用min-width:0防止表格挤开页面。
- 标题图标20px、标题14px semibold；全局搜索宽度约420～500px并随视口收缩，不盖住窗口操作。
- 侧栏内边距8px，导航间隔4px，选中条3px宽，文本与图标间距12px；设置推到底部。
- 命令栏左右留白16px。页面标题18px semibold，操作36px高；危险按钮只在可操作选择下启用。
- 表格名称列最小240px、PID列80px、状态列100px、数值列100～120px；桌面默认展示的列可随空间收缩但不重叠。表格容器单独overflow:auto，表头sticky。
- 960～1100px时侧栏可折叠为64px，图标保留名称提示与accessible name；视口宽时不把列表限制成居中小卡片。
- 性能页采用左侧实际资源摘要+右侧大图或响应式网格；图表无数据时显式“等待采样”，不预生成60个点。
- 对话框最大宽480px，边距24px，有标题、说明、可见输入label、错误与操作。确认按钮使用危险Token；焦点圈与取消始终可见。

## 5. 组件状态

全页网格交互增量（2026-10-23）：列宽默认值继续使用现有列Token；允许用户调宽时覆盖默认几何量。新增 `--width-column-min:64px`、`--width-column-max:1600px`、`--width-column-resizer:8px`、`--width-column-resizer-line:2px`、`--step-column-resize:16px`、`--step-column-resize-large:64px`。语义列可提高最小宽度，调整柄不占用排序按钮区域。柄默认边界沿用control-border，hover/调整中沿用accent，键盘焦点沿用focus；不新增颜色。宽度变化直接反馈，不做width动画；排序与列宽控件分别聚焦，表头/数据按同一像素列宽对齐。完整行为见 `docs/GRID-INTERACTIONS.md`。

| 组件 | 默认/悬浮/选中 | 键盘/忙/错误 |
|---|---|---|
| 导航 | 平面，hover浅蓝；选中selection底+accent竖条 | aria-current=page，焦点outline不被选中态覆盖 |
| 表格行 | 白/轻分隔，hover浅蓝；选中浅蓝+明确边 | 实际选择语义，刷新保留身份与焦点；不靠每行动画表达采样 |
| 排序表头 | 标签与真实汇总分两层，数值列右齐 | button/aria-sort，可键盘切换，排序方向图标有标签 |
| 主按钮 | 蓝底白字，hover/pressed使用专用Token | disabled语义；忙时短文案和小spinner；不扩大宽度 |
| 次按钮 | 白底、control-border、深色字 | focus2px；禁用给原因，禁止空事件处理器 |
| 搜索 | 可见label或sr-only label，搜索图标装饰 | 清空真实恢复；输入不被刷新抢焦点；结果数不高频aria-live |
| 状态条 | 连接、时间、刷新间隔 | 过期/断线/暂停文字明确，非单色点；有重试动作 |
| 缺失指标 | — +可访问原因提示 | 键盘/鼠标都能获取说明，不填0，不着热度 |

## 6. 图标、动效与可访问性

- 单一SVG线性图标族，16/20px、约1.75px描边；不以emoji作为图标；未知进程使用一致通用图标，不猜logo。
- 只对背景/边框/颜色/透明度做100～160ms过渡；不在每秒数据更新时闪烁或移动行；用户排序是唯一主动重排主因之一。
- `prefers-reduced-motion: reduce` 时去掉非必要过渡/入场；曲线从第一真实点直接可读。
- 正常Tab顺序符合视觉顺序；sticky区域使用scroll-padding保证焦点可见；对话框关后归还焦点。
- 桌面紧凑表格允许32px行；主要导航44px。鼠标密集表格不照搬手机48px单元，但所有操作保留键盘等效路径。
- 前端完成后按pro-rules中的适用桌面项复核，移动端safe-area/手机375px不作为本Windows桌面产品硬性指标。
- 产品标识独立使用原创 `assets/branding/takao.svg`：透明外缘、accent 蓝色圆角块和 on-accent 白色显示器/资源柱；标题区尺寸复用 `--space-7`。导航/操作图标继续使用既有 Lucide。SVG/PNG/ICO、favicon 与原生窗口共用母版，生成和验收见 `docs/PRODUCT-ICON.md`。
