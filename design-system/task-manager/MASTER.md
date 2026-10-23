# Task Manager — Windows Blue Design System

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
| --color-chart-secondary | #5C3BBF | 次曲线，与主曲线同时有图例 |
| --color-chart-grid | #DFE8F3 | 图表网格 |
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
- 产品标识独立使用原创 `assets/branding/task-manager.svg`：透明外缘、accent 蓝色圆角块和 on-accent 白色显示器/资源柱；标题区尺寸复用 `--space-7`。导航/操作图标继续使用既有 Lucide。SVG/PNG/ICO、favicon 与原生窗口共用母版，生成和验收见 `docs/PRODUCT-ICON.md`。
