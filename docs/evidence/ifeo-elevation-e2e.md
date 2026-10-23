# IFEO 740 修复：真实 Windows E2E

2026-10-23，Windows x64。便携包与实际安装目录各通过 **69 项断言**，共 138 项。测试控制器为真实非提升令牌；仅注册表 fixture、维护命令和独立探针通过系统标准 RunAs worker 执行。测试未修改 UAC 策略，也未自动操作安全桌面。

## 验收对象

两个目录的 Host/Launcher 文件 SHA256 完全一致。生产二进制代码提交为 `53e290610845b9effe1f43686178d4ac6e796bc3`，ProductVersion 为 `1.1.1+53e290610845b9effe1f43686178d4ac6e796bc3`；报告中的 `testWorkspaceCommit` 单独记录测试工作区当时的提交，不能用其推断生产包源码。

| 对象 | 实际验收目录 | 完成时间（北京时间） | 结果 |
|---|---|---|---|
| Portable | `C:\Users\jieok\.codex\worktrees\ifeo-elevation-fix\taskManager\output\TaskManager-win-x64` | 20:14:19 | PASS 69 |
| Installed | `C:\Program Files\BlueDomainTaskManager` | 20:15:36 | PASS 69 |

Host SHA256：`b2d4d1c3cc3d80df7af49722aeed2b6e4156501997854cf20be0932356bc4d2f`。

Launcher SHA256：`6d33639d0a8f820d37872beb503e8aa1eeed1c922150d018eac1f0374b8e5e59`。

## 实际通过项

| 链路 | 两个目录的实际结果 |
|---|---|
| Manifest 与管理员边界 | mt.exe 独立提取 Host requireAdministrator、Launcher asInvoker，均 uiAccess=false。Medium 原生 CreateProcess 直启 Host 返回 740；DLL 普通/headless/entry/维护四入口全部明确拒绝，无监听副作用。 |
| Launcher 输入边界 | 9 组空参数、错误标记、伪系统路径、Host 配置/维护注入、非法追加参数、超量参数均真实退出 87。 |
| 原问题复现 | 临时写精确旧本目录 Host REG_SZ 后，Medium 原生 CreateProcess 启动真实 System32 taskmgr.exe `/7`，确实返回 Windows 740。 |
| 显式修复 | 生产 CsWin32 GET 返回 migrationRequired/canRepair；普通 enable 不静默迁移。真实 Electron 设置界面打开并取消确认时全键不变；再次明确确认修复后，独立 HKLM64 查询读回精确 Launcher REG_SZ。 |
| 写保护 | stale revision、缺操作 token、外站 Origin 拒绝且全键不变。其他目录、附加参数、大小写差异、REG_EXPAND_SZ、REG_DWORD、嵌入 NUL 均不被视为自有，API 与维护 CLI 均保留配置。 |
| 清理边界 | 旧/新两种精确自有值均可取消。存在 UseFilter、默认值、二进制、多字符串、嵌套子键时阻止启用/修复；正式维护 CLI 仅删除自有 Debugger，逐项保留其他值类型及全部嵌套配置。 |
| 真实首次入口 | Medium 原生 CreateProcess 启动真实 System32 taskmgr.exe，经 IFEO → asInvoker Launcher → Shell runas → requireAdmin Host → Electron。Launcher 及时退出 0；按父 Launcher PID、目录、创建时间核实新 Host。实际 Host 与官方 Electron browser 的 TokenIsElevated=true。 |
| 生产数据与动作 | 真实 API/JSON.NET/CsWin32 采样包含本次 Host 且 sampleId 前进；本次专用 workload 继承管理员令牌，错误身份 409、自家 Host 403 保护有效，最后仅终止该专用 workload。 |
| Renderer | 真实 IFEO renderer 命令行明确包含 --enable-sandbox 且没有 --no-sandbox；同一生产构建的修复 UI 通过真实 CDP 验证 require/process 均不存在，无 renderer runtime error。 |
| 窗口复用 | 真正最小化测试 HWND 后，分别再次 CreateProcess System32、SysWOW64、裸名 taskmgr.exe；均恢复原 HWND、保留原 Host PID，Launcher 退出 0，5263/9263 没有重复 HTTP/CDP。 |
| 结束与恢复 | 通过生产关闭入口结束测试窗口；worker finally 仅按测试进程身份收尾。最终独立 HKLM64 全键与起始状态完全一致，默认 preferences.json 原始字节恢复。 |

两个报告的起止完整 IFEO 快照 SHA256 均为 `8c42a23deef901a418b779b8fd37db235b4527ac88b53e252a0b327ddb68849c`，内容为“键存在，值/子键均为空”。测试前完整递归读取每个值名称、类型、数据与所有子键；若初始配置非空则拒绝修改。清理仅认可本测试已记录的完整 fixture、其 Debugger 删除态、原基线或精确本目录旧/新普通配置；发现未知并发值/子键会保留并明确失败。

## 重现命令

普通非提升 PowerShell 执行。5188 必须空闲；测试拒绝自动关闭现有正常应用。需要操作该应用时先由用户或交付流程正常退出，不能强杀腾端口。

```powershell
dotnet build tests/TaskManager.WindowProbe/TaskManager.WindowProbe.csproj -c Release --no-restore
dotnet build tests/TaskManager.TestWorkload/TaskManager.TestWorkload.csproj -c Release --no-restore
$env:TASKMANAGER_RELEASE_DIRECTORY = 'C:/Users/jieok/.codex/worktrees/ifeo-elevation-fix/taskManager/output/TaskManager-win-x64'
$env:TASKMANAGER_IFEO_EVIDENCE_PREFIX = 'portable'
npm run test:ifeo-elevation
$env:TASKMANAGER_RELEASE_DIRECTORY = 'C:/Program Files/BlueDomainTaskManager'
$env:TASKMANAGER_IFEO_EVIDENCE_PREFIX = 'installed'
npm run test:ifeo-elevation
node tests/static-v11.mjs
```

测试会通过系统标准 RunAs 启动实际提升的 worker；不得以“提权请求已发送”代替 TokenIsElevated 证据。Windows 在本环境下不会把调用进程的临时 TASKMANAGER_* 环境传给 runas Host，真实系统入口因此使用产品默认 5188，真实窗口截图通过独立原生探针完成；测试没有向 Launcher 添加任意参数转发。修复 UI 使用显式启动的专用提升 Host（5261/9261）和隔离偏好。默认入口阶段在内存保存并最终恢复当前用户偏好原始字节。

初期测试曾因 runas 环境继承假设、Windows PowerShell 5 的 ANSI 默认读取导致测试失败；之后修正为默认端口、显式 UTF-8 请求文件读取，重跑结果见上述 PASS 报告。这些测试问题未通过改动生产权限或 Launcher 固定参数来规避。

## 保存证据与限制

- [Portable 完整断言与原生启动结果](ifeo-elevation/portable/results.json)、[Installed 完整结果](ifeo-elevation/installed/results.json)。两个目录同时保存起止完整注册表快照及独立提取的 manifest。
- [Portable 旧值修复确认](ifeo-elevation/portable/legacy-repair-confirmation.png)、[Installed 旧值修复确认](ifeo-elevation/installed/legacy-repair-confirmation.png)。
- [Portable 真实系统入口首次窗口](ifeo-elevation/portable/real-ifeo-first-window.png)、[Installed 真实系统入口首次窗口](ifeo-elevation/installed/real-ifeo-first-window.png)。
- [Portable 恢复窗口](ifeo-elevation/portable/real-ifeo-restored-window.png)、[Installed 恢复窗口](ifeo-elevation/installed/real-ifeo-restored-window.png)。原生截图仅保留产品矩形内部，去掉 DWM 透明边与圆角外围；没有替换界面数据。
- 独立 CsWin32 probe Release 构建 0 警告/0 错误，JavaScript/PowerShell 语法检查通过，扩展到 Launcher 的原生/JSON 静态边界检查 PASS 3。
- 既有 taskbar-integration suite 已使用同一最终便携目录在真实提升 worker 中回归 [PASS 20](ifeo-elevation/taskbar-results.json)：设置确认/取消、960×640 与 125%、同 HWND 恢复、前台状态、独立全键不变与精确关闭均通过；该旧 suite 自身不执行全局注册，其真实注册覆盖由上面两轮新 suite 提供。

**未执行**：真实 UAC 取消、错误管理员凭据、故意制造令牌查询失败。取消的 1223 分支没有伪造通过。NSIS 安装/升级/卸载生命周期及其他回归由交付验收报告单独记录，不能用这 138 项替代；本测试验证了生产卸载维护 CLI 的真实旧/新所有权清理，但没有在此 suite 中卸载用户安装目录。操作 token 只在内存中使用，没有写入结果、截图、偏好或请求文件。
