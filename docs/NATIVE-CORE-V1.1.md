# 1.1核心原生阶段交接

日期：2026-10-23。阶段2a提交`1aab248`实现采样/缓存/ETag/偏好；本文件与下一提交覆盖原生详情/管理动作。此阶段没有改UI；最终UI/Electron验收由后续阶段完成，不把后端检查等同为完整B系列通过。

## 实际HTTP与DTO

C#准确字段、null与单位以`Models/Contracts.cs`及`Models/Preferences.cs`为准，序列化仍全JSON.NET/camelCase。`health/bootstrap.version`为1.1.0，bootstrap额外返回`hostPid,currentUserName,currentSessionId`，是Host实际账户与会话，不能用任意Active用户代替当前用户筛选。

| 能力 | 实际输入/输出与语义 |
|---|---|
| preferences | GET/POST返回`{preferences,warning}`，POST完整UiPreferences，GET warning独立可见。页面键为processes/details/performance/history/startup/users/services/settings；排序方向ascending/descending；过滤state为all/running/restricted。query≤256，resourceType为system/cpu/memory/network/disk/gpu或null，resourceId≤256；scrollTop有界，selectedIdentity≤128。字段白名单见服务源码，name/pid不能隐藏。配置64KiB上限、JSON.NET原子同目录rename，只保存UI/interval；paused、frozen、token、采样/历史不落盘。窗口width960～3840/height640～2160/zoom .75～2，省略window保留原窗口偏好 |
| snapshot | 默认完整GET兼容旧UI/tests，JSON.NET UTF8每revision只序列化一次，实例nonce+revision为标准ETag。If-None-Match匹配304空体。设置、历史重置、警告同步也失效；gzip Fastest按Accept-Encoding协商。`samplingDurationMs`为采样/慢来源/聚合的单调时长，不含HTTP序列化 |
| details | GET `processes/{pid}/details?creationTimeFileTime=` → `process,capturedAtUtc,startedAtUtc,uptimeSeconds,handleCount,privateBytes,relatedServices,unavailable,snapshotCapturedAtUtc,relatedServicesCapturedAtUtc`。native时间/内存/句柄/优先级/I-O累计按同身份句柄实时读；CPU/I-O速率、publisher/user为最近成功快照，对应snapshotCapturedAtUtc；服务来自15秒慢来源，对应relatedServicesCapturedAtUtc。退出404、身份变更409；页面保留上次成功内容但禁写 |
| private/system memory | process.privateBytes为PROCESS_MEMORY_COUNTERS_EX.PrivateUsage“专用提交”，不是私有工作集。system.commitBytes/commitLimitBytes/cacheBytes/threadCount/handleCount来自GetPerformanceInfo，页面数量×PageSize转字节；物理内存仍GlobalMemoryStatusEx，不用进程工作集求和 |
| connections | GET按身份读取`{capturedAtUtc,identity,connections:[{protocol,addressFamily,localAddress,localPort,remoteAddress,remotePort,state,pid}],unavailable,truncated}`。协议TCP/UDP、地址族IPv4/IPv6，TCP state为MIB_TCP_STATE枚举名，UDP远端/状态null；端口已从网络字节序转换。四张OWNER_PID表逐源报错，单次4MiB缓冲上限、最多5000条/进程并标truncated；TTL1秒/最多128身份及合计20,000条缓存。不做DNS，不是进程网络吞吐；枚举前后同句柄验证身份 |
| icons | GET身份→path→Shell SHGetFileInfo小图标→image/png；无图标204，身份/权限失败仍错误JSON。服务器按path+mtime最多512条缓存；图标与文件动作分别独立STA/COM消息泵、有界128任务队列，HICON最终DestroyIcon，不阻塞采样。前端仅可见行请求、建议并发≤2/取消离屏/超时，失败用统一图标；不要每秒新URL重拉或内嵌快照 |
| priority | POST`{creationTimeFileTime,priority:idle|belowNormal|normal|aboveNormal|high}`，返回`{pid,creationTimeFileTime,identity,priority,message}`的priority是英文canonical；快照process.priority仍沿用中文显示。五档真实Set/GetPriorityClass同句柄读回、critical/family保护，realtime400。应用效率模式未恢复时409避免交叉覆盖 |
| efficiency | GET按身份/POST`{creationTimeFileTime,enabled}` → `{identity,available,enabled,canRestore,priority,controlMask,stateMask,reason}`。真实ProcessPowerThrottling执行速度位+idle；原优先级和完整原节流掩码只在内存保存/最多512目标。每目标原生支持才可操作，外部效率模式不覆盖、无原基线不恢复、realtime禁切换；关闭为恢复原状态，不宣称强制改写系统默认。必须显示actual返回值；机器有权限/平台差异时可用性取原生结果 |
| tree-preview | POST身份→`{identity,capturedAtUtc,members:[{pid,creationTimeFileTime,identity,name,parentIdentity,canTerminate,reason}],canTerminate,message}`。真实Toolhelp+创建先后验证关系；包含实际conhost等后代，不只同exe子进程。未知/关键/本应用族成员阻止整树；最多4096成员，不可静默跳过 |
| terminate-tree | POST`{creationTimeFileTime,members:[{pid,creationTimeFileTime}]}`。精确比对集合、先打开验证全部句柄、再次预览后才执行，变化409零开始。共享family集合不每成员枚举全机；叶到根、总退出确认等待≤8秒，逐项`status:terminated|failed|pending`及message/nativeErrorCode；返回`{completed,results,message}`，部分成功保留逐项结果，不能假撤销或自动扩大/重试集合。Windows不提供本用户态实现的原子树快照/终止事务；最后检查之后新生后代仍属于并发边界，应重新查实际状态 |
| terminate | 旧端点现等待≤3秒确认退出，结果completed=true/false；false为已提交但未确认，UI保留真实pending说明。Start动作completed=null不改变CreateProcess合同 |
| services | GET `services/{encodedName}/details`→`{name,displayName,state,pid,binaryPath,startType,account,dependencies,canStart,canStop,canRestart,capturedAtUtc,unavailable}`。按需读SCM配置+状态+动作权限，不把权限拒绝猜成0，依赖为实际MULTI_SZ列表；禁用服务不提供启动，未知启动配置不保证重启 |
| service control | POST`{command:start|stop|restart,expectedState,expectedPid}`→`{name,state,pid,completed,timedOut,message}`。same句柄重验，SCM最小权限；restart先检查非disabled，再stop→实际Stopped→start→实际Running；不自动停止依赖。不成功为400/403/404/409的真实Windows错误；15秒截止时返回completed=false/timedOut=true与最后实际观察状态。首次状态还未读到则state=unknown/pid=null。原生同步SCM调用若仍阻塞，其任务/句柄继续持有且保持busy；截止后不启动后续阶段，刷新状态，勿重试；部分生效失败消息保留最后状态。触发任何写后finally安排慢来源刷新 |
| file action | POST身份+`action:openLocation|properties`，STA执行时重新核验真实路径，再ShellExecuteEx固定Windows explorer.exe标准/select或properties（最终验收修复见FINAL-FIXES-V1.1.md）；仅报告Windows接受打开请求。禁止任意renderer path/verb/shell。真正窗口结果仍需桌面E2E核查 |
| startup/source | startup项增加`locationPath,canOpenLocation,locationReason`。解析只接受现存绝对exe token或实际Startup来源文件，不猜含空格的未引用路径。POST `startup/open-location`使用请求体`{id}`（含反斜杠ID不放路由）；重新读取来源后定位，不执行Run命令，不猜StartupApproved |
| fixed locations | POST `desktop/open-system-location`接受`startupSettings/userStartup/commonStartup/services`，只打开固定ms-settings URI/系统目录/services.msc，不接收任意URI。POST `desktop/browse-executable`使用Electron.NET系统exe选择器，取消`{cancelled:true,path:null}`，成功仅返回路径、不执行。headless503，重复dialog409 |
| window | 既有POST `window/{command}`新增always-on-top toggle，可信BrowserWindow HWND的CsWin32样式读回（最终验收修复）；GET `desktop/window-state`返回available/alwaysOnTop/zoomFactor。尺寸非最大化后600ms保存，缩放/置顶按动作保存。无开机启动/隐藏托盘 |

## 客户端与资源边界

- 程序选择器由用户完成/取消，不能沿用9秒HTTP超时；服务请求18～20秒、树20秒，写动作不自动重试。菜单/确认/详情固定PID+FILETIME；304保留同一快照对象并按成功连接处理。
- 快照新增capabilities为processDetails/processConnections/processPriority/processTree/processIcons/serviceControl/shellLocations/efficiency；processNetwork仍表示逐PID吞吐且明确未接入，不能把连接数替代速率。GPU/磁盘/适配器将在下一遥测阶段覆盖，ProcessQueryService的GPU目前仍按未接入，遥测阶段需同步该处到真实快照字段。
- 慢元数据为一个后台worker/4096队列，20,000身份、2048 path+mtime版本、1024 SID缓存；采样线程仅kernel路径等基础读，版本/SID lookup不阻塞实时采样，loading/null有原因。后台文件失败不跳过用户读取，两者原因独立。缓存有界，Windows native lookup本身可能慢，用户可继续看基本数据。
- Host显式使用Workstation GC。主代理独立A/B测得28核同规模桌面低并发场景，Server GC多个堆开销更高；初步聚合证据保存于docs/evidence/performance-*.json。仅10秒/12次样本，不作为最终SLA，GPU加入后必须预热并30秒以上复测。

## 已运行验证与留待最终验收

`dotnet restore src/Takao.Host --locked-mode -p:ElectronSkipExecCommands=true`；Host、TestWorkload、WindowProbe Release均0警告/0错误。旧`node tests/native-api.mjs`最近回归通过77断言（健康就绪等待次数计入旧脚本断言，先前为78）；`node tests/native-core-v11.mjs`通过25断言：ETag/重启偏好/坏文件/limited内存/系统提交真链路；`node tests/native-management-v11.mjs`通过64断言：真实父/两子/console hosts、零误杀集合冲突、退出确认、四协议族端口、PNG、五优先级、独立CsWin32功耗掩码+优先级读回与恢复、headless选择器拒绝、路径/URI白名单、SCM详情/真实403与无状态变化。测试创建的PID+FILETIME/临时目录独立，清理仅针对这些身份，不保存token或全机清单。

当前标准用户SC_MANAGER_CREATE_SERVICE权限探针实际返回5。未创建或操作用户服务；服务成功start/stop/restart/timeout/部分成功链路未执行，必须在最终结果保留权限限制，不能声称全面通过。真实Shell属性/定位窗口、Electron原生选择器/取消、窗口置顶/尺寸恢复、所有UI交互与最终30秒性能由后续UI/E2E阶段验收。
