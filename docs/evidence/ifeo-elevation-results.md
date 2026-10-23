# IFEO 接管 740 修复交付验收

2026-10-23，Windows 11 x64，.NET SDK 10.0.401、Node 24.16.0、NSIS 3.12。
生产代码提交：`53e290610845b9effe1f43686178d4ac6e796bc3`；需求与诊断：`8df00fd`；接管测试与归档：`adea31e`。
本次发布从该生产提交的独立 worktree 构建，不包含主工作区同时进行的表格和全局搜索变更。

## 修复与真实系统证据

原 IFEO Debugger 直接指向 `requireAdministrator` Host。Medium 会话启动系统 `taskmgr.exe` 时，Windows 在进入 Host 代码前返回原生 740。新接管值改为同目录 `asInvoker` Launcher，由 CsWin32 `ShellExecuteEx(runas)` 固定启动管理员 Host。Host 的 manifest、实际 TokenElevation 检查、renderer sandbox、身份保护和 HTTP 写保护均保留。

旧版严格自有 REG_SZ 显示“旧接管需要修复”，通过设置中的明确确认迁移；普通启用请求不隐式迁移。仅精确属于同目录的旧 Host / 新 Launcher 命令可以取消或由维护入口清理；其他目录、类型、参数差异和特殊 IFEO 配置保持保护。

便携目录与实际安装目录分别通过 **69 项，共 138 项真实接管断言**：

- Medium 原生 CreateProcess 对旧 Host / 旧 IFEO 返回 740；DLL 四种入口拒绝，Launcher 九组异常参数返回 87，不创建低权限业务服务。
- UI 确认修复、取消确认、旧值及新值取消、维护清理、其他目录/类型/参数/特殊配置保护，均经生产 API / JSON.NET / CsWin32 执行并独立读回。
- Medium 实际启动 `System32/taskmgr.exe`，创建真实可见的蓝域窗口；Host 与 Electron browser 的 TokenIsElevated、renderer 的 `--enable-sandbox`、真实采样和专用进程动作均确认。
- 最小化后依次从 System32、SysWOW64 和 bare `taskmgr.exe` 再次启动，恢复相同 HWND 和 Host PID，Launcher 及时退出，无第二套 HTTP/CDP。
- 两轮最终注册表完整快照及偏好原字节均恢复；清理只处理测试创建并核验路径、创建时间的进程。恢复守卫遇未知并发值或子键会保留并失败。

原有入口专项另通过 **20 项**，覆盖设置卡片、确认取消、令牌/Origin/revision、960×640、125% 缩放、旧窗口前台恢复及关闭清理；实际 foreground=true，完整 IFEO 不变。

Windows 的标准 runas 不保留测试设置的端口/CDP 环境变量。本轮真实系统入口使用默认 5188 和独立原生窗口检查；UI 修复步骤使用隔离端口/CDP。验收脚本要求默认端口空闲，不会关闭已有用户窗口来腾端口。脚本读写请求显式使用 UTF-8，原生截图及探针有超时。

## 构建、安装与回归

- 干净依赖恢复、TypeScript、生产 UI、完整 Release solution 和 win-x64 自包含 publish 全部通过；Release 为零警告、零错误。
- 独立 7-Zip 提取核对 NSIS 全部 **3,073 个文件 SHA256**。SDK mt.exe 提取 Host=requireAdministrator、Launcher=asInvoker、Setup=requireAdministrator；实际安装后 Uninstall.exe 亦确认 requireAdministrator，全部 uiAccess=false。
- 通过生产窗口关闭操作正常退出旧版，再用标准 NSIS 同目录更新 `C:\Program Files\BlueDomainTaskManager`。未强杀旧用户进程，未删除用户偏好。实际安装的全部 3,073 个文件哈希与发布清单相同，Host/Launcher ProductVersion 均为 `1.1.1+53e290610845b9effe1f43686178d4ac6e796bc3`。
- 更新安装器的退出码未捕获；不据此宣称退出 0。安装完成由安装器已退出、实际文件全量哈希及原安装记录独立确认。
- 原有 11 套原生/浏览器回归 **360 项**通过；正式管理员桌面 9、完整桌面 42、进程桌面 34、原生窗口/文件动作 22、图标 16、受限进程 UI 7，另 **130 项**通过。

上述广回归共 **17 套 / 490 项**；加本轮两处真实接管与原入口专项，已通过 **648 项断言**。

补充性能基准已执行并达到 120 个真实历史采样点，但预热后 `document.hidden` 不满足真实可见条件，结果为 `NOT_ACCEPTED`，未进行或宣称合格的稳态性能测量。该次测试实例已退出、专用端口已释放。原始诊断保存于 `output/ifeo-elevation-fix/performance-not-accepted.json`。未用历史性能通过结果替代本次限制。

补充系统管理入口套件本轮在 `system location release ready` 超时，退出 1，未取得该套新的通过证据；stdout、stderr 和真实退出记录保存为 `output/ifeo-elevation-fix/system-locations-v11.*` 与 `system-locations-worker.json`。本次未修改这两项套件或其业务实现来规避失败。末轮 PowerShell worker 在性能套件的原生 stderr 上中止，未继续系统入口；系统入口另以独立 worker 执行并取得以上结果。两项均不计入 648 项通过断言。

最后通过标准管理员入口重新打开已更新的用户应用（Host PID 9584）。独立原生窗口确认 visible=true、minimized=false；生产 sampleId 从 49 增至 51，完整 IFEO 与测试前相同、状态为 disabled。测试实例均退出，正常用户应用按交付意图保持运行。证据：`output/ifeo-elevation-fix/reopened-installed-application.json`。

最终提升审计发现系统入口补充套件启动失败后留下三个本次独立构建目录中的 Electron 进程，已逐一核验精确路径、PID 和创建 FILETIME 后清理；再次审计残留为零，未操作已安装的用户应用。记录：`output/ifeo-elevation-fix/final-cleanup.json`。

## 交付文件

稳定交付目录：`output/ifeo-elevation-fix/`。

| 文件 | 字节 | SHA256 |
|---|---:|---|
| `releases/BlueDomainTaskManager-v1.1.1-win-x64-setup.exe` | 161071208 | `d97e514b3b7a0d09be21f1cb6f0a61dae0452fadba4772ed1fd2f1f044211296` |
| `releases/BlueDomainTaskManager-v1.1.1-win-x64.zip` | 231590957 | `18c966bdb52d4cd58dbc086d78f81024e019e7b84356b3327d0ab1a69e2d0d08` |

同目录 `TaskManager-win-x64/` 为完整自包含目录；包版本仍为 1.1.1，由提交信息和上述哈希区分本次修复构建。包未签名。未推送代码或发布 GitHub Release。

## 可复现命令与证据

在生产提交的干净 checkout 中构建：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-desktop.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-installer.ps1 -SkipBuild
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-installer.ps1
```

从普通 Medium 终端运行本轮专项，脚本自行调用系统标准 RunAs worker；要求 5188 和专用 526x/926x 端口空闲、原 IFEO 无第三方配置：

```powershell
$env:TASKMANAGER_RELEASE_DIRECTORY = 'E:/taskManager/output/ifeo-elevation-fix/TaskManager-win-x64'
$env:TASKMANAGER_IFEO_EVIDENCE_PREFIX = 'portable'
npm run test:ifeo-elevation
$env:TASKMANAGER_RELEASE_DIRECTORY = 'C:/Program Files/BlueDomainTaskManager'
$env:TASKMANAGER_IFEO_EVIDENCE_PREFIX = 'installed'
npm run test:ifeo-elevation
```

原有回归采用标准 RunAs 提升 worker 中的 `npm run test:regression`、`npm run test:administrator`、`npm run test:desktop`、`npm run test:process-desktop`、`npm run test:native-desktop`、`npm run test:icon`、`npm run test:restricted-ui` 和 `npm run test:taskbar-integration`；不是以 RunAs 请求受理替代实际结果。

证据：

- 专项逐项结果、完整注册表前后对照和原生窗口截图：`output/playwright/ifeo-elevation/{portable,installed}/`，可归档版本见同目录下的 [专项证据](ifeo-elevation/)。
- 稳定交付的安装核对、manifest、广回归 worker 及结果：`output/ifeo-elevation-fix/installed-update.json`、`package-verification.json`、`installed-uninstaller.manifest.xml`、`payload.sha256`、`ifeo-regression/` 和 `regression-reports/`。
- 原入口专项：`output/taskbar-integration/results.json` 及截图。

## 未执行边界

当前既有策略自动完成管理员提升，未实测交互 UAC 取消、错误凭据或令牌查询故障注入；不能把参数拒绝或正常提升替代这些分支。未对用户实际安装执行破坏性的完整卸载；旧/新严格自有命令的生产维护清理已在专项中实测。没有把这些未执行项算为通过。

测试恢复原始空 IFEO 键，不自动长期启用系统接管。用户可在已更新应用设置中明确启用接管。
