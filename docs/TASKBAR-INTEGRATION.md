# 任务栏右键中的任务管理器入口

> 2026-10-26 最新接管要求以 [TASK-MANAGER-TAKEOVER.md](TASK-MANAGER-TAKEOVER.md) 为准：当前目录可经明确确认接管任意已有工具/配置；取消只恢复标准 Windows 任务管理器，不恢复旧工具。以下“不覆盖其他工具/特殊配置”是历史合同，由本增量覆盖。

> 当前740修复见 [IFEO-ELEVATION-FIX.md](IFEO-ELEVATION-FIX.md)。新IFEO命令指向同目录asInvoker Launcher，Launcher使用CsWin32 ShellExecuteEx runas请求管理员Host；Host实际提升检查不变。下文“IFEO直接Host”“不自动触发UAC”等旧启动描述由此覆盖。旧同目录精确REG_SZ Host命令显示“旧接管需要修复”，设置中可明确修复或取消；升级保留状态且不静默迁移。正式卸载可严格清理同目录新旧两种自有命令。

> 最新要求见 [ADMIN-INSTALLER.md](ADMIN-INSTALLER.md)：所有正式启动改为系统 UAC 管理员入口并验证真实提升令牌；本页“不自动提权/标准用户写拒绝”描述的是此前基线。IFEO仍默认关闭、启用需明确用户动作；同目录升级保留，正式卸载删除文件前只取消严格属于安装目录的 REG_SZ Debugger，失败中止。下文历史测试记录保持原样，当前补验采用 R 系列。

本功能沿用成熟任务管理器的标准 Windows IFEO Debugger 方案，不新增任意任务栏菜单项。它接管原“任务管理器”入口，同时影响 Ctrl+Shift+Esc 等启动 taskmgr.exe 的方式。默认关闭，只能由用户在设置中确认后启用，范围为当前计算机，需要 HKLM 64位注册表写权限；不自动触发 UAC 或后台提权。

实现已完成；专项23项、图标回归16项、静态3项通过。当前标准账户真实写入返回5，未启用全局接管，成功注册/取消仍未执行。最终发布复验及性能见[验收记录](ACCEPTANCE-RESULTS.md)。

## 写入与恢复边界

- 固定目标为 `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe` 的64位视图；仅操作 `Debugger` 值。
- 命令来自当前可信 `TaskManager.Host.exe` 的完整路径与自有 `--task-manager-entry` 标记，正确加引号，不接收 renderer 路径/命令。
- 读状态与真实写权限分别查询。其他工具的 Debugger、非零/异常 UseFilter、过滤子键或其他特殊IFEO配置明确冲突，不覆盖。
- 启用仅允许缺失/空 Debugger；取消只删除精确等于本程序命令的 Debugger，不删除整个键，不改变其他值或过滤配置。读前状态版本、写前重读、写后读回，错误保留真实 Windows 原因。
- 移动或删除程序目录前，须先在原目录运行本程序并取消接管，否则 Windows 入口可能指向失效路径。没有写权限时可查看状态与影响说明，不能假装注册成功。
- 启用前把完整程序目录放在固定、且其他需要使用的账户可读取的位置；不分析或修改账户ACL，不要求其他用户使用个人私有下载目录。

## 启动边界

`--task-manager-entry` 在ASP.NET配置解析之前识别。只清理紧随标记、经过核验的系统 taskmgr.exe 路径及其追加参数，避免将 taskmgr 参数误当作本机主机配置。没有系统追加参数时可正常首次启动。

已有同目录、本会话、路径及宿主关系验证通过的本产品真实BrowserWindow时，用公开 Win32 窗口接口恢复并请求前台，转发进程直接退出；不创建第二个5188主机，不使用私有IPC，不操作仅同名的其他程序或用户窗口。Windows可限制前台切换，窗口复用与前台读回分别记录。

## 验收纪律

测试只读比较全局IFEO前后状态，权限拒绝时验证实际5/403与值不变；不为凑成功接管当前全局入口，不触发UAC，不操作真实SGuard。设置页、确认文案、token/Origin/预期版本冲突、专用本产品entry首次启动与已有窗口复用采用真实API/Electron/CsWin32验证。完整成功注册/取消链路在未具备权限并明确执行时必须标未执行。

## 实际验证（2026-10-23）

完整`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -SkipRestore`通过；TypeScript/Web/Host与测试探针Release均0警告/0错误。成品为`output/TaskManager-win-x64/start.cmd`。

`npm run test:taskbar-integration`在01:14:57 UTC完成23项：独立HKLM64 Debugger/UseFilter对照、固定引号命令、token/Origin与预期版本拒绝、真实Windows5/HTTP403、全键默认值/其他值/子键前后不变、设置/确认取消/刷新、960×640及125%、entry真实系统路径+/7首次启动、最小化后复用同HWND并实际foreground=true、旧PID/端口保留、转发进程退出且无额外HTTP/CDP、精确关闭清理。未写IFEO，因此没有声称真实任务栏点击或Ctrl+Shift+Esc已重定向；直接运行本产品注册命令形态验证了entry链路，完整系统接管成功链受权限限制未执行。

`npm run test:icon`在01:16:25 UTC完成16项，Canonical SVG/ICO、Host EXE资源和实际WM_GETICON均保持原图标；`npm run test:static`完成3项，JSON.NET/CsWin32/Token约束保持。

诊断中曾人为追加不属于真实taskmgr调用的合成Chromium `--headless`参数，导致有CDP页面但真实HWND隐藏。核验Electron.NET官方`RuntimeControllerAspNetDotnetFirst.StartCore`会把`Environment.CommandLine`交给ElectronProcessActive，不能把测试污染当作正常用户窗口生命周期缺陷。已删除合成开关、撤回临时生命周期改动，以真实系统路径+/7重验首次可见/前台复用。未用全局定时器或CDP伪造可见状态。

归档：[23项结果](evidence/taskbar-integration-results.json)、[设置](evidence/taskbar-integration-settings.png)、[确认](evidence/taskbar-integration-confirmation.png)、[960尺寸](evidence/taskbar-integration-settings-960.png)。专项5203/5204/9231/9232及图标5198/9230均已清理；测试只结束其自行创建的身份。
