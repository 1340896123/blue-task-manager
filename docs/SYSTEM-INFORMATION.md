# 系统信息：真实硬件概览

日期：2026-11-07。本文覆盖旧 HWiNFO 页面保留要求，不改变既有 CPU 总体/逻辑处理器图表。界面继续采用 Qt 6 Widgets + .NET 10、Windows 浅色蓝色视觉、JSON.NET 与 CsWin32。

## 用户目标与布局

在左侧“服务”下方、底部“设置”之前增加“系统信息”导航，点击进入独立页面。页面按参考图的阅读顺序展示处理器、显卡、主板、内存、硬盘、显示器、Windows 系统；主板/逐条内存/逐块磁盘和多显示器不能只用整机总数替代。截图中的型号和数字仅为布局参考，生产界面必须取本机真实值。

删除性能页的“HWiNFO 传感器”资源入口、表格/空态与共享内存配置说明，不把它改名后充当系统信息。旧 `resourceType=hwinfo` 偏好在性能页回退到 CPU。已有只读 HWiNFO HTTP 合同可保留兼容，但 Qt 不再请求它；不为本轮扩展外部传感器能力。

一页一个“系统信息”标题/命令栏，页内分组概览，类别标签与图标在左、型号与事实值在右，内存/磁盘/显示器逐设备自然展开，内容可纵向滚动。共享搜索筛选本页实际型号、类别与事实，清空恢复；无匹配显示文字空态。刷新读取实际硬件，加载/失败/部分可用/采集时间放既有页头 caption，不恢复正常成功通知条。型号文字可选择、长文字换行，未知字段用“—”和可读原因。

## 成熟实现研究与采用范围

已实际通过 GitHub 读取并固定以下源码，仅参考设计和标准字段，不移植 GPL 业务代码、内核驱动或专有协议：

| 来源 | 已核验内容与采用方式 |
|---|---|
| [TaskExplorer SystemInfoView.cpp，9f3f95d22f07f759dd1e0114417529ba51decb01](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/TaskExplorer/GUI/SystemInfo/SystemInfoView.cpp) | CPU/RAM/GPU/Disk 各类视图独立，`Refresh` 通过 Qt 单次调度当前视图；借鉴按资源组织与按需刷新，使用当前项目既有 HTTP/Qt 组件。 |
| [LibreHardwareMonitor SMBios.cs，b4718a6208ebbcf028cc29f2aea30e58b138e830](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/b4718a6208ebbcf028cc29f2aea30e58b138e830/LibreHardwareMonitorLib/Hardware/SMBios.cs) | Windows 固件表读取、SMBIOS type 2/4/17 分别描述主板/CPU/内存；逐 DIMM 的厂商、料号、插槽、容量、速度和配置速度来自原始字段。独立按 DMTF 标准实现严格长度、字符串及未知哨兵校验，不照抄其将缺失字节读成零的容错语义。 |

标准来源为 [DMTF SMBIOS](https://www.dmtf.org/standards/smbios)、Windows [GetSystemFirmwareTable](https://learn.microsoft.com/windows/win32/api/sysinfoapi/nf-sysinfoapi-getsystemfirmwaretable)、[DXGI_ADAPTER_DESC1](https://learn.microsoft.com/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc1)、[IOCTL_DISK_GET_LENGTH_INFO](https://learn.microsoft.com/windows/win32/api/winioctl/ni-winioctl-ioctl_disk_get_length_info) 与公开显示配置接口。沿用标准 HTTP GET/JSON，不新增硬件代理协议。

## 数据源与边界

所有 Windows 调用在 .NET Native 层通过 CsWin32 生成绑定；Qt 不采集硬件，不执行 PowerShell/WMI 命令作为生产数据源。原有磁盘/DXGI身份实现优先复用。

| 类别 | 必须取得或明确不可用的事实 | 推荐真源与口径 |
|---|---|---|
| 处理器 | 型号、厂商、物理核心、逻辑处理器 | SMBIOS type 4；需要时用注册表硬件描述与 `GetLogicalProcessorInformationEx`。多插槽不得把全机线程数复制成每颗 CPU 的线程数。 |
| 显卡 | 各物理设备型号、专用显存及明确的软件/虚拟/逻辑口径 | DXGI `EnumAdapters1/GetDesc1`；CsWin32调用D3DKMT公开只读查询，确认单物理节点后按实际PnP设备实例合并逻辑别名。不同PnP实例即使型号相同也保留；多节点/查询失败保持独立逻辑LUID与原因，不把共享内存算作专用显存。 |
| 主板 | 厂商、产品、版本 | SMBIOS type 2；厂商占位字符串当缺失处理，不展示为真实产品。 |
| 内存 | 安装总量、可确认的条数、逐条插槽/厂商/料号/容量/类型/速度 | SMBIOS type 17；支持扩展容量/速度字段和未知哨兵。速度单位为 MT/s，配置速度标为固件报告值，不称实时测量 MHz。全机可用物理内存与安装容量分开。 |
| 硬盘 | 逐物理设备型号/实际容量/SSD或HDD/总线及总量 | 经验证的物理设备编号 + 存储描述符、seek-penalty 与 `IOCTL_DISK_GET_LENGTH_INFO`；容量为字节，UI 用一致的 GiB/TiB，不累加分区/盘符或把标称营销容量作实际容量。部分容量未知时总量明确不完整。 |
| 显示器 | 逐活动显示器名称/分辨率/刷新率；可读时显示物理尺寸 | 公开显示配置/枚举接口、真实 monitor/EDID；分辨率与刷新率为当前模式，物理尺寸不能从像素或 DPI 推算。没有 EDID 的远程/虚拟显示设备保留可读状态。 |
| 系统 | Windows 产品/版本/构建、架构 | 实际 Windows 版本注册表/公开系统 API；不得照抄参考图的版本或位数。 |

工艺节点、GPU 流处理器数量、显存颗粒/类型、主板芯片组、内存通道数与实时 CL 时序没有本轮可靠的通用标准来源，界面无需堆砌这些字段；不按型号库猜测，也不依赖 HWiNFO 安装才能显示基本概览。不采集/展示硬件序列号、Windows 产品密钥等无关标识。

## HTTP 与 DTO 合同

新增 `GET /api/system-information`，返回独立硬件概览，不把静态硬件复制到每秒 snapshot 或历史。`?refresh=true` 表示重新读取；仅触发只读查询，不修改系统。JSON 经现有 MVC/JSON.NET camelCase 输出，继续沿用回环访问限制和原有错误 envelope。

建议类型如下；实现若因 CsWin32 绑定合理调整命名，同步本节/API与Qt消费者：

- 根：`status`（`loading|ready|partial|unavailable`）、`capturedAtUtc:UTC|null`、`unavailable:Dictionary<string,Unavailable>`、`processors[]`、`graphicsAdapters[]`、`motherboard`、`memory`、`disks[]`、`displays[]`、`operatingSystem`。
- 每个子对象均有 `unavailable` 映射，沿用 `{code,message,nativeErrorCode?}`；可缺字段为 null。数组真实为空与枚举失败通过独立原因区分，失败不能把条数/容量变成零。
- processor：`id,name,manufacturer,coreCount,logicalProcessorCount`。
- graphics adapter：`id,name,dedicatedMemoryBytes,isSoftware`。
- graphics adapter另含`physicalDeviceId:string|null`、`adapterKind:physical|software|virtual|logical`及`logicalAdapterIds[]`。物理身份未经确认时不按名称去重，UI明确“逻辑适配器（物理身份未确认）”；ROOT/SWD设备明确虚拟，DXGI软件标记优先保持软件口径。当前硬件节点数不为1时不按首节点PnP键合并聚合适配器。
- motherboard：`manufacturer,product,version`。
- memory：`installedBytes,moduleCount,modules[]`；module：`id,deviceLocator,manufacturer,partNumber,capacityBytes,memoryType,speedMtPerSecond,configuredSpeedMtPerSecond`。
- disk：`id,deviceNumber,model,capacityBytes,mediaType,busType`。
- display：`id,name,widthPixels,heightPixels,refreshRateHz,widthMillimeters,heightMillimeters`。
- operating system：`caption,displayVersion,build,architecture`。

实际容量字段 `installedBytes/capacityBytes/dedicatedMemoryBytes` 为十进制字符串或 null，避免JSON数字精度损失；CPU核心/线程、条数、像素/毫米和MT/s为可空数字，显卡`isSoftware`保留真实DXGI标记。

硬件概览由独立后台单任务采集，普通读取 TTL 60 秒、并发刷新合并，不阻塞实时进程/PDH采样或Qt GUI线程；首次读取与刷新有明确加载及有界等待。列表/固件/字符串/原生缓冲有上限，原生句柄/COM/缓冲均释放，关闭不遗留无限任务。分组失败隔离，不因一块磁盘拒绝访问丢掉其他硬件；旧值若保留必须标记过期/读取失败，不冒充刚采集成功。

页面偏好新增 `systemInformation`（页面白名单和最多九页同步）；保持原有 `settings` 的技术身份和页索引兼容。查询/滚动偏好可用，禁止持久化硬件概览。旧 HWiNFO 偏好只做显示迁移，不删用户其他偏好。

## 设计 Token 与技能依据

沿用 MASTER 当前浅色 Fluent、Segoe UI/中文 fallback、28px页标题、20px分组、14px正文、12px说明、44px导航/主命令、现有4/8px间距和蓝色焦点。新增硬件标签列建议 `systemInfoLabelWidth=112`，内容限宽沿用现有设置页 `settingsMaxWidth=1080`，设备事实行使用正常布局而非固定高度裁切。使用同一矢量图标系列，无 emoji/品牌拼贴/暗色参考图直拷贝。

已读用户指定 `C:\Users\jieok\.skills-manager\skills\ui-ux-pro-max\SKILL.md` 与桌面适用 quick-reference/pro-rules。两次 design-system 查询 `desktop system information dashboard`、`information dashboard enterprise` 都返回营销 `Hero + Features + CTA`，与本机工具不匹配，因此未持久化/未采用该建议；Qt 无专用 stack，使用已有 MASTER 和技能通用规则作为明确 fallback：正文/说明对比≥4.5:1、有意义图标/焦点≥3:1，键盘可达、状态有文字、刷新不夺焦点。显示验收仅1920×1080/100%，不扩展手机/暗色/其他分辨率矩阵。

## 本轮定向验收

| ID | 范围与通过依据 |
|---|---|
| SI01 | .NET与Qt Release构建通过；C#无System.Text.Json/手写PInvoke/PowerShell采集入口，HTTP经现有JSON.NET返回。 |
| SI02 | 真实Windows API读取根与七类对象；独立系统查询对照CPU/主板/内存条/磁盘容量/当前显示模式/Windows版本，记录真实可用与不可用字段，不用参考图或测试fixture冒充实机。 |
| SI03 | 普通缓存读取与显式刷新实际可用、时间前进/请求合并；实时snapshot继续推进；部分失败原因/null口径可读，不归零；只检查本轮采集源。 |
| SI04 | 正式Qt在1080P打开“系统信息”，导航位于服务与设置之间；七类按顺序、逐设备、长文字、滚动、搜索/空态与刷新可用，Windows蓝色/浅色一致，无裁切或页面水平溢出。保存WGC实拍。 |
| SI05 | 性能页不再出现HWiNFO入口/页面/配置说明，旧hwinfo偏好回退CPU；CPU总图与逻辑处理器模式必要代表检查保持。设置入口不受页索引影响。 |
| SI06 | 新页键盘导航/可访问名称/可见焦点，刷新不丢失输入或滚动；新旧页面偏好正确读写，使用测试隔离目录。关闭只清理本次测试Host/Qt身份与端口。 |

按实际diff选择上述项目，结果与可复现命令/退出码/截图路径写入 `SYSTEM-INFORMATION-RESULTS.md`。不重跑进程结束、服务启停、安装更新或所有页面的无关全量矩阵；未测试分支如实标注。禁止关闭用户正在使用的其他Takao实例。
