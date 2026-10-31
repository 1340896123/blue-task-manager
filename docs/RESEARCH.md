# 开源实现研究与技术决策

## 启动状态徽标（2026-10-31）

实际读取 [Microsoft Fluent UI 固定提交 d2375b2452d9b8882ed63d4f4e91d8f537379e27 的 Badge 样式](https://github.com/microsoft/fluentui/blob/d2375b2452d9b8882ed63d4f4e91d8f537379e27/packages/react-components/react-badge/library/src/components/Badge/useBadgeStyles.styles.ts)：`tint-informative` 组合中性底与中性前景，`tint-success` 组合浅绿底与绿色前景，`tint-warning` 使用黄色底与警告前景，并独立布局图标。采用其语义配色与图标文字并存思路，映射到项目已有 status-badge、success、warning 和中性 Token；不复制源码、不引入依赖。ui-ux-pro-max 的对比度及非唯颜色建议用于确保中文文字始终可见、装饰图标 aria-hidden；不把业务禁用状态画成不可操作控件。

## 启动应用右键菜单（2026-10-31）

实际读取 [Microsoft Fluent UI 固定提交 d2375b2452d9b8882ed63d4f4e91d8f537379e27 的 useMenu.tsx](https://github.com/microsoft/fluentui/blob/d2375b2452d9b8882ed63d4f4e91d8f537379e27/packages/react-components/react-menu/library/src/components/Menu/useMenu.tsx)。参考 openOnContext 的坐标定位、329行附近的外部点击关闭、341行附近的上下文菜单外部滚动关闭、376行附近的首可用项焦点，以及384/397行附近仅在焦点仍位于菜单或 body 时归还发起控件。接入项目现有 ContextMenu 和 DataTable，固定启动项来源及命令，复用标准 HTTP/JSON.NET 后端操作；不引入新菜单依赖、不复制源码、不设计协议。范围和本轮不打开 UI 的验证限制见 [STARTUP-CONTEXT-MENU.md](STARTUP-CONTEXT-MENU.md)。

## 启动批准记录来源隔离（2026-10-31）

本轮网络恢复，已实际读取 BCUninstaller 固定提交 [30da609384c98ba6e35c6530129541ea4ff3970a 的 NewStartupDisable.cs](https://github.com/BCUninstaller/Bulk-Crap-Uninstaller/blob/30da609384c98ba6e35c6530129541ea4ff3970a/source/UninstallTools/Startup/Normal/NewStartupDisable.cs)（`GetStartupApprovedKey`）及同目录 `StartupEntryFactory.cs`。成熟实现按原条目所在用户/本机范围、Run/Run32/StartupFolder和原始值名匹配批准记录，不把另一范围同名项当作冲突。本项目独立修正此来源匹配，仍保留未知状态与错误；不采用参考实现的读取创建键、非02一律禁用和状态写入。真实只读跨 hive 探针在旧代码失败、修复后通过；生产读取和GUID专用真实注册值/文件的纯C#验收见 [STARTUP-STATUS-CSHARP-RESULTS.md](STARTUP-STATUS-CSHARP-RESULTS.md)。下节网络失败为2026-10-28历史现场。

## 启动应用启用状态（2026-10-28）

根因是 Run/RunOnce/Startup 后端均固定 `enabled:null`，前端整列也固定“无法确定”。本轮只读接入 Windows Shell StartupApproved，保留未知格式/权限/RunOnce口径与官方设置入口，不实现状态写入。曾尝试读取 TaskExplorer / Bulk Crap Uninstaller 与微软文档，GitHub/Microsoft 网络均 TLS失败或超时，未能核验固定源码，不将链接列为已读取证据。本机实际只读看到12字节状态1/2/3，1不能猜测。来源映射、32/64去重、二进制边界、缺失语义、证据限制与S系列见 [STARTUP-STATUS.md](STARTUP-STATUS.md)；其他枚举/优先级需要追加成熟来源或实机对照后才能扩展。

## 物理磁盘型号与类型（2026-10-26）

实际通过GitHub API核验System Informer固定提交 `d40e6e0441103828287b1b60807259040e541b5f` 的 HardwareDevices/storage.c、disk.c：公开StorageDeviceProperty描述符两段有界读取、ProductIdOffset、IOCTL_STORAGE_GET_DEVICE_NUMBER与原生磁盘索引；借鉴真实编号/部分成功/实际返回长度验证，不复制源码或驱动。微软seek-penalty口径、复合/虚拟设备边界及UI/UX Pro Max适用建议见 [DISK-IDENTITY.md](DISK-IDENTITY.md)。

## GitHub Release更新（2026-10-26）

实际核验electron-builder v26.0.12 tag指向固定提交 `44c5265a9260ca6f319f111b5a2ca7e50b0a7cd6`，读取electron-updater的GitHubProvider.ts/NsisUpdater.ts：prerelease/latest、频道元数据、明确更新参数及安装交接。当前自有NSIS/Release附件不符合该频道模板，因此借鉴流程，采用标准GitHub Releases/Compare REST、HTTPS、API asset digest和SHA256SUMS，不直接调用quitAndInstall。GitHub官方文档及实际build.8资产已核验，同core旧commit compare为ahead；完整取舍、受保护暂存、NSIS一次性交接和U系列见 [GITHUB-UPDATES.md](GITHUB-UPDATES.md)。

## 已有任务管理器入口接管（2026-10-26）

本轮实际在线重新核验 TaskExplorer 固定提交 `9f3f95d22f07f759dd1e0114417529ba51decb01` 的 CustomSetupTool/util.c（`SetupHasTaskMgrDebuggerIfeo` / `SetupCreateTaskMgrDebuggerIfeo`），以及 Microsoft RegSetValueExW / RegQueryValueExW / GFlags 文档源码。采用标准 HKLM64 IFEO、正确引用的固定 Launcher REG_SZ、实际权限与错误、字节/类型有界读取；不移植专有协议/驱动，不复制 GPL 源码。核验链接和取舍见 [TASK-MANAGER-TAKEOVER.md](TASK-MANAGER-TAKEOVER.md)。用户新要求覆盖历史“不覆盖其他工具”策略：显式接管允许替换，取消恢复 Windows 标准入口、不恢复旧工具。UseFilter 关闭及过滤子键行为由 T04 真实 Windows 场景补验，不把仅支持根 Debugger 的参考实现当作该分支已通过证据。

## 首次启动替换提示（2026-10-25）

实际通过 GitHub API 获取 SystemInformer 固定提交 `b059589e4dc5d7dbffc3d3c68190b3c226505659`，读取 [options.c](https://github.com/winsiderss/systeminformer/blob/b059589e4dc5d7dbffc3d3c68190b3c226505659/SystemInformer/options.c) 的 PhpIsDefaultTaskManager/PhpSetDefaultTaskManager，核验标准 IFEO Debugger、先读实际状态、明确替换/恢复确认、固定安全目录提醒与真实写错误。仅借鉴交互及公开 Windows 来源，复用本项目既有更严格冲突/自有值/revision/读回服务。UI/UX Pro Max 定向 keyboard focus modal 查询命中可见焦点；复用现有 React/Tailwind 原生 dialog 与 Windows 蓝色 Token，不生成新设计系统。完整取舍和 F 系列见 [FIRST-LAUNCH-INTEGRATION.md](FIRST-LAUNCH-INTEGRATION.md)。

## 逐进程网络吞吐（2026-10-25）

实际通过GitHub API/raw核验SystemInformer固定提交 `9f257e3b76c9af9ae598968070686561f5ec66b9` 的 `plugins/ExtendedTools/etwmon.c`、`etwstat.c` 和 `phnt/include/ntwmi.h`：Windows8+独立命名系统logger，NETWORK_TCPIP，TCP/UDP IPv4/IPv6从负载PID/TransferSize收发累计；事件头PID不可替代。微软SystemTraceProvider文档确认独立Wnode.Guid/有限槽位，TCP/UDP MOF与事件版本核验原生字段。采用公开ETW/CsWin32、身份/完整窗/丢事件状态、有界后台及仅本实例清理；不复制源码，不接管其他ETW会话，不引入驱动。固定链接、实际源位置、字段和N系列验收完整记录于 [NETWORK-THROUGHPUT.md](NETWORK-THROUGHPUT.md)。

## HWiNFO 与 CPU 图表（2026-10-24）

固定源码、许可边界和二进制布局核验记录见 [HWINFO-CPU-GRAPHS.md](HWINFO-CPU-GRAPHS.md)。核验MIT的Hwinfo.SharedMemory.Net `1d09864597f4342e2466880399ad0872892f870a`（只读SM2、可变stride、mutex/头一致性、缓存、pollTime），以及当前MIT的SystemInformer `897116aaeda8c9de1653c50aafd5155cf7f30609` CPU总图/逐CPU持久模式与独立历史；不复制GPL实现、不引入远程协议或驱动。Windows逐CPU采用公开PDH Processor Information；HWiNFO官方入口本次被Cloudflare阻挡，不声称已核验其当前许可时限。

UI/UX Pro Max定向查询keyboard equivalent pointer gestures首次命中移动手势/拖动，未作为双击直接依据；收窄keyboard navigation实际命中Web Keyboard Navigation，采用完整键盘操作/可见焦点。既有Windows Token继续优先，图表模式使用原生选择控件作为双击等效入口，无新页面视觉系统。

## 全页应用图标与真实硬件网卡（2026-10-24）

本轮实现前核验[TaskExplorer IconExtreactor.cpp](https://github.com/DavidXanatos/TaskExplorer/blob/master/MiscHelpers/Common/IconExtreactor.cpp)的SHGetFileInfo/ExtractIconEx与图标生命周期、[SystemInformer appresolver.c](https://github.com/winsiderss/systeminformer/blob/master/phlib/appresolver.c)的AppsFolder Shell应用枚举、[HardwareDevices netoptions.c](https://github.com/winsiderss/systeminformer/blob/master/plugins/HardwareDevices/netoptions.c)通过MIB_IF_ROW2官方标记描述网卡属性的做法。采用Shell原生应用图标、后台按需提取与有界缓存；打包应用使用公开GetApplicationUserModelId和AppsFolder IShellItemImageFactory，不解析自造协议或移植专有驱动。

网卡筛选使用[Microsoft MIB_IF_ROW2](https://learn.microsoft.com/windows/win32/api/netioapi/ns-netioapi-mib_if_row2)的HardwareInterface/FilterInterface与标准IF_TYPE值；保留断开硬件，拒绝按网卡名称猜测。启动/服务命令只读定位遵循[CreateProcess文档](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw)的引号/未引号前缀顺序与Windows SearchPath，不执行命令。.lnk交由SHGetFileInfo；跨页来源仅允许当前快照已观测行，详见[增量合同](APPLICATION-ICONS-ADAPTERS.md)。

## 全页网格列宽与排序（2026-10-23）

通过 GitHub API 实际读取 TanStack/table `v8.21.3`，固定提交 `c66b60aa6a6eac930ba8b8e26b7164679ea4feb5` 的 [列宽指南](https://github.com/TanStack/table/blob/c66b60aa6a6eac930ba8b8e26b7164679ea4feb5/docs/guide/column-sizing.md) 与 [React列宽示例](https://github.com/TanStack/table/blob/c66b60aa6a6eac930ba8b8e26b7164679ea4feb5/examples/react/column-sizing/src/main.tsx)。成熟模式包括独立columnSizing状态、minSize/maxSize约束、标题与单元格共用getSize像素值、独立调整柄及双击resetSize；指南区分onChange即时反馈和onEnd提交以避免React拖动卡顿。采用这些交互/状态原则，在现有DataTable与TanStack Virtual中接入，不为少量交互迁移到新依赖；拖动临时状态与持久保存分离。

另实际读取官方main固定提交 `c14e0221b1e656c7bb35e7b3d4cdf188719a3599` 的 [React排序指南](https://github.com/TanStack/table/blob/c14e0221b1e656c7bb35e7b3d4cdf188719a3599/docs/framework/react/guide/sorting.md)：显式sorting状态、可配置首次方向、nullable值排序与禁止移除排序均为公开能力。借鉴显式状态和缺失值策略，继续项目既有单列升/降序，不引入v9 atom架构；数值/BigInt/时间用原始值，未知在两方向始终末尾，树按兄弟节点排序。

`ui-ux-pro-max` 精确查询 `dragging movements --domain ux` 命中Accessibility/Dragging Movements，要求拖动有单指针替代并保留键盘操作；已读quick-reference与pro-rules适用桌面范围，因此规格增加可点击列宽设置、键盘调整与可见焦点。无新页面/视觉方向，沿用MASTER Windows蓝色，未生成营销模板。完整覆盖、偏好边界与G系列验收见 [GRID-INTERACTIONS.md](GRID-INTERACTIONS.md)。

## 管理员运行与 NSIS 安装（2026-10-23 增量）

实际获取并核验 Microsoft requireAdministrator/TokenElevation/CreateProcess/.NET self-contained 官方源码文档、Electron.NET 固定提交 Host-first controller、electron-builder v26.0.12 的 NSIS installer/uninstaller/winOptions 及 NSIS v3.11 MUI2 Basic 示例。URL、采用理由与边界集中记于 [ADMIN-INSTALLER.md](ADMIN-INSTALLER.md) 第4节。选择标准 NSIS/MUI2 打包既有 Host-first 自包含目录；不把 electron-builder 的 Electron 入口权限选项误当作 .NET Host 权限，不新增安装协议。用户最新要求覆盖旧普通用户运行/不做安装器范围，历史结果不改写。

## 任务栏原任务管理器入口接管（本轮增量）

固定研究版本[TaskExplorer 9f3f95d22f07f759dd1e0114417529ba51decb01 的 CustomSetupTool/util.c](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/ProcessHacker/tools/CustomSetupTool/util.c)，核验`SetupHasTaskMgrDebuggerIfeo`（755行附近）和`SetupCreateTaskMgrDebuggerIfeo`（790行附近）：成熟任务管理器通过HKLM 64位IFEO taskmgr.exe Debugger与正确引用的程序路径接管系统入口。Windows没有面向普通桌面应用、通用新增任意任务栏右键菜单项的接口，本项目明确采用“接管原任务管理器入口”的实际行为，同时说明Ctrl+Shift+Esc等入口影响。

不复制GPL实现，使用CsWin32的RegOpenKeyEx/RegEnumValue/RegEnumKeyEx/RegCreateKeyEx/RegSetValueEx/RegDeleteValue独立实现。比参考安装器更保守：不覆盖其他命令或特殊过滤配置，读/写权限分开，预期版本重验，只取消精确自有值。首次entry参数在ASP.NET配置前清理；已有同路径、同SID/会话、可信Host祖先的Electron主窗口用标准EnumWindows/ShowWindow/SetForegroundWindow复用，不增加私有IPC或任意renderer启动能力。

专项诊断另核验[Electron.NET当前官方RuntimeControllerAspNetDotnetFirst](https://github.com/ElectronNET/Electron.NET/blob/main/src/ElectronNET.AspNet/Runtime/Controllers/RuntimeControllerAspNetDotnetFirst.cs)：StartCore把Environment.CommandLine传给ElectronProcessActive。合成`--headless`启动参数会污染Chromium可见性，不能据此归咎ready事件时序。本轮恢复真实taskmgr系统路径+/7后，独立HWND首次visible=true、复用后foreground=true；未保留未经证实的生命周期补丁。

## 1.1日常闭环增量核验（2026-10-23）

本轮先查成熟源码，再约束实现；只借鉴公开接口和交互模型，不复制GPL实现或私有协议。GitHub API/raw在线核验TaskExplorer仍为`9f3f95d22f07f759dd1e0114417529ba51decb01`；SystemInformer当前master为`83d559622c4582358c3cbc23ba9f128b47f17e47`。

| 来源/位置 | 本次核验观察 | 采用/限制 |
|---|---|---|
| [TaskExplorer WinService.cpp](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/TaskExplorer/API/Windows/WinService.cpp) 约328/361/394/427行 | StartService与ControlService区分启动/暂停/恢复/停止，查询状态与配置独立 | 服务动作走SCM/CsWin32，成功必须等待真实状态；不照搬提权/驱动路径 |
| [TaskExplorer WinSocket.cpp](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/TaskExplorer/API/Windows/WinSocket.cpp) 约459～514行 | 对IPv4/IPv6分别GetExtendedTcpTable/GetExtendedUdpTable，关联owner与创建时间 | 本项目选择轻量OWNER_PID按需连接页；枚举前后重验目标创建身份。TCP/UDP是端点，不是网络吞吐 |
| [SystemInformer phlib/svcsup.c](https://github.com/winsiderss/systeminformer/blob/83d559622c4582358c3cbc23ba9f128b47f17e47/phlib/svcsup.c) 约794/804/951/959/980/2553/2583行 | QueryServiceStatusEx、StartService、ControlService分开，操作后状态查询 | 成熟SCM模式、状态/权限与超时模型；不把API接受当最终完成，不自动停止依赖 |
| [SystemInformer netprv.c](https://github.com/winsiderss/systeminformer/blob/83d559622c4582358c3cbc23ba9f128b47f17e47/SystemInformer/netprv.c) 约1109/1179/1776～1813行 | 连接CreateTime参与对象关联；IPv4/IPv6及TCP/UDP独立收集；provider elapsed使用真实时间 | 连接身份与部分读取说明，短TTL按需页签；不引入其服务/驱动 |
| [微软PdhAddEnglishCounterW](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhaddenglishcounterw)（本轮HTTP200核验） | 标准English counter接口避免本地化路径名称 | CsWin32 PDH query复用+两帧有效样本；GPU最忙引擎，PhysicalDisk注明源语义；异步后置初始化不阻塞基本就绪 |
| [微软GetProcessMemoryInfo](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-getprocessmemoryinfo)（主代理在线核验） | 当前Windows QUERY_INFORMATION或QUERY_LIMITED_INFORMATION即可，VM_READ是XP/Server2003要求 | 复用现有limited句柄，PROCESS_MEMORY_COUNTERS_EX.PrivateUsage称专用提交；减少约850额外OpenProcess/frame及误报无权 |
| [微软Starting a Service](https://learn.microsoft.com/en-us/windows/win32/services/starting-a-service)（本轮HTTP200核验） | StartService、实际状态及等待过程属于独立环节 | 有界QueryServiceStatusEx等待与最新状态回报；不安装服务/申请管理员权限作为普通运行前提 |

本机验证事实由主代理提供：GPU Engine有效实例1632，支持可用采集；883进程完整快照约1.26MB；当前非管理员。故GPU应真实接入并测开销，304/缓存有实际收益，服务成功E2E需明确权限条件，不为覆盖率操作用户服务。

UI沿既有MASTER增量。应用About Face的“目标→场景→行为→可观察验收”方法，不编造访谈；`ui-ux-pro-max`本轮React查询`memo list rendering`命中按实测使用memo/稳定keys，采用identity key。`virtualize data table`与重试`large list virtualize`未匹配虚拟化规则，不能作为数据库已验证结论；虚拟列表沿用现有TanStack成熟实现与真实DOM测量，通用表格容器滚动建议适用。已读pro-rules并按其scope选择桌面适用项，不套用手机安全区或375px验收。

核验日期：2026-10-23。只借鉴架构、计数语义和公开Win32实践，不直接复制TaskExplorer的C++实现、驱动代码或远程协议。

1.1遥测阶段复核固定commit的[GpuCounters.c](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/TaskExplorer/API/Windows/ProcessHacker/GpuCounters.c)：GPU实例按ProcessId/AdapterLuid/EngineId区分，当前实现使用perflib并保留PDH批量查询参考。采用其“进程与物理引擎分开聚合”的设计思路，本项目独立用CsWin32生成PDH English API，未复制GPL源代码。微软[PdhCollectQueryData](https://learn.microsoft.com/windows/win32/api/pdh/nf-pdh-pdhcollectquerydata)明确wildcard配合格式化数组；[PdhGetFormattedCounterArrayW](https://learn.microsoft.com/windows/win32/api/pdh/nf-pdh-pdhgetformattedcounterarrayw)规定两次有界缓冲查询；每项CStatus验证，不因API整体成功便信任所有实例。公开[GetIfTable2/MIB_IF_ROW2](https://learn.microsoft.com/windows/win32/api/netioapi/ns-netioapi-mib_if_row2)提供LUID、Alias、HardwareInterface、链路速度和字节累计，硬件/虚拟/过滤层不靠名称猜测。PhysicalDisk使用原生Disk Read/Write Bytes/sec及100-% Idle Time；排除_Total，不重复合计或假设总idle代表整机活动。各资源真实时间与UI规则见NATIVE-TELEMETRY-V1.1.md。

## 1. TaskExplorer

1.1 GPU友好身份增量：复核同一固定commit的[WinGpuMonitor.cpp](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/TaskExplorer/API/Windows/Monitors/WinGpuMonitor.cpp) `EtQueryAdapterFeatureLevel`，其CreateDXGIFactory1→EnumAdapters→GetDesc后以RtlIsEqualLuid关联，说明显示身份须按完整LUID精确匹配，不能按枚举顺序猜显卡。采用公开DXGI1.1 GetDesc1扩展读取Description与SOFTWARE标志，参照微软[EnumAdapters1](https://learn.microsoft.com/windows/win32/api/dxgi/nf-dxgi-idxgifactory1-enumadapters1)、[DXGI_ADAPTER_DESC1](https://learn.microsoft.com/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc1)的工厂/适配器生命周期与LUID定义；[微软DirectX-Graphics-Samples](https://github.com/microsoft/DirectX-Graphics-Samples/blob/master/Samples/Desktop/D3D12HelloWorld/src/HelloTriangle/D3D12HelloTriangle.cpp)也使用标准DXGI工厂。只借鉴公开设计，未复制GPL实现；不用QueryVideoMemoryInfo推算整机显存（该接口统计调用进程），不扩大此阶段范围。PDH与DXGI描述失败互不混为一类。

该小改继续使用UI/UX Pro Max及现有MASTER。定向`readable technical labels --domain ux`只返回移动字号/表单标签/对比度等不适配本问题的结果，按合同缩窄`plain language jargon`无匹配；未把其当作已验证数据库建议。名称优先、技术ID次级来自用户明确便利性要求与项目已有桌面Token规范，无新视觉样式。

仓库：[DavidXanatos/TaskExplorer](https://github.com/DavidXanatos/TaskExplorer)。本次研究 master commit：`9f3f95d22f07f759dd1e0114417529ba51decb01`，通过GitHub API读取commit和文件树、raw源码核验。

| 实际源文件/符号 | 观察 | 本项目采用方式 |
|---|---|---|
| [WinProcess.cpp](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/TaskExplorer/API/Windows/WinProcess.cpp) `InitStaticData`（约267、298～300行） | 原生进程信息的PID与CreateTime组成SProcessUID；随后单独读取静态元数据；查询权限分级尝试 | PID+原始FILETIME身份；静态与动态信息缓存分离；低权限字段部分成功 |
| 同文件 `InitStaticData`（约434～471行）、`UpdateDynamicData`（约815行） | 打开查询句柄与动态数据更新分开；可用时补真实创建时间；存在内核驱动依赖扩展 | CsWin32读取公开用户态接口；不引入驱动；动作在同一句柄重验身份 |
| [ProcessModel.cpp](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/TaskExplorer/GUI/Models/ProcessModel.cpp) `Map`（约208～250行） | CPU累计时间、I/O与Disk/Network分属不同计数块；隐藏列无需拉取昂贵扩展 | 严格区分进程I/O和物理磁盘/网络；元数据按需与缓存，不用无源零值填列 |
| 同文件（约1290～1335、1429～1437行） | 真实终止/新建状态、动态CPU数据、创建时间和运行时长分开；GPU仅在需要时采集 | 真采样时间轴；运行时间从真实创建时间计算；GPU没有采集源就null |
| [WinService.cpp](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/TaskExplorer/API/Windows/WinService.cpp) `UpdatePID` / `UpdateDynamicData`（约129～172行） | 服务状态/PID来源为ENUM_SERVICE_STATUS_PROCESSW，配置改变与动态状态分别处理 | 通过SCM/CsWin32枚举服务与PID，慢配置缓存，不构造静态服务表 |
| [SystemAPI.cpp](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/TaskExplorer/API/SystemAPI.cpp) | 中央系统API协调进程数据、缓存与异步读取 | 单例SnapshotService串行采样，UI读取不可变完整快照 |

研究时还确认存在 `TaskExplorer/API/Windows/Monitors/WinGpuMonitor.cpp`、`ProcessHacker/GpuCounters.c`、`WinSocket.cpp`；这些能力需要独立采集实现，不能由一般进程I/O推导。因本版范围和无驱动要求，不承诺搬入全套GPU/ETW/远程子系统。

## 2. Electron.NET

核验官方[当前README](https://github.com/ElectronNET/Electron.NET/blob/main/README.md)及其最小ASP.NET Core示例。现代版本已采用 `ElectronNET.Core` / `ElectronNET.Core.AspNet`，支持.NET 8/10和Node22+；通过 `builder.UseElectron(args, async () => ...)` 回调创建真实BrowserWindow。

采用框架提供的主机生命周期，不另写Node与.NET通信私有协议；生产UI由同源ASP.NET Core提供。候选版本0.6.0须在实现阶段恢复与真实启动验证。现代Core改善.NET支持与启动方式，但其默认Electron30.4.0同样偏旧，不能直接照用；实现阶段必须核验npm，显式固定当前受支持Electron版本。实际锁定版本与启动命令由桌面阶段记录。

阶段4已恢复并使用 `ElectronNET.Core` / `ElectronNET.Core.AspNet` 0.6.0，npm核验后固定Electron45.0.0。官方仓库研究commit `87cc6f98b6a9c5968d7846c0e775ea713bd0d7b2`，相关源为 `src/ElectronNET.AspNet/API/WebApplicationBuilderExtensions.cs`、`Runtime/Controllers/RuntimeControllerAspNetDotnetFirst.cs`、`src/ElectronNET.Host/main.js` 与 `docs/Using/Custom_main.md`。采用官方 `UseElectron` / `CreateWindowAsync`，显式同源URL；`custom_main.js` 的官方 `onStartup(globalThis)` hook在创建webContents前注册同步外站导航与新窗口拒绝策略。禁用Node、启用contextIsolation与sandbox；CDP只有显式测试环境变量才开启回环端口。没有修改框架源码或创建自有IPC协议。

Release采用官方支持的.NET优先启动，`.electron`与主机放同目录；构建脚本复制完整输出，包含实际Electron运行时。官方框架生成的image-size1.x存在已披露解析器DoS问题，本项目不启用splash，仍通过标准npm清单升级到修复版2.0.4并保存独立锁；最终全依赖npm audit为0。框架内部Socket.IO及其序列化属于依赖内部实现，项目自有C# HTTP/错误/探针JSON全部使用JSON.NET。

桌面验收发现MVC路由变量`{action}`为保留路由值，会导致窗口POST405，最终使用`{command}`。Electron CDP不支持Chromium的Browser.getWindowForTarget，因此独立CsWin32 WindowProbe根据本次PID+创建时间匹配HWND，以GetWindowPlacement验证最小化/最大化，并以SetWindowPos调整真实窗口。测试不假造窗口状态。

## 3. CsWin32

1.1核心增量复核TaskExplorer `TaskExplorer/API/Windows/WinProcess.cpp` 的SetPriority/SetPowerThrottled与`WinService.cpp`的Start/Stop：采用独立对象动作、最小权限句柄与原生错误模式；不使用其驱动/提权代理/私有协议。Windows10/11的[GetProcessMemoryInfo权限文档](https://learn.microsoft.com/windows/win32/api/psapi/nf-psapi-getprocessmemoryinfo)仅要求QUERY_INFORMATION或QUERY_LIMITED_INFORMATION，VM_READ为XP/Server2003旧要求，本项目复用limited句柄读EX工作集/PrivateUsage。ETag/If-None-Match遵循标准HTTP，gzip使用ASP.NET官方ResponseCompression；文件/账户慢元数据借鉴成熟监控工具的独立缓存，后台单worker与显式loading避免阻塞实时采样。

核验[microsoft/CsWin32 README](https://github.com/microsoft/CsWin32/blob/main/README.md)与[Getting started](https://microsoft.github.io/CsWin32/docs/getting-started.html)。它在编译时基于Win32 metadata生成强类型P/Invoke、COM及友好SafeHandle重载；不需要手写DllImport签名。采用NativeMethods.txt最小API集，把句柄生命周期集中在原生采集层。

## 4. UI/UX Pro Max 核验记录

使用用户指定 `C:\Users\jieok\.skills-manager\skills\ui-ux-pro-max\SKILL.md`，以及references/quick-reference.md和pro-rules.md。堆栈由用户明确为React/Tailwind，不做默认推断。

1. `Windows system monitor dashboard --design-system --density 9`：返回Minimalism/Swiss、蓝色配色，但页面模式为Hero+Features+CTA，与桌面工具不匹配。
2. 按查询合同重试 `admin dashboard dense monitoring --design-system --density 9`：返回Real-Time/Operations与实时新鲜度/暂停建议，仍含Landing和Glassmorphism，不构成可直接采用的完整Windows设计系统。
3. 因两次系统查询均部分偏离，未把生成输出持久化为产品事实；本项目MASTER是依据用户Windows蓝色要求和参考布局人工综合的Token规范。复用已验证适用的高密度、实时数据新鲜度、暂停/更新周期、对比度/焦点建议；不采用营销Hero、玻璃模糊、绿色CTA、在线Fira字体。
4. React查询 `live table keyboard selection` 无命中；仅重试 `rerender memo`，命中“React.memo按实测使用/避免普遍memo”的Rendering/Performance建议。未将无命中输出作为依据。
5. `focus not obscured --domain ux` 命中WCAG2.2 AA最小可见与AAA增强规则；采用可见焦点和sticky滚动留白，不把AAA要求宣称为AA。

最终桌面设计取舍：Segoe UI Variable/微软雅黑本地字体；浅蓝灰/白色结构面；Windows蓝色强调；高密度表格；克制动效；按真实数值绘制蓝色热度；不使用截图中的固定应用logo/进程名或营销大卡片。

阶段3补充：React堆栈查询 `virtualized table polling` 无命中，按合同收窄为 `virtualize` 后命中“100项以上使用windowing，优先react-virtual”的有效Performance建议，采用成熟 [TanStack Virtual](https://github.com/TanStack/virtual) 的React适配器，不自研虚拟滚动协议。Tailwind 4.2插件的Vite peer范围不含Vite8，经npm核验后采用兼容的4.3.3。按钮/搜索/表格状态按项目MASTER落实，未持久化无匹配的检索输出。

1.1进程工作流补充：`virtualize lists stable keys --stack react`实际命中stable unique keys与100项以上windowing两条适用建议，继续使用TanStack Virtual，tree先扁平化且保留identity key。结合上文TaskExplorer ProcessModel的创建身份/独立字段模式，实现真实父子与按需详情，不移植其Qt组件或私有协议。按MASTER桌面范围复核pro-rules的焦点、对比度、状态、Token与reduced-motion项目，不把手机尺寸/暗色主题作为本Windows应用新需求。


## 最终验收修复所用公开模式

- 复核[官方Electron.NET BrowserWindow/API基类](https://github.com/ElectronNET/Electron.NET/blob/87cc6f98b6a9c5968d7846c0e775ea713bd0d7b2/src/ElectronNET.API/API/BrowserWindow.cs)及打包Socket处理，GetNativeWindowHandle是官方接口。当地框架SetAlwaysOnTop实际读回不生效，改为其可信HWND加Windows公开SetWindowPos(HWND_TOPMOST/NOTOPMOST)，没有新增IPC。缩放用WebContents.GetZoomFactorAsync。
- [ShellExecuteExW](https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-shellexecuteexw)与Windows Explorer标准/select参数。当地SHOpenFolderAndSelectItems两种公开PIDL写法均返回S_OK而无可观察定位窗口；固定Windows目录explorer.exe的/select路径在同一独有测试文件上由原生UI Automation确认实际选中。仅传后端核验路径，固定exe/verb，无cmd/任意renderer参数。
- 版本资源按[VerQueryValueW](https://learn.microsoft.com/windows/win32/api/winver/nf-winver-verqueryvaluew) Translation表读取，账户用GetUserNameEx，Run/RunOnce按RegEnumValue原始REG_SZ/REG_EXPAND_SZ枚举。沿用TaskExplorer的公开Windows来源/独立慢元数据模式，不移植代码、驱动或专有协议。
- UI排序与时间格式复用模块级Intl.Collator/Intl.DateTimeFormat，避免每次900余项排序/120点曲线重新创建ICU格式器。慢来源异步和新快照串行发布分离，不伪造数据或改采样单位。

- 尺寸复验定位[官方WindowManager](https://github.com/ElectronNET/Electron.NET/blob/87cc6f98b6a9c5968d7846c0e775ea713bd0d7b2/src/ElectronNET.API/API/WindowManager.cs)仍对OSDescription含Windows 10的环境硬加14×7旧边框补偿，Windows11也命中。项目不扣固定边框；GetContentBounds客户区保存、UseContentSize创建后再用官方SetContentSize与实际GetContentBounds读回请求尺寸，原生ClientRect独立确认重启不累加。
