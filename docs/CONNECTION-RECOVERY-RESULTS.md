# 本机断连与窗口关闭：真实验收结果

日期：2026-10-25（Asia/Shanghai）。规格提交 `c2f1bbc`，实现提交 `97c2d7c`，Electron 兼容性修正 `3560a51`。最终管理员 worker `55496` 于本地时间 01:23:05 完成，四套测试均退出 0，共 **181 项断言通过**。

## 结论与现场边界

正式 Electron 窗口的最小化、最大化/还原和关闭已脱离 HTTP 服务。真实中断 HTTP、终止测试主机、首次 bootstrap 失败以及偏好保存挂起均可关闭；普通错误通知的鼠标和键盘关闭也通过。测试没有确定用户截图发生时服务失联的具体原因，不能把本次修复描述为已找到那次主机退出原因。

首次真实验收发现额外的 `getLastWebPreferences().preload` 检查会拒绝合法窗口：本机 Electron 45 的读回对象没有该属性。最终实现依照官方安全建议，保留所属 BrowserWindow、主 frame 与精确 `frame.origin` 校验。受限 preload 仍只暴露三个固定窗口动作，renderer 继续 sandbox/contextIsolation、无 Node 权限。最终截图与断言对应修正后的脚本。

## 实测结果

| 测试 | 断言 | 时长 | 结果 |
|---|---:|---:|---|
| `tests/connection-recovery.mjs` | 54 | 45.74 秒 | 通过 |
| `tests/native-api.mjs` | 80 | 10.57 秒 | 通过 |
| `tests/recovery-e2e.mjs` | 4 | 10.74 秒 | 通过 |
| `tests/desktop-e2e.mjs` | 43 | 18.00 秒 | 通过 |

| 专项关闭场景 | 点击至真实窗口关闭 |
|---|---:|
| 正常连接 | 19 毫秒 |
| 测试页面真实 HTTP 传输中断 | 27 毫秒 |
| 测试 Host 已实际终止、Electron 仍在运行 | 22 毫秒 |
| 新 renderer 首次 bootstrap 请求失败 | 17 毫秒 |
| 偏好 POST 保持挂起、750 毫秒尽力保存上限 | 780 毫秒 |

耗时包括 Playwright 点击调用开销；专项窗口关闭统一要求小于 3 秒。每个桌面场景均核验已记录的 Host/Electron PID 与创建身份退出、5331/9331 HTTP/CDP 端口释放，以及该 Host 精确 ETW 前缀没有遗留会话。主机终止仅处理测试创建的 Host，并通过既有 `stopOwnedHost` 释放其独立命名会话。

专项使用真实生产 UI、Host、JSON.NET 和 CsWin32，确认采样 ID 前进并包含测试主机 PID。故障仅通过 `route.abort` 中断测试页传输，或将偏好请求保持挂起，没有提供合成响应或伪造业务数值。暂停后的断连保留同一个真实进程身份和值；通知错误来自实际失败的刷新请求，鼠标与 Enter 都能本地清除。bootstrap 失败后桥接和标题栏按钮仍启用。普通 Chromium 中没有桥接且桌面按钮禁用，子 frame 没有桥接；普通客户端 TypeError 保留原始消息。全部场景无 renderer 异常。

原生与旧故障恢复回归覆盖真实进程/采样/权限/身份、专用任务创建终止、暂停恢复、令牌与 Host/Origin 边界、断连保留与重新连接。正式桌面回归覆盖全部导航、搜索排序、专用任务创建终止、对话框键盘焦点、960/1380/1600 窗口及 125% 缩放、外源导航与新窗口拒绝、原生窗口状态及退出。旧桌面测试仅新增首次提示出现时真实点击“暂不替换”；它使用临时偏好，没有写系统接管配置。

## 复现与证据

环境：Windows 11 x64、Node 24.16.0、Electron 45.0.0、Electron.NET 0.6.0；本次使用已恢复的专用 .NET SDK `output/tools/network-sdk-10.0.401`，没有更改系统 SDK 或 `global.json`。

```powershell
node --check tests/connection-recovery.mjs
node --check tests/desktop-e2e.mjs
$recoveryWorker = Join-Path (Get-Location) 'tests/connection-recovery-worker.ps1'
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList @(
    '-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $recoveryWorker + '"')
)
# 已提升终端可直接运行四套顺序验收：
powershell -NoProfile -ExecutionPolicy Bypass -File tests/connection-recovery-worker.ps1
# 对已准备的目录只验证正常采样、桥接、窗口按钮及退出：
powershell -NoProfile -ExecutionPolicy Bypass -File tests/connection-recovery-worker.ps1 -Smoke -ReleaseDirectory '<完整运行目录>'
```

worker 明确设置子进程 `DOTNET_ROOT`/`DOTNET_ROOT_X64` 到专用 SDK，只运行固定测试列表。默认桌面为 `output/connection-recovery/TaskManager-win-x64`；常规 API 和浏览器恢复回归运行新编译的 Release 主机。所有偏好与测试负载使用专用临时目录，测试结束清理。该运行目录包含任务开始时已有的网络工作基线，精确产物通过 [artifacts.json](evidence/connection-recovery/artifacts.json) 标识。

| 关键产物 | SHA256 |
|---|---|
| `TaskManager.Host.dll` | `3e81d81a9302d699c63a6eef410e1a3400a9f785aebe3a9e72b968505c0fb68e` |
| `.electron/custom_main.js` | `00878625ecb94a20b76dacf3136d76e39594b2bb6eae3021873c629ea8064cd7` |
| `.electron/window-preload.js` | `6f376bd509f6350a600f054a63e140ce74e9466af7f29e758f0d136f7cd599e4` |
| `wwwroot/index.html` | `6b0ddd1b1483c617f2d28f16efa1521b962467677f64640128a9f27065a6496a` |

可审阅证据为 [专项结果](evidence/connection-recovery/results.json)、[worker 退出码](evidence/connection-recovery/worker-status.json)、[正常桌面](evidence/connection-recovery/normal-electron.png)、[HTTP 断连桌面](evidence/connection-recovery/http-disconnected-electron.png)、[bootstrap 失败桌面](evidence/connection-recovery/bootstrap-unavailable-electron.png)。截图已检查，仅捕获测试应用内容。完整日志在 `output/playwright/connection-recovery`；旧桌面回归结果及多尺寸截图在 `output/playwright/final-desktop-results.json` 等 `final-desktop-*` 文件中。

原始非法 IPC 通道/命令注入没有动态执行，拒绝分支通过源码审查；不将可调用面限制和子 frame 检查冒充所有原始 IPC 攻击分支覆盖。没有重新执行全套安装/卸载、全部硬件采集、HWiNFO、服务控制或性能预算验收。

## 本机安装更新

2026-10-25 已通过标准 NSIS 包原位更新 `C:\Program Files\BlueDomainTaskManager`，安装退出 0。静态解包与安装后均核对 3,246 个文件；用户偏好与既有 IFEO 配置保持。旧安装包和 ZIP 已备份到 `output/connection-recovery/previous-package`。

最终安装器为 `output/releases/BlueDomainTaskManager-v1.1.1-win-x64-setup.exe`，169,317,042 字节，SHA256 `94026901cab531913ba48493e4f5b4689d96d957bdf5c746430927e058bc7a58`。包由已验收的 Host、生产 UI 与最终窗口脚本组装；[包核验](evidence/connection-recovery/package-verification.json) 与 [安装结果](evidence/connection-recovery/install-result.json) 记录精确产物。

安装后通过同一专项脚本 `--smoke` 从实际安装目录再验收 12 项：真实采样、三个固定窗口方法、无 renderer Node 权限、子 frame 无桥接、实际最小化/最大化/还原、关闭及精确进程/端口/ETW 清理。关闭实测 27 毫秒，无渲染器异常；见 [安装后结果](evidence/connection-recovery/installed-smoke-results.json) 和 [安装产物](evidence/connection-recovery/installed-artifacts.json)。测试仅使用临时偏好，未替用户选择系统入口设置。

复现命令：`powershell -NoProfile -ExecutionPolicy Bypass -File output/connection-recovery/install-worker.ps1`（标准管理员终端；固定安装目标、所有权与安装器哈希验证）。该 worker 调用已有 `scripts/build-installer.ps1 -SkipBuild` 的产物，完成文件及设置核验后调用 `tests/connection-recovery.mjs --smoke`。最终保持应用关闭，[独立复核](evidence/connection-recovery/final-state.json) 确认日常偏好未变，5188/5331/9331 无遗留监听。
