# 架构与数据合同

> 1.1实施增量见 `PRODUCT-V1.1.md` 与 `API.md` 的1.1合同。新增能力沿用同源HTTP/JSON.NET/CsWin32，不新增私有传输协议；以下原始架构继续适用，服务只读等1.0范围由本轮增量覆盖。

## 1. 技术决策

- 前端：React + TypeScript + Tailwind CSS，由 Vite 构建本地静态资源。默认使用 Windows 系统字体和统一 SVG 图标。
- 桌面：Electron.NET，优先官方现代 `ElectronNET.Core` + `ElectronNET.Core.AspNet` 0.6.0，ASP.NET Core 主机以 `UseElectron` 回调创建 BrowserWindow。框架默认Electron版本不保证受支持，必须在实现阶段核验npm并显式固定当前受支持的Electron版本。目标 .NET 10 / Windows x64；开发机已具备 .NET 10.0.401、.NET 8.0.426、Node 24.16.0。
- 系统接口：Microsoft.Windows.CsWin32 0.3.367 为初始候选，包以实际 NuGet 可还原版本固定。`NativeMethods.txt` 声明需要生成的 Win32 绑定，优先生成的安全句柄封装。
- JSON：Newtonsoft.Json / Microsoft.AspNetCore.Mvc.NewtonsoftJson；API控制器配置 `AddNewtonsoftJson`。禁止因 Minimal API 默认结果自动落到 System.Text.Json；需要直接输出时显式调用 JsonConvert。
- 协议：同源 HTTP/JSON；前端轮询不可变快照，操作走 POST；不设计私有二进制协议、消息总线或远程代理。

Electron.NET 自身的宿主通信属于成熟框架实现，不替代应用的同源 HTTP API。不得用框架依赖作为自行调用 System.Text.Json 的理由。

## 2. 数据流与分层

```text
React视图 / 表格 / 图表
      │ 同源HTTP；轮询、取消过期请求、错误状态
ASP.NET Core控制器（JSON.NET / Origin / 操作令牌 / 输入校验）
      │
SnapshotService ── ProcessActionService
      │                  │ 相同句柄校验创建时间后执行
Win32Collector / StartupReader / SessionReader / ServiceReader
      │
CsWin32生成绑定 ── Windows内核、PSAPI、Toolhelp、SCM、WTS、IP Helper、Registry
```

推荐目录：`src/TaskManager.Host` 放 C# 主机/采集/控制器；`src/TaskManager.Web` 放 React；`tests` 放真实集成和 E2E；`scripts` 放开发、构建与验收入口。实现阶段可微调目录，但必须更新对应文档与命令。

采样器作为单例后台服务串行执行；用单调时钟计算间隔，用 UTC 时间给快照标记；启动静态信息缓存按进程身份索引，进程退出后及时释放句柄。前端只读完整快照，不能拼接属于不同采样时刻的系统总量。慢速项（服务、启动项、用户名称/发行者）可以使用独立缓存与采集时间。

## 3. Win32 来源与语义

| 数据/操作 | 首选 CsWin32 API | 语义/约束 |
|---|---|---|
| 进程枚举/父PID/线程数 | CreateToolhelp32Snapshot、Process32First/Next | 一次快照枚举；逐进程读取失败不丢弃基础行 |
| 创建时间/CPU | OpenProcess、GetProcessTimes、GetSystemTimes | 句柄最小权限；创建FILETIME为字符串；增量不跨身份复用 |
| 内存 | K32GetProcessMemoryInfo、GlobalMemoryStatusEx | 工作集与全机物理内存分开；字段注明字节单位 |
| 路径/优先级 | QueryFullProcessImageName、GetPriorityClass | 可缓存静态字段；拒绝访问保留原因 |
| I/O | GetProcessIoCounters | 读取/写入累计字节及速率，不能当物理磁盘或网络 |
| 用户/会话 | ProcessIdToSessionId、WTSOpenServer/WTSEnumerateSessions/WTSQuerySessionInformation；可选OpenProcessToken/GetTokenInformation/LookupAccountSid | 登录会话不等于本应用用户；归属不确定保持未知；释放WTS内存和句柄 |
| 服务 | OpenSCManager、EnumServicesStatusEx | 返回实际状态与PID；SCM只读权限 |
| 启动项 | RegOpenKeyEx/RegEnumValue + Windows Startup已知文件夹/文件枚举 | 来源与未知启用状态显式，不凭存在猜测已启用 |
| 总网卡吞吐 | GetIfTable2/FreeMibTable | 对活动、非回环接口累计字节差分；新接口首采样预热；不能用来填进程网络列 |
| 运行时间 | GetTickCount64 | 系统启动后经过时间，单位明确 |
| 启动进程 | CreateProcessW，必要时ShellExecuteEx | 可执行文件与参数分别传入，不拼接powershell/cmd字符串；句柄释放 |
| 结束进程 | OpenProcess + GetProcessTimes + IsProcessCritical + TerminateProcess | 先比对创建FILETIME；始终在同一个已打开句柄上重验和终止 |

可获取值=0时为真实零值。`null` 必须伴随字段或能力级原因；原因至少覆盖 `warmingUp`、`accessDenied`、`notSupported`、`notCollected`、`exited`、`error`。实现不要为满足一张表而自行增加驱动或持续 ETW 会话。

## 4. 推荐 HTTP 合同

合同以固定根 `/api` 为准；字段 camelCase，时间 ISO 8601 UTC，字节 bytes、速率 bytes/sec、CPU percent（0～100）。ID和大整数精度规则单独注明。采样更新时间与读取响应时间不能混为一谈。

| 方法与路径 | 行为 |
|---|---|
| GET /api/health | 平台、主机版本、就绪状态；不触发采样，不泄露令牌 |
| GET /api/bootstrap | 当前同源页面获取本次主机的写令牌、采样设置和能力声明；Cache-Control: no-store |
| GET /api/snapshot | 最近完整快照：sampleId、capturedAtUtc、sessionStartedAtUtc、intervalMs、paused、system、processes、services、startupItems、users、history、capabilities、warnings |
| POST /api/processes/start | `{ executable, arguments? }`；返回实际PID与身份；操作令牌必需 |
| POST /api/processes/{pid}/terminate | `{ creationTimeFileTime }`；只结束该身份的一条进程；不默认递归结束树；操作令牌必需 |
| POST /api/settings | 更新 intervalMs（1000/2000/5000）与paused；返回实际应用的设置；操作令牌必需 |
| POST /api/history/reset | 清空本次观测累计及调整会话起点；操作令牌必需 |

实现可把慢速列表拆为独立 GET 端点，但要保持来源/时间/错误合同，并同步前端和验收脚本。禁止前端通过 CLI、WMI脚本或 Electron 的Node权限直接读取系统来绕开后端。

建议进程 DTO：`identity`（`${pid}:${creationTimeFileTime}`）、`pid`、`parentPid`、`creationTimeFileTime`、`name`、`path`、`publisher`、`sessionId`、`userName`、`threadCount`、`priority`、`cpuPercent`、`workingSetBytes`、`ioReadBytesPerSecond`、`ioWriteBytesPerSecond`、`totalCpuTime100ns`、`ioReadBytes`、`ioWriteBytes`、`networkBytesPerSecond`、`gpuPercent`、`canTerminate`、`unavailable`。不可读创建时间的行仍可展示，但 `canTerminate=false`。Windows FILETIME与可能超过 JS 安全范围的累计 64 位计数器以十进制字符串传输，前端按需转换 BigInt 计算或转为合理单位展示。

建议错误结构：`{ error: { code, message, nativeErrorCode?, details? } }`。输入错误400、权限/保护拒绝403、目标消失404、进程身份不匹配409、来源暂不可用503。错误消息不得把令牌、完整内部堆栈写入页面。

## 5. 本机 HTTP 与桌面边界

- Kestrel 仅绑定明确回环地址（127.0.0.1或受控localhost），不得监听0.0.0.0、局域网地址或通配Host。
- 同源部署静态UI；验证 Host，存在 Origin 时必须精确匹配当前主机来源；不存在 Origin 的只读本地检查可允许。写入同时要求本次启动生成的高熵 `X-TaskManager-Token`；外站跨域CORS不开放。
- 令牌只保存在进程内存、同源bootstrap和UI内存，不能写文件、日志、URL查询、验收报告或截图；no-store保护bootstrap/API。
- Electron renderer 启用 contextIsolation、关闭 nodeIntegration；禁止外部导航/新窗口获得本地应用权限；不向任意URL暴露本地原生调用。
- 保护 PID0、PID4、自身宿主与当前 Electron 进程族、关键系统进程。后端调用 IsProcessCritical 判定，未知关键状态须保守拒绝。前端禁用只是提示，后端再次执行保护检查。
- 终止目标先打开具备查询和终止权限的句柄，再在同一句柄上验证创建时间并调用终止，避免 PID复用时间窗口。不给目标默认 SeDebugPrivilege，不尝试绕过PPL/系统保护。

## 6. 生命周期与可测性

- `dotnet run` 的桌面启动必须由 Electron.NET 管理真实 BrowserWindow；可额外提供明确的无窗口 `--headless`/环境开关供API及浏览器E2E，不把浏览器启动等同桌面验收。
- 关闭桌面窗口时结束本应用主机/采样器，避免留下监听端口与孤儿Electron；释放所有Native/WTS/SCM句柄。
- 测试允许创建专用真实进程、真实短期文件与CPU负载，不允许替换生产采集器、拦截API返回假快照、填造表格行。
- 保留只读健康检查和可配置回环端口以支持自动化；E2E不能需要关闭系统任务管理器、杀用户应用、安装驱动或管理员权限。
- 最终产物包含 Windows x64 Release 可运行目录与一键启动脚本；README记录路径、必要运行时与完整命令。无需制作安装器。桌面E2E可通过仅测试时启用的本地CDP控制真实Electron页面，完成搜索、新建专用任务、结束该任务及窗口关闭全链路。

## 7. 1.1增量设计

- 复用每个进程已打开的limited-query句柄读取PROCESS_MEMORY_COUNTERS_EX；详情所需句柄/私有内存等原生字段独立失败。所有更改优先级/节流/终止动作在同句柄校验PID+创建时间+保护，不能只信前端canTerminate。
- 元数据缓存分为进程身份→路径/用户，文件路径+mtime→版本发布者，SID→账户名，均有界；昂贵首次读取预算化，不把全机版本/目录域解析串行塞进首帧。详情/连接使用按需请求与短TTL，避免每帧全量枚举TCP/UDP或服务配置。
- SnapshotService发布不可变快照表示，表示版本涵盖采样、设置、历史重置和警告变化。GET snapshot使用标准ETag/If-None-Match；304直接返回无JSON体。用户本地快照仅在UI内存保存；写令牌仍no-store且不持久化。
- PDH统一长寿命query，使用English counter路径避免中文系统计数器名称差异；批量读取GPU/PhysicalDisk，处理实例新增/退出、首帧与CStatus；最忙GPU引擎语义与I/O/物理磁盘语义不能混用。GetIfTable2按接口LUID/真实累计值保持相邻计数与名称；不把多个虚拟接口之和标物理网卡速率。
- 服务动作使用SCM最小权限、同服务句柄、预期状态校验及QueryServiceStatusEx实际状态轮询；有超时与最新状态，不自动提权/操作依赖服务。长动作与采样分离，不能阻塞全局采样锁。
- 前端继续TanStack虚拟列表；树先由同帧身份建立有循环保护的扁平可见行；冻结仅保存展示快照引用。展示/实时状态分开，危险操作总在服务端重验，不以冻结内容充当最新状态。
- UI偏好仅保存白名单字段：页面查询/排序、列、密度、树模式、侧栏、采样间隔；JSON.NET有界验证并原子落盘，坏文件回退并反馈；paused/frozen等易导致陈旧数据状态默认不跨重启恢复。API token、完整快照、打开详情对象不落盘。
