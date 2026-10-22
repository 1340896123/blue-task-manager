# 实际后端 API 合同

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

`unavailable`是字段名到 `{code,message,nativeErrorCode?}` 的字典。常见code：`warmingUp/accessDenied/exited/error/notCollected/protected/partial`。不存在对应错误项表示该字段读取成功；null不允许转0。逐进程网络/GPU恒null且有notCollected原因。

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
- 能力：`networkBytesPerSecond/gpuPercent:null`、`canTerminate:boolean`、`unavailable`。`canTerminate=true`只表示身份/关键状态/保护检查满足；执行时仍可能因权限变化返回403。

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
