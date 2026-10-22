# 后端 API 合同

## 1.1实施增量

本节是本轮实施合同；以下原有章节是1.0已验收基线。可根据实际CsWin32生成类型调整DTO/路径，但必须同步前端、本文与真实E2E。继续复用JSON.NET、标准HTTP状态和同源POST令牌校验，不发明私有协议。

### 版本、偏好与详情

阶段2核心已实现，实际合同与验证见[NATIVE-CORE-V1.1.md](NATIVE-CORE-V1.1.md)。`GET/POST preferences`返回`{preferences,warning:Unavailable|null}`，POST为完整UiPreferences对象而非补丁。字段精确为`version:1,density:compact|standard,sidebarCollapsed:boolean,processView:flat|tree,pages:{页面名:{query,sortKey,sortDirection:ascending|descending,visibleColumns:string[]|null,filters:{currentUser,sessionId,state:all|running|restricted}|null,scrollTop,selectedIdentity,resourceType,resourceId}},window:{width,height,zoomFactor,alwaysOnTop}|null`。query≤256、8页、40列、配置≤64KiB；进程/详情必须保留name/pid。省略window保留已有原生窗口偏好，首次为默认。保存位置为`%LOCALAPPDATA%/BlueDomainTaskManager/preferences.json`，测试必须设`TASKMANAGER_DATA_DIR`隔离。坏文件降级warning，不保存paused/frozen。

快照增加`samplingDurationMs`（本次原生+慢数据+聚合的单调耗时，非HTTP序列化耗时）；进程增加`privateBytes`（PROCESS_MEMORY_COUNTERS_EX.PrivateUsage，专用提交）、`handleCount,startedAtUtc,uptimeSeconds`；系统增加`commitBytes,commitLimitBytes,cacheBytes,threadCount,handleCount`（GetPerformanceInfo×PageSize）。unknown仍null并有unavailable。元数据后台有界队列初读期间为`loading`，基本路径与实时资源先可用。

快照JSON.NET UTF-8表示按完整revision缓存，标准弱ETag含每次Host启动随机实例ID，语义在gzip/identity编码间保持一致；暂停/周期/历史/警告变更使其失效。标准gzip Fastest按Accept-Encoding协商；默认GET仍是完整原合同，不新增私有差分协议。

- GET `snapshot`返回标准ETag；客户端携带`If-None-Match`，表示未变返回304且无体。ETag对应完整表示，采样、暂停/周期、警告、历史重置等变化都使其改变，不能只依赖sampleId。304不等于断线，不重建React快照对象。`bootstrap`及token继续no-store；快照即使no-store仍可用客户端内存中的手动条件请求，不使用磁盘HTTP缓存。
- GET/POST `preferences`读写白名单UI偏好：`version`、`density`、`sidebarCollapsed`、`processView`、`pages`（已知页面各自query≤256字符、排序白名单、visibleColumns白名单、筛选）。列表数量有界且保留名称/PID；JSON.NET原子写用户本地配置。interval沿用settings并持久化；paused/frozen默认不跨重启恢复。坏文件回退默认并给可见原因；不允许持久化token/快照。
- GET `processes/{pid}/details?creationTimeFileTime=...`按身份实时读取，返回`process`、`capturedAtUtc`、`startedAtUtc`、`uptimeSeconds`、`handleCount`、`privateBytes`（PrivateUsage，UI称“专用提交”）、`relatedServices[]`和`unavailable`。退出404，身份变化409；未知仍null。可在现有ProcessDto增加低成本字段，不能因此每帧拉取所有服务配置/网络端点。

  关联服务只允许慢来源观察时刻晚于或等于已验证进程创建时刻；若`startedAtUtc > relatedServicesCapturedAtUtc`、观察时间缺失或创建时间不可确认，则`relatedServices:[]`并返回`unavailable.relatedServices.code:stale`及可见原因，避免PID复用关联旧服务。服务页定位按新GET服务详情获得的PID和capturedAtUtc，在最新已知快照中找创建早于观察的完整identity，再GET进程details核验；不因PID相同直接定位。
- GET `processes/{pid}/connections?creationTimeFileTime=...`返回`capturedAtUtc,identity,connections[],unavailable,truncated`。连接含`protocol`（TCP/UDP）、`addressFamily`、`localAddress/localPort`、`remoteAddress/remotePort`（UDP可null）、`state`、`pid`，IPv4/IPv6均覆盖。CsWin32 GetExtendedTcpTable/GetExtendedUdpTable OWNER_PID，枚举前后核验身份，不做自动反向DNS；按需短TTL，最多5000条并显式截断。连接不是流量吞吐。
- GET `processes/{pid}/icon?creationTimeFileTime=...`按身份取路径，返回标准image/png或明确无图标响应；SHGetFileInfo等公开Shell接口，有界路径+mtime缓存；前端仅可见行请求并保留通用图标，不把base64塞进快照。

### 写动作

| 方法与路径 | 输入与行为 |
|---|---|
| POST `processes/{pid}/priority` | `{creationTimeFileTime,priority}`；priority白名单`idle/belowNormal/normal/aboveNormal/high`，拒绝realtime/未知值；同句柄身份/保护检查+SetPriorityClass+GetPriorityClass读回；返回实际身份与实际优先级 |
| POST `processes/{pid}/tree-preview` | `{creationTimeFileTime}`；返回当前根/后代成员完整`pid,creationTimeFileTime,identity,name,parentIdentity,canTerminate,reason`与采样时间；无法可靠建立身份的成员阻止树动作，不能悄悄跳过 |
| POST `processes/{pid}/terminate-tree` | `{creationTimeFileTime,members:[{pid,creationTimeFileTime}]}`；仅接受用户预览的确切集合。执行前重新枚举比对成员并打开所有必要句柄，逐一重验身份/保护；集合变动409且不开始。叶到根执行，执行中失败返回逐项成功/失败/未执行，不声称事务全成功，不重试或扩大集合 |
| POST `processes/{pid}/file-action` | `{creationTimeFileTime,action:"openLocation"\|"properties"}`；后端取核验路径，Shell标准接口定位文件/属性；禁止renderer传入任意路径/脚本/verb。无路径/已退出/权限给具体错误 |
| POST `desktop/browse-executable` | 本地exe选择，取消返回`{cancelled:true,path:null}`；成功返回规范路径，不执行；headless明确503。不自动提权，不将文件选择当作进程已启动 |
| POST `services/{name}/control` | `{command:"start"\|"stop"\|"restart",expectedState,expectedPid}`；URL编码真实服务名；SCM最小权限、同服务句柄重验状态/PID，StartService/ControlService并QueryServiceStatusEx等待（有界≤15秒）；返回实际state/pid/completed/timedOut/message。权限403；输入400；目标变化409；超时保留最新实际状态不伪报成功。不得自动停依赖服务 |
| POST `startup/open-location` | `{id}`；注册表ID包含反斜杠，使用标准JSON请求体传递，避免路由编码歧义。服务端重读已知真实条目并定位来源文件/程序；不执行Run命令、不猜StartupApproved。无法可靠解析返回明确不支持 |
| POST `desktop/open-system-location` | `{location:"startupSettings"\|"userStartup"\|"commonStartup"\|"services"}`固定枚举，使用`ms-settings:startupapps`、已知Startup目录、系统服务管理入口；不可接受任意URI/shell文本 |

效率模式使用单独`processes/{pid}/efficiency`：身份、enabled、Windows能力和原生节流读回，保留可恢复的原优先级/节流状态；只降优先级不能声称效率模式。复制与CSV由前端基于明确所见快照完成；不包含写token。CSV为当前筛选全部行（非虚拟窗口一屏），标记采样时刻/单位，RFC4180转义和UTF-8 BOM，文本单元防公式注入。

效率模式已实现并以真实专用目标原生读回/恢复验证。`GET processes/{pid}/efficiency?creationTimeFileTime=...`与POST返回`{identity,available,enabled,canRestore,priority,controlMask,stateMask,reason}`；POST输入`{creationTimeFileTime,enabled}`。UI以逐目标available为准，本应用保存的原状态才允许关闭/恢复；外部已开启状态拒绝覆盖，实时优先级进程拒绝切换。`window/always-on-top`为现有窗口命令的新增toggle；GET `desktop/window-state`返回`{available,alwaysOnTop,zoomFactor}`，窗口置顶有读回确认，尺寸/缩放/置顶有界持久化。

客户端超时：程序选择器是用户控制操作，不使用原9秒HTTP超时，取消请求不等于关闭系统选择器；服务动作15秒观察，客户端应至少18～20秒且禁止自动重试；树动作预检后退出确认总等待≤8秒，建议客户端20秒。单目标结束响应新增`completed:boolean|null`，terminate为真实退出确认，3秒未确认则false并明确pending；start保持原创建结果且completed为null。部分成功/超时不能显示全成功。

### 资源增量

阶段3实际合同详见[NATIVE-TELEMETRY-V1.1.md](NATIVE-TELEMETRY-V1.1.md)，准确大小写见Contracts.cs。默认快照额外字段为`networkAdapters[],physicalDisks[],gpuAdapters[],telemetry`，没有`disks`别名。

- `process.gpuPercent:number|null,gpuEngine:string|null,gpuCapturedAtUtc:string|null`：PDH English GPU Engine、最忙单引擎，不把多个并行引擎相加。至少两个PDH样本、有效CStatus和前后创建身份校验；新身份额外预热；无实例为`noObservedEngine`，不是0。gpuEngine为真实`luid_..._phys_.../eng_..._engtype_...`标签。详情沿用最近同身份值和独立GPU时间，不宣称实时同步读取GPU。
- `networkAdapters[]`：`id`为UInt64 LUID十进制字符串，另有`interfaceIndex,alias,description,status,interfaceType,isHardware,isVirtual,isLoopback,isTunnel,receiveLinkSpeedBitsPerSecond,transmitLinkSpeedBitsPerSecond,receivedBytes,sentBytes,receiveBytesPerSecond,sendBytesPerSecond,unavailable`。四个累计/链路值均字符串。status为真实IfOperStatus枚举；虚拟标记为非硬件且不是回环/隧道，不猜型号；GetIfTable2包含软件过滤层，不能把所有接口相加当物理网线吞吐。非Up接口速率null/disconnected，稳定接口独立差分，重置/新接口预热。
- `physicalDisks[]`：`id,name,isTotal:false,readBytesPerSecond,writeBytesPerSecond,activePercent,unavailable`。id/name为真实PhysicalDisk实例，如`4 C:`；使用Disk Read/Write Bytes/sec与`100 - % Idle Time`，活动有效范围0～100。排除`_Total`，避免把总量重复相加或猜其idle聚合。`system.diskReadBytesPerSecond/diskWriteBytesPerSecond`为全部非_Total物理实例之和，`system.diskActivePercent`为最忙物理磁盘；任何成员指标无效，该聚合指标null。进程I/O不参与。
- `gpuAdapters[]`：`id,adapterLuid,physicalIndex,utilizationPercent,busiestEngine,engineCount,unavailable`。ID为计数器LUID+physical index，不虚构厂商名称；同一物理引擎先跨PID汇总再取最忙，`system.gpuPercent`为其中最大值。成员CStatus不完整时保守null/partial，不把各PID的最大值求和。
- `telemetry`：`sampleId,capturedAtUtc,collectionDurationMs,gpuInstanceCount,unavailable,queryDurationMs,initializationDurationMs`。这是独立PDH时间轴，可能落后基本快照一帧；首次sampleId0、时间null/预热。collectionDurationMs含批量查询/格式化/聚合/身份确认，queryDurationMs仅PdhCollectQueryData，初始化单列。超过max(5秒,3×采样周期)无新PDH样本，下一基本快照清空旧数值并stale。能力available表示采集链已接入，当前设备/权限是否有值仍以unavailable为准。
- `performanceHistory[]`增加`telemetryCapturedAtUtc,gpuPercent,diskActivePercent,networkAdapters[],physicalDisks[],gpuAdapters[]`。各资源点为`{id,percent,readBytesPerSecond,writeBytesPerSecond}`（网络read=receive/write=send）。GPU/磁盘曲线必须按telemetryCapturedAtUtc去重、按真实时间绘制；同一异步PDH样本可能在多次基本快照出现，不能视为新采样。网络点使用原capturedAtUtc；不为未出现的资源补0。

GPU/磁盘仅后台单worker惰性初始化；每个基本样本投递一份有界最新请求，不阻塞health、首快照或settings锁；同一长寿命PDH query批量读取，不按PID另开query，不返回GPU全引擎数组。暂停后不继续启动PDH采样；正在进行的不可取消原生调用可完成，但暂停快照不因此更新。退出等待worker完成并释放query；图标句柄由独立核心层释放。

## 1.0已验收基线

C#源合同是 `src/TaskManager.Host/Models/Contracts.cs`。字段camelCase，JSON由MVC + JSON.NET输出。URL同源根 `/api`，默认 `http://127.0.0.1:5188`。可用 `--port=5191` 或环境变量 `TASKMANAGER_PORT` 指定端口。正常启动由Electron.NET创建桌面；仅显式 `--headless` 为无窗口测试主机。`bootstrap.desktop` 反映当前真实窗口是否存在。

## 请求与错误

GET不用写令牌。所有POST带 `X-TaskManager-Token`，令牌通过GET bootstrap读取，只保存在UI/测试内存。Host必须是127.0.0.1或localhost并且端口与当前监听端口一致；Origin存在时必须精确匹配当前请求来源。所有API `Cache-Control: no-store`，不开放CORS，不绑定局域网。

错误为 `{ error: { code, message, nativeErrorCode } }`；原生码可为null。400输入/Win32启动错误、403令牌/来源/保护/权限、404进程已退出、409进程身份变化、503首采样或headless能力不可用。无效DTO自动模型验证也使用同一错误格式。

## 端点

| 方法 | 路径 | 请求与结果 |
|---|---|---|
| GET | health | `{platform:"Windows",version,ready,processId,nativeSource,json}`；ready为首次成功采样完成 |
| GET | bootstrap | `{token,settings,capabilities,version,desktop:boolean}` |
| GET | snapshot | 下述完整不可变快照；不触发采样 |
| POST | processes/start | `{executable:string,arguments?:string}` → `{pid,creationTimeFileTime,identity,message}`。不通过shell拼接；程序与参数分别传入CreateProcess。可填完整路径或可从System32/Windows/PATH解析的程序名 |
| POST | processes/{pid}/terminate | `{creationTimeFileTime:string}` → 同上。身份在同一查询/终止句柄上验证；不递归终止子树 |
| POST | settings | `{intervalMs:1000\|2000\|5000,paused:boolean}` → 实际 `{intervalMs,paused}`。暂停立即反映在快照，sampleId不伪增长 |
| POST | history/reset | `{}` → `{sessionStartedAtUtc,message}`；立即清空本次历史，下次采样重新建立累计基线 |
| POST | refresh | `{}` → `{message}`；下一有效采样重读慢速列表。暂停时不会擅自继续采样 |
| POST | window/{command} | `minimize`/`maximize`（最大化/还原切换）/`close` 为真实BrowserWindow动作；`zoom-in`/`zoom-out`/`zoom-reset` 对应25%步进/100%恢复（75%～200%）。受同样token/Origin保护；无窗口时503 `headlessUnavailable`。前端据desktop禁用无窗口动作。路由变量不能用MVC保留名action |

## 快照

顶层字段：`sampleId:number`、`capturedAtUtc:string`、`sessionStartedAtUtc:string`、`intervalMs:number`、`paused:boolean`、`system`、`processes[]`、`services[]`、`startupItems[]`、`users[]`、`history[]`、`performanceHistory[]`、`capabilities`、`warnings[]`、`slowDataCapturedAtUtc:string|null`。

时间全部ISO8601 UTC。`capturedAtUtc`是最近成功采样时间，不是HTTP响应时间。服务/启动项/WTS用户名称默认缓存15秒，慢速时间单列标明；进程动态数值每周期真实读取。路径、文件版本CompanyName、令牌用户按进程身份缓存；CompanyName不是签名认证。

`unavailable`是字段名到 `{code,message,nativeErrorCode?}` 的字典。常见code：`warmingUp/accessDenied/exited/error/notCollected/protected/partial/noObservedEngine/disconnected/stale`。不存在对应错误项表示该字段读取成功；null不允许转0。逐进程网络仍null/notCollected；1.1的GPU真实链与独立时间见上文。

### system

- `cpuPercent:number|null`，GetSystemTimes相邻样本差分；第一帧null。
- `memoryPercent:number|null`、`totalPhysicalBytes/availablePhysicalBytes/usedPhysicalBytes:number|null`，GlobalMemoryStatusEx物理内存；不是可读进程工作集求和。
- `networkReceiveBytesPerSecond/networkSendBytesPerSecond:number|null`、`networkInterfaceCount:number`，GetIfTable2活动非回环/非隧道网卡的计数差分。新接口集合首帧/计数回退预热，可能含虚拟接口，不宣称物理网线唯一吞吐。
- `processCount:number`、`logicalProcessorCount:number`、`uptimeSeconds:number`、`unavailable`。

### processes[]

- 身份：`identity:string`（PID:FILETIME；不可读为PID:unknown）、`pid/parentPid/threadCount:number`、`creationTimeFileTime:string|null`、`name:string`。
- 元数据：`path/publisher/userName/priority:string|null`、`sessionId:number|null`、`state:"running"|"restricted"`。
- 动态：`cpuPercent:number|null`（按整机逻辑处理器归一化）、`workingSetBytes:number|null`、`ioReadBytesPerSecond/ioWriteBytesPerSecond:number|null`。
- 累计64位：`totalCpuTime100ns/ioReadBytes/ioWriteBytes:string|null`，十进制字符串，不能直接无损转JS number；前端需BigInt或换算展示。
- 能力：`networkBytesPerSecond:null`、`gpuPercent:number|null`（1.1真实PDH）、`canTerminate:boolean`、`unavailable`。`canTerminate=true`只表示身份/关键状态/保护检查满足；执行时仍可能因权限变化返回403。

### services[] / startupItems[] / users[]

- 服务：`{name,displayName,state,pid}`；state为CsWin32真实枚举名称，如`SERVICE_RUNNING/SERVICE_STOPPED/SERVICE_PAUSED/…`。停止服务PID0为真实SCM值。
- 启动：`{id,name,command,source,enabled:null,enabledReason}`；真实Run/RunOnce注册表值或Startup文件路径。只读，不猜测StartupApproved/策略最终启用状态。
- 用户：`{sessionId,userName,stationName,state,processCount,cpuPercent,workingSetBytes,unreadableProcessCount,unavailable}`。state为WTS枚举名，如`WTSActive/WTSDisconnected`。只按匹配用户SID解析名称与会话的进程聚合；任何已归属进程对应指标不可读时聚合指标为null/partial，不把不明归属进程算给当前用户。

### history[] / performanceHistory[]

- 会话累计：`{identity,pid,name,firstObservedAtUtc,lastObservedAtUtc,isRunning,observedCpuTime100ns,observedIoReadBytes,observedIoWriteBytes}`。后三者十进制字符串或null，只累加相邻观测增量，首观察不会把进程出生以来累计量纳入。退出身份可保留，最多20,000身份，超出时移除最早不在当前列表的身份。
- 性能点：`{capturedAtUtc,cpuPercent,memoryPercent,networkReceiveBytesPerSecond,networkSendBytesPerSecond}`，保留最近120次成功采样；只有一帧就只有一点，不补零、不填充空曲线。暂停/失败造成的时间缺口须由前端按时间戳表达。

`capabilities`是名称到 `{available,source,reason}` 的字典；键为`processes/system/network/services/users/startup/history/processNetwork/processGpu/diskActivity`。warnings保留慢速来源错误与最近采样失败，不包含令牌或全机转储。

## 可复现后端检查

```powershell
dotnet restore src/TaskManager.Host --locked-mode
dotnet build src/TaskManager.Host -c Release --no-restore
dotnet build tests/TaskManager.TestWorkload -c Release
node tests/native-api.mjs
```

测试脚本自行创建与关闭独立端口5191的真实主机、专用负载和临时目录；仅结束自己创建并保存身份的目标，不使用mock，不保存令牌。可通过`TASKMANAGER_TEST_PORT`换端口。API开发启动：`dotnet run --project src/TaskManager.Host -c Release -- --headless --port=5188`。生产UI资源随Release输出复制；完整成品用`scripts/build.ps1`生成并从`output/TaskManager-win-x64/start.cmd`启动。UI/桌面完整验收见docs/ACCEPTANCE-RESULTS.md。
