# 修复 IFEO 接管入口的 Windows 740 错误

日期：2026-10-23。此专项是 [管理员交付合同](ADMIN-INSTALLER.md) 和 [任务栏入口合同](TASKBAR-INTEGRATION.md) 的修正增量；保留 Host 必须提升的要求，修正“IFEO 直接指向 requireAdministrator Host”这一不可工作的启动方式。本文为诊断和实施规格，实际通过项仍以本轮真实 E2E 证据为准。

## 1. 根因与只读现场

现有 `TaskManagerIntegrationService.RegistrationCommand` 为 `"<目录>\TaskManager.Host.exe" --task-manager-entry`，而 Host 的实际构建配置嵌入 `requireAdministrator`。系统 IFEO 重定向的 debugger 创建链不会替被重定向的程序执行 Shell 的 `runas` 提升流程。来自 Medium 令牌的入口因此在 Host 代码执行前返回 `ERROR_ELEVATION_REQUIRED` (740)，用户看到的错误框仍显示原始 `C:\WINDOWS\system32\taskmgr.exe`。在 Host 中添加捕获异常或调整 HTTP/Electron 时序无法修复此处失败。

本阶段只读检查得到：

- 当前执行会话为 Medium，Administrators 组为 deny-only，不能作为管理员成功链证据。
- HKLM 64 位 `SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe` 当前是空键，无值、无子键；本阶段没有写注册表或启动 taskmgr。
- 已安装目录为 `C:\Program Files\BlueDomainTaskManager`，卸载记录版本为 1.1.1；已安装 Host 的 ProductVersion 为 `1.1.1+63e4e763f223fbaf94629596d5dfadffbb735a48`。
- 仓库便携目录的 Host 为 `1.1.1+9848ed207571823402442ddb22a1f7481ec20a5b`，与已安装文件不同。必须验收并交付更新后的实际安装包，不能用便携目录测试结果替代用户安装目录的修复。
- 现有用户 Host 实例不属于测试创建的进程，不能为了覆盖安装或释放端口擅自结束。

## 2. 成熟来源与标准机制

实际通过 GitHub 页面读取并核验：

| 来源 | 核验结果与采用方式 |
|---|---|
| [TaskExplorer 固定提交 util.c](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/ProcessHacker/tools/CustomSetupTool/util.c) | `SetupHasTaskMgrDebuggerIfeo` / `SetupCreateTaskMgrDebuggerIfeo` 使用标准 taskmgr.exe IFEO `Debugger`。保留此公开 Windows 机制与 HKLM 64 位边界，不移植专有协议或驱动。 |
| [Microsoft ShellExecuteExW 文档源码](https://github.com/MicrosoftDocs/sdk-api/blob/docs/sdk-api-src/content/shellapi/nf-shellapi-shellexecuteexw.md) | BOOL 失败通过 GetLastError 读取真实原因；取消为 ERROR_CANCELLED；调用前建议初始化 COM，STA 适用于 Shell 扩展。 |
| [Microsoft SHELLEXECUTEINFOW 文档源码](https://github.com/MicrosoftDocs/sdk-api/blob/docs/sdk-api-src/content/shellapi/ns-shellapi-shellexecuteinfow.md) | `runas` 明确由 UAC 请求同意或管理员凭据。短命调用线程使用 `SEE_MASK_NOASYNC` 完成 Shell 启动；若使用 `SEE_MASK_NOCLOSEPROCESS`，负责关闭返回句柄。Shell 启动受理不等于应用已完成启动。 |

采用“可从 Medium 创建的固定本地入口 + Shell `runas` + 原管理员 Host”。这是标准 Windows API 组合；不引入服务、计划任务、任意命令提升、私有 IPC 或 UAC 绕过。

## 3. 最小实现合同

增加同目录 `TaskManager.Launcher.exe`，manifest 为 `asInvoker` / `uiAccess=false`，以 CsWin32 生成 `ShellExecuteExW` 等实际使用的绑定。Launcher 无 HTTP、采集、偏好、Electron 或后台业务逻辑；唯一启动目标为 `AppContext.BaseDirectory` 中固定 `TaskManager.Host.exe`，工作目录为同一完整目录，传入固定 `--task-manager-entry`。

IFEO 注册值改为 `"<目录>\TaskManager.Launcher.exe" --task-manager-entry`。仅接受此固定入口形态及 Windows 追加的可信系统 taskmgr 路径/原始参数；不能把原始命令行、可执行路径、`--port`、`--headless`、维护开关或环境外部参数直接转交为 Host 配置。必须在任何提升请求前拒绝异常入口。系统附加的 `/7` 等 taskmgr 参数无业务含义，无需传给管理员 Host。

使用 Shell `runas` 请求管理员 Host；无递归启动 Launcher，无 740 后无界重试，无低权限回退。取消返回真实 1223，不创建 Host/HTTP/Electron 或写偏好。路径不存在或启动失败给出明确 Windows 原因。短命 launcher 完成 Shell 调用后退出并释放句柄；不得等待整段桌面应用生命周期。

Host 继续保留 `requireAdministrator` manifest 与 `TaskManagerEntry.Prepare` 最前的实际 TokenElevation 检查；DLL/headless/维护参数不得绕过。已有可信本目录/同 SID/同会话窗口的复用继续在提升后使用原有 CsWin32 窗口实现。其他账户 UAC 凭据仍产生该账户的 Host 和偏好，不绕过身份边界复用原用户窗口。

## 4. 旧值、打包与卸载

“自有”仅包含普通 REG_SZ 且全文逐字精确等于当前目录新 Launcher 命令或此前当前目录 Host 命令。其他目录、大小写/引用/参数差异、其他类型、嵌入空字符、UseFilter/其他特殊配置均维持既有冲突和保护规则。

- 新注册仅由明确设置操作触发；空值可启用。读状态须区分旧自有命令与已修复命令，旧值可取消，并允许用户明确确认“修复接管”后迁移为新命令。
- 迁移复用预期 revision、写句柄重新读取、精确所有权、REG_SZ 与读回检查；不能因“文件名看起来像本产品”覆盖其他命令。未请求修复时不静默迁移生产注册表。
- 正式卸载的现有固定维护入口可以删除同目录两种严格自有命令；删除任何文件前读回确认清理成功。缺失/损坏/清理失败仍中止卸载、保留应用。不要删除整键、其他配置或其他目录接管。
- 同路径安装/升级保留当前注册状态；新 Launcher 随完整自包含目录发布，升级后旧值仍可由明确修复操作迁移。若安装升级选择执行迁移，须在产品安装合同和测试中明确授权与严格所有权，不能隐含加入。
- `.sln`、Release build、win-x64 self-contained publish、便携 ZIP、NSIS 安装 payload 与文件/哈希清单必须包含 Launcher 及其必要 .NET 产物；使用同目录既有固定运行时，不新增网络依赖。保持 Host/安装器/卸载器 requireAdministrator，单独核验 Launcher 的 asInvoker。
- NSIS 检查 Launcher 文件占用；维护完整性校验和卸载清单包含新产物。开始菜单、桌面快捷方式和普通 `start.cmd` 可继续指向 Host，不必为了 IFEO 改变所有入口。

## 5. 本轮真实 E2E 必验

本轮用户要求修复接管且 E2E 验收，已授权在专用测试目录、专用端口和可恢复窗口内临时执行真实 IFEO 接管；旧记录“从未启用全局入口”的限制是历史验收事实，不阻止本轮真实成功链。测试进入前完整保存现有键是否存在、每个值的名称/类型/原始数据及全部子键配置；若有第三方/不可确认配置立即保持不动。所有注册修改经提升测试 worker、生产 API/CsWin32 所有权逻辑执行，`finally` 恢复原始完整配置并独立读回比较。不能把自动化申请提升受理当作 UAC 已完成。

| 场景 | 必须证据 |
|---|---|
| Manifest/Medium 创建 | mt.exe 独立提取 Host requireAdministrator、Launcher asInvoker；Medium CreateProcess 直接运行 Host 仍真实 740；创建 Launcher 能进入 Shell 提升流程。 |
| 真 IFEO 首次启动 | 临时写入测试目录的新命令后，从真实 Medium 调用系统 taskmgr.exe；UI 真实可见，Host/Electron browser 令牌提升，正常生产采样/API链路工作，未出现原740错误。 |
| 已有窗口 | 专用测试实例最小化后再次启动系统 taskmgr.exe，恢复同 HWND、Host PID/端口不变，无第二 HTTP/CDP 实例；Launcher 及时退出。 |
| UAC 取消/异常参数 | 使用可操作的真实系统取消分支，或明确记未执行；不能伪造1223。未知目标、重复开关、`--headless`、端口/维护参数不触发任意提升或配置注入。 |
| 旧值恢复 | 仅对测试目录精确旧 Host REG_SZ 进行显式修复、读回新值，再取消；错误 revision、其他目录、参数差异、类型异常拒绝且完整键不变。 |
| 安装和卸载 | 新 NSIS/ZIP 完整包含 Launcher，专用安装目录实际启动链成立；卸载能分别清理严格旧/新自有命令；第三方或其他目录值不变，清理失败保留文件。 |
| 回归与清理 | TypeScript/生产 UI、Release/自包含构建、原生静态边界、管理员拒绝、原专用进程动作回归；截图只包含本产品，写令牌不落盘；只关闭本次测试的 PID+创建时间身份；最终完整 IFEO 与起始状态完全一致。 |

验收报告保存可复现命令、Git SHA、安装包/ZIP SHA256、断言及截图路径，并明确所有未执行项。正常用户 Host 不属于测试清理范围。
