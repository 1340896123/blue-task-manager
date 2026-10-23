# 1.1 真实链路验收记录

日期：2026-10-23（Asia/Shanghai）。结论：核心原生、浏览器和正式 Electron 工作流已通过；系统入口部分确认；**最终性能验收未完成，不能宣称 1.1 全部验收通过或性能预算已达成。** 管理员服务成功链路和长时压力项目也未计为通过。

完整功能回归对应业务基线 `21ebffd`；`a49a7bf`、`f8725e2`、`ed275a6` 只调整测试观察器和性能测量前提。发布阶段受限进程说明小修 `4ca01bd` 另有真实 Electron 专项 9 项通过；它不等于小修后重新执行全部回归。后续产品图标调整也不自动继承未经重跑的专项结论。

环境：Windows 11 企业版 x64，10.0.26200 / build 26200；.NET SDK 10.0.401；Node 24.16.0；Electron 45.0.0；ElectronNET.Core / Core.AspNet 0.6.0；Playwright 1.64.1。界面版本 1.1.0；需求与视觉依据为 [PRODUCT-V1.1](PRODUCT-V1.1.md)、[验收矩阵](ACCEPTANCE.md) 和 [MASTER Token](../design-system/task-manager/MASTER.md)。

## 命令与实际结果

```powershell
npm run build:desktop
npm run test:regression
npm run test:desktop
npm run test:process-desktop
npm run test:native-desktop
npm run test:system-locations
npm run test:performance
```

首次完整构建实际执行根目录、Web、Electron runtime 的锁文件恢复及 NuGet `--locked-mode`；之后修复使用 `scripts/build.ps1 -SkipRestore`。TypeScript、Vite 和 C# Release 构建成功，0 警告、0 错误；Web 先于 Host 构建，避免压缩静态资源清单引用旧资源。根、Web 与 Electron runtime 的本轮依赖 audit 均为 0 漏洞，这是当时审计结果。

| 套件 | 结果 | 断言数 | 证据 |
|---|---|---:|---|
| static-v11 | 通过 | 3 | [11 组回归汇总](evidence/regression-v11.json) |
| native-api | 通过 | 77 | 同上 |
| native-metadata-v11 | 通过 | 5 | 同上；真实版本资源、账户和 Run 注册表对照 |
| native-core-v11 | 通过 | 25 | 同上 |
| native-management-v11 | 通过 | 64 | 同上；不含管理员服务成功链 |
| native-telemetry-v11 | 通过 | 24 | 同上 |
| gpu-identity-v11 | 通过 | 12 | [GPU 对照](evidence/gpu-identity-v11.json) |
| process-ui-v11 | 通过 | 34 | [真实 Chromium 工作流](evidence/process-ui-v11-results.json) |
| pages-ui-v11 | 通过 | 50 | [页面工作流](evidence/pages-ui-v11-results.json) |
| ui-smoke | 通过 | 36 | [浏览器回归](evidence/final-ui-results.json) |
| recovery-e2e | 通过 | 4 | [断连恢复](evidence/final-recovery-results.json) |
| desktop-e2e | 通过 | 42 | [正式 Electron 桌面](evidence/final-desktop-results.json) |
| process-desktop-v11 | 通过 | 34 | [Electron 进程工作流](evidence/process-desktop-v11-results.json) |
| native-desktop-v11 | 通过 | 22 | [原生窗口与重启](evidence/native-desktop-v11-results.json) |
| system-locations-v11 | 部分确认 | 6 | [Windows 入口](evidence/system-locations-v11-results.json)；4 项受理，3 项实际窗口确认 |
| restricted-process-ui-v11 | 通过（专项） | 9 | [受限进程结果](evidence/restricted-process-ui-v11.json)，对应 `4ca01bd` |
| desktop-performance-v11 | 未通过验收 | — | 三次尝试均未形成满足前提的最终测量 |

11 组回归共 334 项，于当地 07:31 完成；桌面 42 项于 07:32、Electron 进程 34 项随后、原生桌面 22 项于 07:33 完成。系统入口于 07:45 形成部分确认结果。438 项成功断言不等于 438 个独立功能全部完成；受限进程专项 9 项单列，也不包含性能预算。

各组按顺序运行，使用 5191～5202 / 9224～9228 回环端口与隔离的 `TASKMANAGER_DATA_DIR`。桌面从便携目录启动，cwd 故意设为系统临时目录。`test:e2e` 可顺序组织上述常规项目；本次最终性能未完成，不能将总入口记为全部成功。

测试使用真实 Windows 进程、端点、服务权限与 PDH 计数器，无 API mock、固定业务行或预造历史点。结束 / 优先级 / 效率模式操作仅针对测试创建且保存 PID + 创建 FILETIME 的专用目标。令牌只在内存使用，不归档完整机器快照或全机进程清单。

## A01～A19 基础回归

| ID | 结果 | 实际范围 |
|---|---|---|
| A01 | 通过 | 锁恢复、类型检查、生产 / Release 构建；便携输出含 UI 与 Electron runtime。未将全平台安装器或清空全局缓存算作已执行 |
| A02 | 通过 | Windows health、非空列表和实际 Host PID；sampleId / capturedAt 前进 |
| A03 | 通过 | 专用目标的 PID、名称、内存与独立 Windows 查询一致；权限不足保留基础行 |
| A04 | 通过 | 首帧 CPU / 速率为 null 预热；后续真实负载产生 CPU / I/O 增量，无负速率 |
| A05 | 通过 | 真实标题搜索、侧栏、命令栏、汇总表头、虚拟列表与状态栏；Windows 蓝色 Token 已视查 |
| A06 | 通过 | 真 PID 搜索 / 清空与数值排序，CPU / 内存 aria-sort 正确，未知未伪 0 |
| A07 | 通过 | 性能、会话历史、启动、用户、详情、服务、设置均有真实来源与正确导航 |
| A08 | 通过 | 逐进程网络仍未接入；GPU 在 1.1 已接真实 PDH，预热 / 无实例保留原因。进程 I/O 未冒充物理磁盘吞吐 |
| A09 | 通过 | UI 经 CreateProcess 创建真专用目标，独立查询确认；错误保留输入，取消不运行 |
| A10 | 通过 | 先取消再确认结束，只退出目标，独立旁观负载仍运行 |
| A11 | 通过 | 错 FILETIME 409；PID 0 / 4、自身与保护目标拒绝，无误杀 |
| A12 | 通过 | 错令牌 / Origin / Host 拒绝，回环监听，Electron 拒绝外站导航 / 新窗口，renderer 无 Node 全局 |
| A13 | 通过 | 真会话 CPU / I/O 累计与重置；退出记录保留观察语义，不称 Windows 全历史 |
| A14 | 通过 | 真暂停保持样本，周期生效、恢复前进；暂停后丢弃在途帧，采集 / 请求串行 |
| A15 | 通过 | 真停启 Host；保留最后值 / 时间、显示断连，重新 bootstrap 后使用轮换令牌恢复 |
| A16 | 通过（已测范围） | Ctrl+F、Tab / Shift+Tab、Esc 焦点恢复与键盘菜单；未做完整读屏 / WCAG 认证 |
| A17 | 通过 | 实际 Electron 960×640、1380×860、1600×1000、125%、reduced-motion；页面无横向溢出，表格可内部滚动 |
| A18 | 通过 | 正式 .NET → Electron.NET → React → HTTP → CsWin32 → Windows 闭环；任务动作、导航、窗口状态与退出 / 端口清理 |
| A19 | 通过 | 指定技术栈实际运行、CsWin32 生成调用存在；应用 C# JSON 使用 JSON.NET。静态回归未发现业务 mock / System.Text.Json / 手写 P/Invoke；框架内部序列化属于依赖实现 |

## B01～B19 便利性与性能增量

| ID | 结果 | 实际范围与未覆盖部分 |
|---|---|---|
| B01 | 通过 | 真父进程和两子进程树、展开折叠、搜索祖先、父导航与平铺切换；虚拟化有界 |
| B02 | 部分覆盖 | 真树预览、成员 / FILETIME 重验、篡改 409 零误杀、叶到根完成和保护通过；非常态执行中 partial / pending 未可靠触发 |
| B03 | 通过 | 详情按身份刷新，时间 / 线程 / 句柄 / 工作集 / 专用提交 / 用户 / 路径有真源；退出后保留最后信息且禁用写动作 |
| B04 | 通过 | 自有目标真 IPv4 / IPv6 TCP / UDP 与 IP Helper 对照；UDP 无远端为 null，连接不冒充吞吐 |
| B05 | 通过 | 右键、Shift+F10、更多、Enter / 双击与菜单键盘操作；筛选排除旧选择后不操作旧目标 |
| B06 | 通过 | 原生 UI Automation 确认 Explorer 选中唯一测试 exe、属性表识别同文件；复制对应目标。原生截图仅留本地 output |
| B07 | 通过 | 独立 RFC4180 解析真实全量筛选导出，超出虚拟视口且包含折叠的两子进程；BOM / 单位 / 采样时刻 / 未知值语义；真实逗号路径和等号 exe 名防公式注入 |
| B08 | 通过 | 五档非实时优先级对自有目标设置、独立读回、恢复；非法 realtime / 错身份 / 保护拒绝 |
| B09 | 通过 | 冻结仍真采样，暂停单独生效；移除 Playwright 默认焦点模拟后真最小化轮询 ≥5 秒，恢复立即补新 |
| B10 | 通过 | 每页上下文、列 / 密度 / 树 / 资源偏好、坏配置恢复；重启后真实 960×640 客户区、125% 与置顶一致；无 token / 快照 / 暂停 / 冻结落盘 |
| B11 | 通过 | 真选择器选择 / 取消 / 等待 >9 秒；失败保留输入、成功真身份，后续采样仍可正确结束 |
| B12 | 权限受限 | 独立 SC_MANAGER_CREATE_SERVICE 返回错误 5；API 403 / 原因与 UI 禁用通过。成功建服务 / 启停 / 重启 / 超时 / 部分生效链未执行，不操作用户业务服务凑覆盖 |
| B13 | 部分确认 | 真来源 / 完整命令 / 复制和定位；4 官方入口均受理，公共 Startup / 启动设置 / Services 实际窗口确认，当前用户 Startup 前台可见性未确认 |
| B14 | 通过（本机） | 真 PDH GPU / PhysicalDisk 和逐网卡 LUID 对照。RTX 3080 Ti 为实体显卡，Microsoft Basic Render Driver 为软件适配器，不称第二实体 GPU、不猜 Windows 编号；热插拔未测 |
| B15 | 通过 | 相同表示 ETag→304 空体，设置 / 历史变化后新表示；UI 不清空，前台请求串行 |
| B16 | 未通过验收 | 最新同版满历史、≥30 秒、真实可见窗口的有效性能报告未形成；CPU / p95 / 内存预算不得引用旧数据宣称达成 |
| B17 | 通过（已测范围） | MASTER Token、多尺寸、125%、详情 / 菜单、reduced-motion、焦点与文字状态；不含完整读屏认证 |
| B18 | 通过 | 正式 Electron 树 / 详情 / 优先级 / 效率 / 冻结 / 文件 / 导航 / 生命周期；原生 22 项还验证重启偏好与应用关闭后独立任务继续运行 |
| B19 | 部分覆盖 | 真 Shell 图标、按需加载、通用回退、GetPerformanceInfo 来源与短期滚动 / 详情通过；最终整应用性能和长时内存 / 句柄压力未完成，不宣称无泄漏 |

效率模式有专用目标的 ProcessPowerThrottling 与优先级实际读回 / 恢复，不仅是降优先级。恢复依赖本应用会话保存的基线，不覆盖未知外部效率状态。

## Windows 系统入口

探针显式 UTF-8，避免本地编码损坏中文标题；窗口基线含隐藏 HWND，关闭前重验 PID + 原生创建 FILETIME，仅关闭本次新建且核实的窗口。Windows 11 breadcrumb 不总暴露全路径，另用标准 Shell automation 的 HWND / LocationURL 对照。

| 入口 | Shell 受理 | 独立实际窗口确认 |
|---|---|---|
| 当前用户 Startup | 是 | **前台可见未确认**。之后只读 Shell 核对找到精确目录对应后台 / 隐藏 CabinetWClass，不据此宣称可见成功 |
| 公共 Startup | 是 | 可见 Explorer HWND 对应精确官方目录 |
| Windows 启动设置 | 是 | 实际设置窗口含启动标题，原有设置窗口保留 |
| Windows 服务管理器 | 是 | 实际服务标题 / MMCMainFrame，未操作服务 |

结果为 `PARTIAL_WINDOWS_OBSERVABILITY`，不是总 PASS。Windows 自身保留的外部工具进程不计本应用泄漏，清理针对便携 Host / Electron 的确切身份。

## 最终性能：未完成

目标：Host 整机平均 CPU ≤1%、采样 p95 ≤500ms、搜索交互目标 <100ms；需同时记录整套 Electron 资源与测量前提。

最新基线后三次尝试均未形成有效报告：

1. 满 120 点后，32 秒内有效 UI 样本不足 20，稳定测量断言失败。
2. 起始原生前台且 `document.hidden=false`，末尾变 hidden，可见性断言失败，不能混合前后台样本。
3. 使用真实临时置顶并原生读回后，160 秒内未填满 120 点，预热超时；未留足诊断确定原因，不据此断言已证实性能退化或预算通过。

三次均精确清理。当前测试保留置顶读回、开始 / 结束可见与隐藏事件检查，不使用焦点模拟或覆盖 document.hidden；本次没有继续无结论重试。

本地 `output/performance/desktop-v11.json` 仍是 **06:39（UTC 2026-10-22T22:39:02.027Z）** 的旧版定位结果，旧 PASS / 数字不属于最新 `21ebffd` 最终验收。已有性能材料仅供 [历史阶段追溯](evidence/README.md)，不能冒充最新报告或严格同条件 A/B。

交互方法为“Playwright 填写搜索至下一 requestAnimationFrame 回调，含协议时间”；rAF 在 paint 前，不能称实际像素上屏。当前无有效最终 CPU、私有内存 / 工作集或 p50 / p95 指标可交付。

## 受限进程专项

用户反馈 SGuard64.exe 不可见后，发布阶段独立核对真实 API 与正式 Electron。SGuard64.exe、SGuardSvc64.exe 的基础行可枚举，字段 / 动作读取可能返回 Windows 错误 5。专项 9 项通过，证据为 [结果](evidence/restricted-process-ui-v11.json) 和 [过滤截图](evidence/guard-process-visible.png)；只读核查，没有对这两个进程发送写操作。

界面明确“访问受限不等于不存在”，信息读取筛选与搜索有说明；按 exe 名 / PID 定位，系统任务管理器的文件说明名未被当作已接入搜索字段。本专项不声称获得受保护字段或绕过保护。

## 成品与证据

入口 `output/TaskManager-win-x64/start.cmd` 或同目录 `TaskManager.Host.exe`；须保留完整目录。依赖 .NET 10 的 Microsoft.NETCore.App 和 Microsoft.AspNetCore.App，不需 Node / npm 或 Microsoft.WindowsDesktop.App。生产默认不开 CDP。

- [桌面 1380](evidence/final-desktop-1380.png)、[960](evidence/final-desktop-960.png)、[125%](evidence/final-desktop-125-percent.png)、[结束确认](evidence/final-desktop-terminate-confirm.png)。
- [进程树](evidence/v11-process-tree.png)、[资源](evidence/v11-resources.png)、[服务详情](evidence/v11-service-details.png)、[GPU](evidence/v11-gpu-identity.png)、[960 客户区 +125% 详情](evidence/v11-desktop-960-125-details.png)。
- [断连](evidence/final-recovery-disconnected.png)、[恢复](evidence/final-recovery-connected.png)、[阶段证据索引](evidence/README.md)。

缩放截图使用 CDP 完整 surface，客户区由 ClientRect 验证，不扣固定边框。Windows picker / Explorer / Properties 原图含用户目录 / 导航，仅留本地 output，不随公开资料发布；归档 JSON 只含断言、汇总与相对截图名。

## 清理与边界

功能测试按保存身份关闭 Host / Electron、释放本次端口，清理自有负载 / 临时目录；未按 exe 名批量杀用户程序。三次性能失败也已清理，之后只读复查无性能测试 Node，5196 / 9226 无监听。后续发布实例的清理由发布阶段另记，不提前声称全机端口为空。

逐进程网络吞吐、StartupApproved / 启动策略编辑、内核分析和保护绕过未接入；端点不是吞吐，CompanyName 版本资源不是数字签名认证。无权限字段不等于进程不存在，搜索只能匹配实际已读字段。

未执行：管理员服务成功 / 超时链、非常态树部分执行、最终性能预算、热插拔、长时泄漏、其他 Windows 版本、安装器 / 自动提权、完整读屏认证。不能用旧报告或生产构建代替这些验收。
