# 标题与数据视觉层级审查

日期：2026-10-24。用户反馈“设计太单一，没有突出标题等元素”。本增量针对现有八页的排版、强调和表面区分；延续 [WINDOWS-LAYOUT.md](WINDOWS-LAYOUT.md) 的 Windows 11 任务管理器结构，Token 以 [MASTER.md](../design-system/takao/MASTER.md) 顶部增量为准。

## 1. 审查结论

当前已有页面标题，问题是局部层级被压平：多数内容集中在 12～14px、400～500 字重及同一白色面上，缺少稳定的阅读顺序。应形成“页面标题 → 分组/关键指标 → 控件与表格正文 → 来源说明”四层，而不是增加更多装饰颜色。

| 位置 | 源码现状 | 阅读影响 | 修正 |
|---|---|---|---|
| 每页命令栏 | `.page-title h1` 为 20px/600，操作为 13px | 已有层级，但页面锚点偏弱 | 标题 24px/600，固定单行，保持 56px 命令栏 |
| 设置分组 | `.settings-section h3` 为 13px/600，设置项为 13px/500，说明 12px；页面 h1 直跳 h3 | 组名接近普通设置项，扫描时不易分段；标题语义跳级 | 分组使用 h2、16px/600，24px 行高，组间 24px；说明仍 12px |
| 汇总表头 | `.column-metric` 为 20px、继承 400；列名 12px/400/次级文字，表头白底 | 实时总量缺少强调，列名与行内容区分弱 | 汇总 20px/600/蓝色；列名 13px/500；极浅冷灰表头，排序列用浅蓝面与明确方向 |
| 性能事实 | `.primary-metric` 为 24px、继承 400 | 标签、数值与技术细节的视觉重量接近 | 关键数值 24px/600；普通事实 13px，标签 12px；保留 32px 资源名称 |
| 导航与资源选择 | 已有选中底色、竖条、图标蓝色，导航文字保持正文色 | 所处页面或设备的文字提示偏弱 | 选中文字蓝色/600，同时保留底色、竖条、`aria-current`/`aria-pressed` |
| 应用标识和计数 | Windows 布局覆盖使应用名为 13px/400；页计数有边框与底色 | 应用识别较轻，非操作计数容易像按钮 | 应用名 14px/600；已有计数保留真实文本，去按钮式边框/底色，不新增装饰徽标 |

源码位置：`src/Takao.Web/src/styles.css`、`components.tsx`、`pages/Settings.tsx`、`pages/TaskManagerIntegration.tsx`、`Performance.tsx`。性能设备名称、表头汇总与列表行仍只使用已有真实数据。

## 2. 成熟来源与技能核验

- 已核验 [Microsoft Fluent UI fonts.ts](https://github.com/microsoft/fluentui/blob/master/packages/tokens/src/global/fonts.ts)：12/14/16/20/24/32px 字阶、400/500/600 字重、对应行高。
- 已核验 [Fluent UI typographyStyles.ts](https://github.com/microsoft/fluentui/blob/master/packages/tokens/src/global/typographyStyles.ts)：`subtitle1` 为 20px/600、`subtitle2` 为 16px/600、`title3` 为 24px/600。采用其清晰字阶与语义映射，保留项目桌面表格 13px 密度及本地 Segoe UI/微软雅黑回退，不新增字体下载或 UI 框架。
- 用户指定 ui-ux-pro-max 的 `heading visual hierarchy dashboard --domain ux` 返回 `Heading Hierarchy`、`Heading Line Balance`，对应顺序标题及窄窗自然布局；`semantic heading typography --stack react` 返回语义 HTML 指导。命中结果适用于本 React 19 桌面界面，不使用营销模板。
- 已阅读该技能 `references/pro-rules.md`；适用项为语义 Token、图标一致、可见焦点、稳定交互和文字对比。手机尺寸、手机触控目标、safe-area、暗色模式不扩展本次桌面范围。

可复现查询（技能脚本使用绝对路径）：

```powershell
python 'C:\Users\jieok\.skills-manager\skills\ui-ux-pro-max\scripts\search.py' 'heading visual hierarchy dashboard' --domain ux -n 2
python 'C:\Users\jieok\.skills-manager\skills\ui-ux-pro-max\scripts\search.py' 'semantic heading typography' --stack react -n 2
```

## 3. Token 与组件映射

新增与覆盖的 Token 只在 MASTER 和全局 CSS 声明，组件引用语义值；保留全部既有几何量。

```css
--font-size-title: 24px;
--font-size-section: 16px;
--line-height-title: 32px;
--line-height-section: 24px;
--color-table-header: #F6F8FB;
--color-table-header-selected: var(--color-selection);
```

| 角色 | 字号 / 字重 / 行高 | 颜色与间距 |
|---|---|---|
| 页面 h1 | title / semibold / title | text，现有命令栏左边距 24px；可用既有 3px accent 短竖线强调，不能改变标题行高度 |
| 标题栏应用名 | control / semibold / body | text，不与页面标题争夺视觉重量 |
| 设置 h2、可展开分组标题 | section / semibold / section | text；标题下 12px、组间 24px；保留原生 details/summary 的键盘行为 |
| 设置项 | body / medium / body | text；说明 caption / regular / secondary |
| 表头汇总 | subtitle / semibold / 28px | accent，使用等宽数字；未知仍保留原原因与占位 |
| 表头列名 | body / medium / 现有标签高度 | text；表头使用 table-header，排序列 table-header-selected，排序列文字 accent/semibold，方向箭头与 aria-sort 保留 |
| 性能资源名称 | large-metric / semibold / body | text，保持现有 32px；真实设备描述仍为次级文字 |
| 性能关键事实 | metric / semibold / body | text，24px；普通事实 body / regular，标签 caption / secondary |
| 选中导航、选中资源名称 | body 或 control / semibold / body | accent，保留现有选中底与可访问状态 |
| 页计数、来源、时间与辅助说明 | caption / regular / body | secondary；计数使用普通文本，不画可点击边界 |

表头排序强调应使用样式变化，不能让边框增厚导致列宽或表头高度改变。所有正常文字至少 4.5:1；大标题不以降到 3:1 为目标。选中态、焦点、危险状态仍依赖既有语义 Token，排序不能覆盖焦点样式。

## 4. 保持的布局与交互边界

- 标题区 48px、命令栏 56px、普通表头 36px、汇总表头 58px、数据行 32/40px；DataTable 的虚拟高度估算、持久列宽与滚动行为保持一致。
- 全部八页只有一个 h1；设置一级分组使用 h2，分组下确有子层级才用 h3。详情对话框和性能图标题保留原有语义，不把普通标签伪装为 heading。
- 不增加第二条工具栏、整页背景渐变、营销标题、图标墙、独立指标卡片墙或新的业务字段。设置继续为纵向原生设置行；性能继续资源磁贴列 + 大图 + 事实。
- 所有强调属于静态样式，不随真实采样闪烁、缩放、位移或重放动画；未知值、预热、权限失败、断线提示不被弱化。
- 只调整前端视觉与标题语义，不改变 Windows 采集、JSON.NET、操作权限和进程身份保护。

## 5. 本轮验收

| ID | 检查 | 通过标准 |
|---|---|---|
| V01 | 构建 | 前端 TypeScript 与生产构建通过；按仓库验收执行 Release 构建 |
| V02 | 标题与扫描顺序 | 八页各一个 24px/600 h1；设置 h2 16px/600，不跳过 h2；设置组、项目、说明可清楚区分；性能关键事实与汇总有600字重 |
| V03 | 表头与列表 | 普通/汇总表头仍 36/58px，行高仍 32/40px；排序列的背景/文字/方向与 aria-sort 一致；全部列排序、调宽、滚动无错位 |
| V04 | 尺寸与可访问性 | 1380×860、960×640、实际 Electron 125%缩放与 reduced-motion；标题/按钮不重叠，文档无横向溢出；键盘焦点与更多入口可用；文字对比达到4.5:1 |
| V05 | 真实链路 | 使用生产 Windows 后端逐页打开、共享搜索、排序并查看实时性能；只在专用测试进程上验证实际操作，不伪造接口或截图数值 |
| V06 | 桌面证据 | 正式 Release Electron.NET 真实窗口截图至少覆盖进程、性能、设置及窄窗；关闭本次测试实例并记录清理结果 |

命令、Git SHA、实际通过/未执行项、截图路径写入 `docs/ACCEPTANCE-RESULTS.md`。本轮针对视觉层级回归 A05/A06/A07/A16/A17/A18、B17/B18、L系列受影响项；安装器、全局 IFEO 写入与硬件来源新增能力不因样式修改重复变更。截图比较使用实际采样，数值随时间变化属于正常。
