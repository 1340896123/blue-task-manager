# Qt + .NET 迁移规格与验收

## 2026-11-04 默认窗口缩小

最新用户要求优先：正常启动默认 **1100×720 逻辑像素**，最小960×640，保留缩放与最大化；Qt尺寸Token和.NET窗口偏好默认一致。旧默认1600×950、1380×860及旧测试残留1920×1080恢复为新默认，其余自选尺寸继续恢复。1080P指显示测试环境，默认窗口无需占满屏幕；下文“1920×1080可见窗口画面”仅保留为既有专项测试模式，不作为正常启动尺寸要求。

日期：2026-11-04。本文为本轮现行技术与显示验收合同；覆盖旧文档中的 React/Tailwind/Electron.NET 实现要求和多分辨率测试要求。旧验收记录保留现场原貌，不作为当前 Qt 版本通过证据。

## 目标与范围

Takao 改为 **Qt 6 Widgets 原生 Windows 桌面界面 + .NET 10 后端**。移除活动 Electron/Electron.NET 依赖、启动方式、打包资源、专用预加载桥和 CDP 桌面测试入口；React/Tailwind 页面由 Qt 原生视图替代，不通过 Qt WebEngine 包装原网页。沿用 Windows 蓝色、八页导航、真实采集、身份保护、管理员入口和既有产品名称。

Qt 负责绘制、导航、表格、图表、可访问性、文件选择和窗口行为；系统采集与系统更改继续由 .NET/CsWin32 完成。不得在 C++ 中另建 Windows 采集器、执行 PowerShell 作为数据源或填充示例数据。C# JSON 继续使用 JSON.NET；Qt 使用 Qt 自带 JSON 类型解析标准 HTTP 交换内容。

## 已核验的成熟实现

已通过 GitHub API 固定并实际读取以下源码，参考设计思路、不复制 TaskExplorer 的 GPL 业务源码，也不移植驱动或专有协议：

| 来源 | 实际读取与采用的设计 |
|---|---|
| [TaskExplorer ProcessView.cpp，9f3f95d22f07f759dd1e0114417529ba51decb01](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/TaskExplorer/GUI/TaskInfo/ProcessView.cpp) | QTreeView 接排序代理与进程模型、列状态恢复、名称/PID必要列；采用 Qt model/view，避免每次采样创建全部单元格控件。 |
| [TaskExplorer ProcessModel.cpp，同一提交](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/TaskExplorer/GUI/Models/ProcessModel.cpp) | 进程身份、父子路径和数值/显示分离；沿用现有 PID+creation FILETIME 身份，不按 PID 单独合并样本。 |
| [Qt 官方 HTTP 示例，c07c2d5a527a644d36e7853d55132ae38921682f](https://github.com/qt/qtbase/blob/c07c2d5a527a644d36e7853d55132ae38921682f/examples/network/http/httpwindow.cpp) | Qt v6.8.3 标签解析至该 commit；QNetworkAccessManager 异步请求，finished/readyRead、abort 与 NetworkError 明确处理，不阻塞 GUI 线程。 |

HTTP/JSON、标准 HTTP 状态、ETag、进程与服务身份合同保持；不新增私有消息协议或远程服务。Qt 动态运行库及其 LGPL/第三方许可随包交付，构建记录固定 Qt 版本、工具链和来源。

## 架构与生命周期

```text
Takao.Host.exe (.NET 10、管理员入口/单实例)
  ├─ Kestrel：127.0.0.1:<port>，既有 /api
  ├─ JSON.NET → 原有服务 → CsWin32 → 真实 Windows
  └─ Takao.Qt.exe (Qt 6 Widgets)
       └─ QNetworkAccessManager → HTTP/JSON → /api
```

- Host 成为唯一正式启动入口，后端就绪后启动同目录可信 Qt 程序；路径由后端决定，不接受网页或请求指定执行程序。
- 保留 `--headless` 供真实 API 检查；此模式不创建 Qt 窗口，不能计作桌面通过。正式模式失败时给出可诊断错误，不静默降级浏览器。
- Qt 关闭使本次 Host 正常停止并释放采样、ETW 和监听端口；Host 退出或启动失败时，本次 Qt 同样退出。只清理本次创建的进程身份，不按进程名称批量终止。
- 保留原有管理员校验、Launcher/IFEO、产品兼容别名和偏好身份。Qt 窗口 PID、Host PID、启动链身份须纳入进程保护，窗口定位/单实例不得继续依赖 BrowserWindow。
- Qt 仅连接 Host 分配的明确回环来源；写操作沿用 `X-TaskManager-Token` 和精确 Origin。令牌仅在本次进程内存，不经 URL、参数、文件或日志传递；由 bootstrap 获取。拒绝外部重定向，不开放 CORS 或局域网监听。
- 请求串行且有界超时；snapshot 支持 ETag/304，304保留快照。后台读取不阻塞 GUI；关闭时取消请求，动作不自动重试。服务控制观察需保留原有较长超时。
- 原生窗口控制采用 Qt 公共窗口 API；系统操作仍走既有 CsWin32 服务。没有实现的窗口缩放或能力必须返回明确不支持，不能返回成功镜像值。

## 页面与交互保留

| 页面 | Qt 迁移合同 |
|---|---|
| 进程 | 真实列表、搜索、数值排序、单选身份、新任务、结束确认/真实退出反馈；已有树、筛选、列、详情、优先级、效率模式、文件动作和CSV功能按现有 API 接入。 |
| 性能 | CPU/内存与逐真实磁盘、网卡、GPU资源；曲线来自最近实际时间样本，CPU总图/逐逻辑CPU可切换；磁盘只展示读/写吞吐，缺失保留间断和原因；HWiNFO可选来源保留状态。 |
| 应用历史记录 | 明确本次会话、真实累计与会话起点；清除统计调用既有后端。 |
| 启动应用 | 实际 Run/RunOnce/Startup 来源、状态文字和原因；详情/来源位置/复制命令/官方启动设置；不执行启动命令，不直接改用户启用状态。 |
| 用户 | 实际 Windows 会话与可归属聚合；不添加未实现的注销/断开入口。 |
| 详细信息 | 相同进程身份的平铺表格与进程操作，搜索/排序/列宽，受限字段逐项说明。 |
| 服务 | SCM真实列表、搜索/详情、预期状态与PID重验的启停/重启、关联进程定位；不自动控制依赖服务。 |
| 设置 | 采样间隔/暂停、显示密度、偏好、窗口/入口状态、产品及更新状态；真实变更反馈，系统接管保持显式确认和读回。 |

界面共用 QAbstractItemModel（或相同 model/view 架构）与代理排序；按原始数值排序，未知值在两方向均末尾。可见行绘制由 Qt 视图负责，禁止大量永久 cellWidget。刷新保留当前输入、排序、滚动、选中 identity；当前进程退出后禁止危险动作。菜单打开时固定目标，恢复发起控件焦点。

## ui-ux-pro-max 验收依据

使用用户指定的 [SKILL.md](C:/Users/jieok/.skills-manager/skills/ui-ux-pro-max/SKILL.md)，已阅读 quick-reference 的可访问性、交互、性能与 pro-rules 的范围说明。Qt 不在其支持的 stack 清单中，不伪称得到 Qt/WPF 栈匹配。保持已有 MASTER 的产品视觉方向，不生成营销设计系统、不覆盖旧 MASTER。

已实际查询并核验：`keyboard focus modal --domain ux` 命中 Focus States、Focus Not Obscured；`dragging movements --domain ux` 命中 Dragging Movements。采用可见键盘焦点、模态焦点恢复、列宽拖动的菜单/键盘替代。`virtualize lists desktop` 与一次重试 `virtualization` 都为零结果，列表性能建议来自 quick-reference 的通用 `virtualize-lists/main-thread-budget` 规则与 Qt model/view 成熟实现，不声称数据库命中。

桌面适用项：正文及说明对比≥4.5:1，承载信息的图标/焦点≥3:1；同系列矢量导航图标，无结构性 emoji；颜色不是唯一状态；可访问名称、字段标签、Tab顺序、快捷键、模态键盘闭环；明确预热/空/暂停/过期/断连/失败与恢复；命令有忙态和完成反馈。主命令及导航采用44px命中高度，数据行沿用桌面32/40px密度，不能机械套用手机触控行高。仅交付既有浅色主题，手机尺寸/横竖屏/safe-area和未提供的暗色主题不在本次验收范围。无装饰性循环动画，降低动态效果时不得出现新增位移动画。

## 仅 1080P 的本轮验收矩阵

显示测试和截图统一为 **1920×1080 可见窗口画面物理像素、100%测试缩放**；不执行旧960×640、1380×860、125%、手机或其它分辨率矩阵。若当前桌面无法取得该画面尺寸，标记该项未执行并报告原因，不用另一尺寸截图冒充1080P。无界面的 .NET 构建/API/原生检查不受显示尺寸限制。

| ID | 必验及证据 |
|---|---|
| Q01 | Release构建 .NET 与 Qt，记录 Qt/.NET/编译器版本与可复现命令；生产启动入口中不再包含 Electron.NET、Electron运行时或 React构建依赖。 |
| Q02 | 生产Host→HTTP/JSON.NET→CsWin32真实采样：PID/身份、CPU/内存、SCM/启动项/会话；null与原因保持，不能用UI行数或静态源码替代数据检查。 |
| Q03 | 正式Qt启动、后端断连/恢复状态、窗口关闭及端口释放；Host/Qt之间无本次孤儿进程。 |
| Q04 | 1920×1080下八页截图；页头突出、Windows蓝色导航选中、无重叠/裁切/页面横向溢出，表格自身滚动可用。 |
| Q05 | 实际Qt控件键盘导航、搜索/排序/选中、菜单与确认框、Esc取消/关闭/焦点恢复；输入与图标操作有可访问名称、可见焦点。 |
| Q06 | 实际资源曲线/当前值/单位/图例；白底蓝实线和灰次虚线、无历史不补点、未知不归零；正文/说明与焦点/曲线对比度计算留档。 |
| Q07 | 在Qt界面创建测试专用进程，验证其真实identity后结束；确认取消不写，完成后进程实际退出、对话框关闭且出现成功反馈。仅清理本次测试目标。 |
| Q08 | 暂停/恢复、间隔/偏好保存和真实刷新不夺焦点；只读页面、可用服务/启动项入口与设置动作有明确反馈。测试不更改用户启动配置或控制用户服务。 |
| Q09 | model/view有界绘制、请求串行、刷新不重置用户上下文；记录可观察的刷新与交互情况，性能数字只写实测值。 |
| Q10 | 发布目录包含Qt platform插件、动态运行库、.NET自包含文件、产品图标和相关许可；安装/打包脚本从Qt目录装载，无残留Electron方式。实际未运行的安装生命周期或更新分支必须标记未执行。 |

结果保存为独立本轮报告，包含 Git SHA、命令、退出码、截图绝对/项目相对路径、真实通过/失败/未执行项目与限制。旧 Electron E2E 因技术不再适用应从活动默认命令移除；可保留历史文档，不能将旧成功数量挪作Qt验收结果。

## 实施阶段

1. 需求、架构、Token与研究：本文及对应现行增量，检查后独立提交。
2. Host去Electron、Qt生命周期与构建发布基础；Qt原生页面按既有API实现，按顺序由阶段代理写业务代码，上一阶段检查提交后交接。
3. 在1080P完成真实Qt E2E、技能标准检查、修复与报告；最终提交可复现结果。常规实现和修复自主推进。
