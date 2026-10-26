# 接管已有任务管理器入口

日期：2026-10-26。用户要求：无论当前由哪个工具接入，都允许当前目录程序显式接管；取消接管只恢复 Windows 标准任务管理器，不恢复此前工具。本文覆盖历史文档中“其他工具/特殊配置一律不可覆盖”的限制。应用默认不写系统入口，仍需明确用户操作、真实权限与读回确认。

本轮实际结果见 [验收记录](TASK-MANAGER-TAKEOVER-RESULTS.md)：36 项真实只读桌面/API 断言与 3 项静态约束通过；成功全局替换/标准恢复及过滤写入未执行。

## 目标与当前场景

设置中的“任务管理器入口”发现其他目录/工具时应提供可用的“接管现有入口…”操作。便携版本与已安装版本即使都属于蓝域，只要目录不同，也需要明确替换，不能永久禁用。

本轮只读现场为 HKLM64 `taskmgr.exe` 根键仅有普通 REG_SZ `Debugger`，指向 `C:\Program Files\BlueDomainTaskManager\TaskManager.Launcher.exe`，无子键。当前目录版本与其路径不同。此记录不是写入/成功接管证据；测试不得自动覆盖该用户入口。

## 标准机制与成熟来源

2026-10-26 实际在线重新核验以下源码，只参考公开机制与设计，不复制 GPL 实现，不引入驱动、服务或专有协议：

| 来源 | 核验与采用 |
|---|---|
| [TaskExplorer 固定提交 util.c](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/ProcessHacker/tools/CustomSetupTool/util.c) 的 `SetupHasTaskMgrDebuggerIfeo` / `SetupCreateTaskMgrDebuggerIfeo` | 读取 HKLM64 IFEO Debugger；以正确引用的程序路径设置 REG_SZ。沿用既有固定 Launcher + `--task-manager-entry`，不接受前端命令。 |
| [Microsoft RegSetValueExW](https://github.com/MicrosoftDocs/sdk-api/blob/d43984d7c322f7d7d35704d6d1ad36dc3539703b/sdk-api-src/content/winreg/nf-winreg-regsetvalueexw.md) | KEY_SET_VALUE 权限、指定类型/原始数据、字符串包含终止符、失败返回真实 Windows 码。多个值的写入不是通用原子事务。 |
| [Microsoft RegQueryValueExW](https://github.com/MicrosoftDocs/sdk-api/blob/docs/sdk-api-src/content/winreg/nf-winreg-regqueryvalueexw.md) | 注册表字符串不保证终止符，读取类型与字节长度后有界验证；类型/编码异常不能猜成空值。 |
| [Microsoft GFlags Details](https://github.com/MicrosoftDocs/windows-driver-docs/blob/staging/windows-driver-docs-pr/debugger/gflags-details.md) | IFEO Debugger 是按程序名、计算机范围的标准入口，重新启动目标进程时生效；保留其他诊断设置。 |

`UseFilter` 的路径过滤会使子键配置参与实际入口选择，单改根 Debugger 不足以保证接管。因此显式接管时关闭现有过滤选择，取消时保持过滤关闭。上述 TaskExplorer 根键实现没有覆盖此分支，不能把该参考说成已验证过滤行为；本项目必须以 T04 的真实 Windows 过滤场景核验补足证据。

## 原生与 API 合同

- 固定目标仍为 `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe` 的 64 位视图；所有原生调用由 CsWin32 生成，API 与 C# 数据处理继续使用 JSON.NET。
- GET 不写注册表。保持既有状态和字段，新增 `canTakeOver:boolean`。配置可完整读取、实际可写、Launcher/Host 齐全，且已有其他命令、异常类型或特殊 IFEO 配置需明确替换时为 true；这些配置不再构成永久禁用理由。不可读取、权限拒绝、容量限制等实际错误仍明确返回，不能承诺绕过系统 ACL。
- 普通 `enabled:true` 仍只用于可直接启用状态，`repair:true` 用于精确本目录旧 Host 命令。接管请求为 `{enabled:true,expectedRevision,takeOver:true}`；`takeOver` 默认 false，不能与 `repair:true` 或 `enabled:false` 同时提交，非法组合 400。未带明确接管标记的旧客户端遇到现有其他配置仍返回 409，避免静默改变既有调用语义。
- 接管不判断原工具品牌、文件是否仍存在、参数、路径是否为当前产品旧目录，也不执行其命令。新的 `Debugger` 始终为后端固定本目录 Launcher REG_SZ。其他类型、异常字符串与额外 IFEO 值/子键均可经明确确认接管。
- 根 `UseFilter` 已存在且为非零 DWORD、其他类型或异常长度时，明确接管先将其写为 DWORD 0；缺失或已为有效 DWORD 0 时不增加/重写。过滤子键和无关根值保留，关闭过滤后不能继续支配入口。不要递归删除整键或子键，不修改其他程序 IFEO。
- 不提供旧工具备份恢复，不保存旧命令用于产品取消或卸载。产品不执行原命令。
- 保持 Origin/写令牌、revision、单实例串行、写句柄立即重读和写后实际读回。revision 覆盖根值原始类型/字节、子键名、可信本目录命令与权限；检测到变化先返回 409。Windows 无通用跨工具 CAS，不声称外部程序永远无法同时更改。
- `UseFilter` 与 `Debugger` 为多个原生写入。任一步失败报告真实错误及“可能部分生效，请刷新”；不自动重试、不恢复旧工具。成功仅在读回固定根 Debugger、过滤禁用且无读错误后成立。

## 取消与卸载

取消只处理精确属于本目录的新 Launcher 或旧 Host REG_SZ Debugger，保持严格路径/全文所有权校验。若自有根 Debugger 同时存在启用/异常过滤，仍应提供取消路径：重验 revision 后关闭过滤、删除自有根 Debugger，并读回根无 Debugger、过滤禁用，恢复 Windows 标准任务管理器。失败/部分成功明确反馈，不宣称已恢复。

原工具命令永不恢复。正式卸载复用同一逻辑；若根 Debugger 已被其他工具改走，不删除其配置。若自有值清理或读回失败，仍中止卸载并保留程序文件。普通安装/升级不静默接管其他工具。

## 界面行为

- 复用 Windows 蓝色设置分组、Modal、现有 Token、忙态和焦点管理。其他接管状态展示真实当前命令（可折行）；主操作为“接管现有入口…”，特殊配置也有可达操作。读失败/无权限/缺文件时明确原因。
- 确认标题“接管现有任务管理器入口？”，展示当前命令与本目录目标，说明替换现有工具、关闭影响入口的过滤配置、影响所有用户与 Ctrl+Shift+Esc、以后请求 Windows 管理员授权，以及“取消接管将恢复 Windows 标准任务管理器，不恢复此前工具”。主动作“确认接管现有入口”，初始焦点为“暂不更改”。取消不发送 POST。
- 已自有时“取消接管…”确认清楚写明恢复 Windows 标准任务管理器；修复/影响说明/成功通知与新语义一致，不能残留“不会覆盖其他工具”等相反文案。
- 首次提示遇到其他工具/特殊配置时不永久禁用；显示当前状态和替换后果，提供“前往设置接管现有入口”进入上述明确确认。可复用原“前往设置管理”动作，但名称和说明应清楚，不能隐式发送 `takeOver:true`。普通未接管的一次替换流程保持。
- 提交显示“正在接管并核验…”，禁用重复提交；409 刷新实际状态并要求重新明确确认。写后未知只重读，不自动重复提交。成功明确当前目录接管；取消成功明确标准入口恢复。

## 真实验收（T 系列）

| ID | 场景 | 通过条件 |
|---|---|---|
| T01 | Release、Web 类型/生产构建与原生静态约束 | JSON.NET/CsWin32、令牌/Origin、固定命令与 Launcher 管理员链保持；takeOver 非法组合与缺/旧 revision 拒绝 |
| T02 | 当前其他目录命令的只读 UI/API | GET 与独立 HKLM64 读取一致；设置显示接管操作及确认后果；暂不/Escape 不写；不自动覆盖用户现场 |
| T03 | 受控真实其他目录 REG_SZ、其他类型/异常文本 | 生产 API 显式 takeOver 写固定命令并读回；无标记仍拒绝；取消恢复标准入口，原命令不恢复 |
| T04 | 受控真实 UseFilter=1 与含 FilterFullPath/Debugger 的子键，及异常过滤值 | 接管将过滤置 DWORD 0；真实系统 taskmgr 启动进入专用实例；取消后真实系统 taskmgr 是 Windows 标准程序；无关值/子键保持；缺失/已零过滤不额外写 |
| T05 | 实际权限/并发变化/读写异常 | 403/409/503 与真实原因；原工具变化不被旧确认覆盖；多值部分失败明确、无自动回滚旧工具；无法真实触发的分支记未执行 |
| T06 | 设置与首次提示的键盘、960×640/1380×860/实际 125% | 操作可达、路径折行、无页面横溢；暂不焦点、Escape、忙态/错误/成功正确；首次 foreign 状态可进设置确认 |
| T07 | 正式桌面与卸载回归 | 新/复用窗口 Launcher 链、关闭精确清理；维护入口只清理精确自有值且恢复标准，不恢复旧工具；清理失败保留文件 |
| T08 | 记录与测试清理 | 保存 SHA、命令、断言/未执行项与应用内截图；最终完整配置与测试前相同；仅结束测试创建 PID+创建时间身份 |

真实测试只能在受控且明确安排的窗口进行。进入前完整捕获全键原始字节/类型/子键，存在当前用户入口时不自动覆盖。测试 worker 的 finally 原样恢复测试前现场仅用于测试清理，与产品取消不同；产品取消必须被独立断言为标准入口。fixture 只验证错误分支，不能冒充 T03/T04 真实成功。受限时如实标未执行，不能为覆盖率结束用户程序或更改用户服务。
