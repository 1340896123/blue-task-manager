# 1.1真实遥测阶段交接

日期：2026-10-23。本阶段仅实现原生资源链与真实API检查；UI、完整Electron生命周期及最终Release由后续阶段验收。源码合同为`Models/Contracts.cs`，HTTP仍JSON.NET/camelCase，无新私有协议、驱动或后台PowerShell。

## 实际采集链

- `PdhTelemetryCollector`是独立惰性单worker。基本Snapshot已发布后才投递最新PID/创建身份映射；队列只保留最新一份请求。初始化和PdhCollectQueryData绝不持有snapshot/settings锁。一个长寿命CsWin32 SafeHandle query、四个English wildcard counter：GPU Engine Utilization Percentage、PhysicalDisk Disk Read Bytes/sec、Disk Write Bytes/sec、% Idle Time。按基本采样节奏请求，暂停时不开始新查询，已进入的不可取消原生调用可以完成但不更新暂停快照。
- GPU检查API返回码、每项CStatus（VALID_DATA或NEW_DATA）、有限非负值、至少两样本。实例按公开PID/LUID/phys/engine标签解析，逐PID最忙单引擎；同适配器同phys/engine先跨PID累加，再选最忙引擎，百分比限于0～100，不能把每个PID的最大值相加当整机GPU。来源包含软件适配器，phys索引不构成实体硬件保证。部分实例CStatus无效，相关进程/适配器保守null并给原因，不拿已知子集冒充完整值。
- 每个出现GPU实例的PID每批只打开一次limited-query句柄验证GetProcessTimes；上批和本批身份必须一致，创建时间须早于上批PDH开始时间。新身份额外预热；基本快照合并仍按完整identity，不跨PID复用套用旧值。没有观察到实例为`noObservedEngine`，不表示GPU0；驱动提供有效0才显示0。此为公开用户态计数器身份关联，不能保证驱动本身错误/延迟实例的内核级消歧。
- 物理磁盘只返回真实非_Total实例（本机如`0 D:`），读写来自独立PhysicalDisk计数器，活动为`100-% Idle Time`并限有效范围。系统读写分别对这些实例求和，系统活动为最忙物理磁盘，**不是所有磁盘同时忙的总时长**；成员缺失则对应系统聚合null。排除_Total避免重复计算，也不假设_Total idle的跨盘聚合语义。进程I/O完全不参与。
- `NetworkCollector`每个基本样本一次GetIfTable2；按UInt64 LUID和单调间隔差分，稳定接口独立预热/回退。保留硬件、软件虚拟、回环和隧道接口，含真实Alias/Description/链路位速率/字节累计；非Up速率null/disconnected。IsVirtual是非硬件且不是回环/隧道，软件filter也可能属于该类，不靠名称猜测。现有系统网络合计保持活动非回环/非隧道口径，**可能重复经过虚拟/过滤层，不能标物理网线吞吐**。新UI应优先让用户选择具体接口。

## UI准确字段与时间

`snapshot.networkAdapters[],physicalDisks[],gpuAdapters[],telemetry`均已实现，没有`disks`别名。完整字段/单位见API.md资源增量。进程新增`gpuPercent,gpuEngine,gpuCapturedAtUtc`，详情读取最近同身份值，不能把详情capturedAtUtc当GPU采样时刻；GPU时间单列。system新增`gpuPercent,diskActivePercent,diskReadBytesPerSecond,diskWriteBytesPerSecond`。

`telemetry.sampleId/capturedAtUtc`属于独立PDH时间轴，通常比基本snapshot慢一帧；首值sampleId0、时间null/warmingUp。`collectionDurationMs`含查询/数组/聚合/身份确认，`queryDurationMs`只量PdhCollectQueryData；`initializationDurationMs`单列昂贵初读。GPU实例只返回数量，不发送全引擎数组。超过max(5秒,3×周期)没有新PDH结果，下一基本快照清空陈旧数值并stale。

performanceHistory仍最多120个基本点。新增`telemetryCapturedAtUtc,gpuPercent,diskActivePercent,networkAdapters[],physicalDisks[],gpuAdapters[]`，资源点统一`{id,percent,readBytesPerSecond,writeBytesPerSecond}`，网络read=receive/write=send。**GPU/磁盘图表按telemetryCapturedAtUtc去重并按真实时间画点，重复基本快照不是新PDH采样；网络用基本capturedAtUtc。** 没有观察到资源的历史不能补0。暂停造成的时间缺口应显示为缺口。

capability `processGpu,gpuAdapters,diskActivity,networkAdapters`的available表示采集链已接入，设备/权限/计数器当下状态仍以相应unavailable为准。逐进程网络吞吐仍未接入，TCP/UDP端点不是吞吐。适配器ID是原生LUID，不虚构GPU产品型号；后续[GPU友好身份阶段](GPU-IDENTITY-V1.1.md)已通过DXGI精确匹配增加真实description/isSoftware及UI名称，并保留LUID。

## 性能与资源边界

一个query和四个counter随worker释放；每个原生数组最多8MiB/65536项，最多三次竞争重试；GPU/物理磁盘各最多256资源，超限明确capacity、不部分加总；网卡最多4096接口；PID映射最多20000并每次替换，无无限静态身份缓存。已发布DTO仅替换引用，后台不修改旧快照。StopAsync先停基本服务，再取消worker/等待正在执行的原生调用结束并关闭query。PDH提供者初始化是同步Windows调用，无法强制取消；主窗口最终退出耗时须在Electron阶段按实际主机测量，不宣称无限慢驱动可强制及时退出。

## 可复现检查

```powershell
dotnet build src/Takao.Host -c Release -p:ElectronSkipExecCommands=true --no-restore
dotnet build tests/Takao.WindowProbe -c Release --no-restore
dotnet build tests/Takao.TestWorkload -c Release --no-restore
$env:TASKMANAGER_TELEMETRY_REPORT='docs/evidence/performance-v11-telemetry.json'
node tests/native-telemetry-v11.mjs
node tests/native-api.mjs
node tests/native-core-v11.mjs
node tests/native-management-v11.mjs
```

新脚本默认5199端口、隔离临时preferences，仅创建/结束自己的真实WriteThrough负载；独立WindowProbe按身份读CPU/私有内存/句柄，直接GetIfTable2对照LUID/原始计数；独立PowerShell Get-Counter读取两样本对照GPU提供者规模与PhysicalDisk实例名称。独立消费者可能输出首帧无效状态，脚本只采用最终有效Status，不将PowerShell结果作为应用来源。测试不保存完整机器进程清单/令牌。

本阶段验收结果与30秒测量见`docs/evidence/performance-v11-telemetry.json`；范围为headless Host、真实I/O测试负载与500ms HTTP读取，未包含renderer/Electron总内存，不能等同最终桌面性能结果。

最终同版本执行：Host、WindowProbe、TestWorkload Release均0警告/0错误；`native-telemetry-v11.mjs`固定24断言通过，`native-api.mjs`76断言通过（健康检查次数使总数浮动），`native-core-v11.mjs`25、`native-management-v11.mjs`64通过。5191/5197/5198/5199端口及测试自建目标已清理。标准用户SCM创建服务拒绝5的限制继续保留，本阶段未操作用户服务补成功覆盖率。

最后测量30.04秒、949～953实际进程、28逻辑核：27基本/27PDH样本；基本采样p50 44.97ms/p95 60.13ms；PDH完整批处理p50 4.38ms/p95 17.94ms，query p95 1.57ms；初始化389.71ms异步；基本就绪490.48ms。Host平均单核8.95%/整机0.3195%，private63.8MB、工作集127.6MB，句柄639→629。1675识别GPU实例聚合为2适配器，5磁盘，54接口；末帧未压缩JSON 1,853,539字节，整个500ms读取阶段累计完整JSON 102,290,326字节，57次重复条件GET为304空体。该测试刻意每500ms额外请求完整快照以量化负载，实际UI应持续使用ETag。新字段/历史增加payload，不能把一次来源规模不同的测量宣传为与883进程短基线严格等条件的提升；最终桌面阶段仍须量整体Electron树/renderer与120点稳定历史。
