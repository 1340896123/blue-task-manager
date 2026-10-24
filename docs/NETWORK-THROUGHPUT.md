# 逐进程网络吞吐补全

日期：2026-10-25。用户要求补全网络吞吐；目前硬件网卡 `GetIfTable2` 已接入，但进程 `networkBytesPerSecond` 固定 null。本增量将其替换为真实 Windows ETW TCP/UDP 收发统计，覆盖历史“逐进程网络未接入”的范围。所有原生接口由 CsWin32 生成；同源 HTTP、JSON.NET、管理员入口、PID+创建 FILETIME 身份合同继续有效。

## 用户行为与数据语义

- 进程/详细信息网格的“网络”显示最近完整观测窗口的接收+发送速率（字节/秒），支持原有升降序、调宽、列选择、树、复制与 CSV。未知在两方向均末尾；不将子进程流量再次累加给父进程。
- 进程详情概览显示接收、发送、合计及独立来源时刻；沿用最近同 identity 快照，不能用打开详情的时间冒充网络采样时间。IP Helper 网络连接页仍是端点，不是吞吐。
- 性能页继续使用硬件网卡累计字节差分及原有曲线。ETW 覆盖 Windows 报告的 TCP/UDP IPv4/IPv6，可含回环与虚拟网络；不等于硬件适配器总量，不宣称覆盖全部链路协议或应用启动前流量。不读取或保存包内容、目的地址、DNS、调用栈。
- 健康完整窗口无该进程事件时允许真实 0 B/s；身份未知、首次观测、断流、丢事件、暂停恢复、会话失败时必须 null 与原因。不能把网络采集失败伪装成静默。

## 已核验成熟实现与公开接口

2026-10-25 通过 GitHub API 获取 SystemInformer master 固定提交 `9f257e3b76c9af9ae598968070686561f5ec66b9`，实际读取下列源码；只借鉴设计和公开事件定义，不复制实现、驱动或私有接口。

| 来源 | 核验内容与采用范围 |
|---|---|
| [etwmon.c](https://github.com/winsiderss/systeminformer/blob/9f257e3b76c9af9ae598968070686561f5ec66b9/plugins/ExtendedTools/etwmon.c) 约116–225、386–601、653–699行 | Windows8+ 独立系统 logger、独立 Wnode.Guid、SYSTEM_LOGGER_MODE/NETWORK_TCPIP、OpenTrace/ProcessTrace；从网络负载 ProcessId/TransferSize 归属。采用独立唯一会话，不采用接管已有同名会话的部分。 |
| [etwstat.c](https://github.com/winsiderss/systeminformer/blob/9f257e3b76c9af9ae598968070686561f5ec66b9/plugins/ExtendedTools/etwstat.c) 约355–413、492–512行 | 逐进程收发分开累计后差分；进程发现前事件可能缺失。采用身份计数与预热，不把发现前缺失算0，不引入未公开新系统进程计数接口。 |
| [ntwmi.h](https://github.com/winsiderss/systeminformer/blob/9f257e3b76c9af9ae598968070686561f5ec66b9/phnt/include/ntwmi.h) WMI_TCPIP/UDP_V4/V6 | 现代网络负载前两个 ULONG 为所属 PID 与 TransferSize，IPv4/IPv6长度不同。必须校验事件版本/opcode/长度；不能取 EventHeader.ProcessId 当所属进程。 |
| [微软 SystemTraceProvider](https://learn.microsoft.com/en-us/windows/win32/etw/configuring-and-starting-a-systemtraceprovider-session) | Windows8+ 多系统 logger，私有名称、SYSTEM_LOGGER_MODE、Wnode.Guid 不得等于 SystemTraceControlGuid；槽位有限，失败明确，不停止用户采集器。 |
| [TcpIp_SendIPV4](https://learn.microsoft.com/en-us/windows/win32/etw/tcpip-sendipv4)、[TcpIp_TypeGroup1](https://learn.microsoft.com/en-us/windows/win32/etw/tcpip-typegroup1)、[UdpIp_TypeGroup1](https://learn.microsoft.com/en-us/windows/win32/etw/udpip-typegroup1)、[UdpIp_TypeGroup2](https://learn.microsoft.com/en-us/windows/win32/etw/udpip-typegroup2) | IPv4 send/receive opcode10/11、IPv6 26/27；PID/size 为uint32。只计收发，不把重传/连接/断开重复加总。 |
| [EVENT_TRACE_PROPERTIES](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/ns-evntrace-event_trace_properties)、[ControlTraceW](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/nf-evntrace-controltracew)、[ProcessTrace](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/nf-evntrace-processtrace) | 有界实时缓冲、丢事件/缓冲计数、FLUSH/QUERY/STOP、时间转换与消费者释放；丢失窗口不能声称完整。 |

TCP provider 为 `9a280ac0-c8e0-11d1-84e2-00c04fb998a2`；UDP 为 `bf3a50c5-a9c9-4988-a005-2df0b7c80f80`。微软 UdpIp 定义 EventVersion(2)；实现需记录实际 Windows 的版本/负载并仅接受已核验布局，未知布局明确 notSupported，不能盲读。无需专有协议或驱动。

## 采集、身份与生命周期

1. SnapshotService 管理一个后台采集器和长寿命独立系统实时 logger，仅启用必要网络事件。会话名含本实例 PID、原始创建 FILETIME 和随机唯一标识；控制句柄只来自本次成功 StartTrace，不复用/修改/停止 NT Kernel Logger、SystemInformer 或其他已有会话。
2. CsWin32 生成 StartTraceW、OpenTraceW、ProcessTrace、ControlTraceW、CloseTrace 等绑定；必要权限仅通过公开令牌 API 启用已有权限，不修改系统安全策略。失败保留原生码。ProcessTrace 运行于专用后台线程；回调只做有界解析与计数，不做同步HTTP、文件、DNS或每包 OpenProcess。
3. 字节按已验证的 `PID:creationTimeFileTime` 关联，使用事件自身时间而非回调到达时间划窗。事件时间不得早于该身份创建时刻/有效观测边界，延迟旧 PID 事件不得归新身份；新身份首窗预热，创建时间不可读则不可用。可采用默认 ProcessTrace 时间转换获得 FILETIME 对照，速率分母仍用单调时钟。
4. 只发布已消费的完整窗口。实测 Windows 的 BuffersWritten 与 BuffersRead 口径不同，不以两者相等判断完整；通过公开 EventRegister/EnableTraceEx2/EventWriteString，在同一会话、同一次 ProcessTrace 消费流写入每窗唯一序号标记，FLUSH 后等待对应标记回调，按标记的实际 FILETIME 划界。该标记是标准 ETW 内部同步，不承载业务协议。4秒未确认标记则 stale/null；晚到旧事件使当前窗口失效并重建基线。原生等待/查询不阻塞HTTP、设置锁或基础进程采样；网络来源可独立，重复来源不伪造新时间。
5. 收发速率分别为有效窗口字节增量/真实单调时长，双方有效才合计。计数回退、非有限值、未知布局、容量上限、丢事件/丢缓冲增量使该窗口不可用；恢复重新建基线。健康完整窗无事件才是0。
6. 全局提供状态、来源时刻及实际累计丢失计数/原因；超过 `max(5秒,3×intervalMs)` 未更新即 stale 并清空旧速率。暂停不发布新窗口，恢复预热，不将暂停期累计挤进短窗口；冻结仅冻结UI。
7. 原生缓冲数与身份缓存均有上限，退出进程清理。关闭 STOP 仅本实例成功创建的会话，CloseTrace 消费句柄，等待消费线程退出后释放委托/本地内存；部分初始化失败也清理。不持久化流量与机器转储。Windows 在 TerminateProcess 后保留系统logger；故意强杀的原生验收先由独立 CsWin32 probe 核验本次 Host PID+创建 FILETIME，只清理带相同身份前缀且精确捕获的会话；正式桌面关闭必须验证生产 DisposeAsync 释放，无需测试代清理。

## HTTP/DTO 增量

沿用 GET snapshot 与同身份进程 details，无新写操作。

| 位置 | 字段与语义 |
|---|---|
| `process` | `networkReceiveBytesPerSecond:number|null`、`networkSendBytesPerSecond:number|null`、`networkBytesPerSecond:number|null`（双方有效时相加）、`networkCapturedAtUtc:string|null`。保留既有合计字段名兼容列偏好。 |
| `process.unavailable` | 缺失的收、发、合计逐项给 `warmingUp/accessDenied/notSupported/identityUnavailable/eventsLost/stale/paused/capacity/error` 等可读原因，成功移除旧notCollected；详情保留同identity网络字段与原因。 |
| `snapshot.processNetwork` | 独立状态：`capturedAtUtc:string|null`、`status:string`、`eventsLost:number`、`buffersLost:number`、`collectionDurationMs:number|null`、`unavailable`；附 `windowDurationMs:number|null`、`flushedBuffers:number`、`consumedBuffers:number` 与最多16项 `eventLayouts:string[]` 来源诊断。缓冲两计数口径不同；完整性由标记确认。丢失为本会话真实累计；健康不等于所有PID可归属。 |
| `capabilities.processNetwork` | 接入后 `available:true`，source 明确 CsWin32/ETW TCP/UDP IPv4/IPv6；当前可用性以状态与逐字段原因为准。 |
| `system.network*`、`networkAdapters` | 保持 GetIfTable2 硬件适配器原合同，不被进程ETW合计覆盖，不要求两者相等。 |

实现按现有命名模式调整状态字段时，必须同步 Contracts.cs、TypeScript、API.md 与验收。无逐进程网络累计历史需求，不向120点资源历史复制所有进程网络数组。

## 界面与 Token

复用 MASTER 数值/caption/蓝色热度/焦点/缺失值/详情网格 Token，无新页面/配色/大卡片。网络列按真实值显示 B/s～GiB/s，说明“接收+发送”；提示/详情可读收发、来源和原因。未就绪不能显示0或永久“未接入”。硬件图的实线/虚线图例、单位与当前值继续存在。

本轮 ui-ux-pro-max `real-time network throughput --domain chart` 命中 Real-Time Streaming；采用当前数值/状态文本与线型图例建议。原SVG历史有界，不采用绿色闪烁或新图表库。

## 必验项（N 系列）

| ID | 真实验证 | 通过条件 |
|---|---|---|
| N01 | Host Release、Web类型/生产构建、静态边界 | CsWin32/JSON.NET、身份安全合同保持；无固定网络null/未接入渲染；状态/缓冲有界。 |
| N02 | 专用TCP IPv4收发进程及独立传输计数 | 预热后完整identity收发合计非零、合计等于双向和；停止传输后完整窗回落0，空闲对照身份不混淆；单位正确。 |
| N03 | 专用UDP IPv4与TCP/UDP IPv6 | 实际方向非零；记录支持情况/事件版本；IPv6不可用则未执行，fixture不能替代。本机临时端点，不依赖外网。 |
| N04 | 新进程、退出/身份冲突、暂停恢复、周期1/2/5秒 | 新身份预热、旧值不继承；详情冲突不读新PID；暂停时间不变、恢复不跨窗尖峰；真实分母可解释。 |
| N05 | ETW权限/故障/容量/丢失/过期边界 | 可实际触发分支记真结果；其余可独立计算/生命周期测试，不能冒充采集成功；未知null有原因，其他监控继续。 |
| N06 | 浏览器真实API→网格/排序/详情/CSV | 同身份网络值一致；双向排序未知末尾；详情收发/来源/原因可读；CSV真值；保留列宽与选择。 |
| N07 | 正式Electron 1380×860、960×640、125% | 真窗口截图，表格内部滚动无页面溢出、数值/单位可读；网卡性能页原曲线/图例保持。 |
| N08 | 开销与关闭/重启 | 记录采样时长、响应大小和线程/句柄趋势；本实例ETW会话释放、再启动正常，不触碰用户会话；仅清理测试PID+FILETIME与端点。 |

交付记录于 `docs/NETWORK-THROUGHPUT-RESULTS.md`：命令、Git SHA、Windows版本、实际事件版本、数据与截图路径、通过/失败/未执行和精确清理。回归 A07/A14/A17/A18、B01/B03/B04/B14/B19 和相关L/G项，不以构建替代真实API与Electron。

旧 `tests/native-api.mjs`、`tests/native-telemetry-v11.mjs` 中“进程网络恒null”的断言须改为本增量状态/值合同；`desktop-performance-v11.mjs` 的未采集说明同步更新。真实提升可复用现有 `tests/windows-layout-worker.ps1` 的 RunAs worker，不能为了测试降低生产管理员校验；非提升启动拒绝仍按原合同验证。
