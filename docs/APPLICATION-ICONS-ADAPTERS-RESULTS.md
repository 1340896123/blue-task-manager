# 全页应用图标与当前真实网络适配器验收

验收日期：2026-10-24（Asia/Shanghai）。业务基准：`89e08ab`，包含 `d790d14`、`47e54b3` 及移除设备过滤修复。使用隔离工作区的生产 Release Host、生产 Web 构建和正式 Electron.NET；真实管理员 Token 的专用 worker 执行。没有 API mock、模拟进程/服务/网卡数据或用户服务控制。

主目录对应的网卡修复为 `6bbf801`。本次隔离 checkout 未包含主目录同时开发的 HWiNFO 业务变更，本报告不对该并行功能作验收声明。

## 最终结果

最终统计见 [汇总](evidence/application-icons-adapters/summary.json)、[浏览器](evidence/application-icons-adapters/browser-results.json) 与 [正式 Electron](evidence/application-icons-adapters/desktop-results.json)。

新增真实链路套件浏览器 106 项、Electron 118 项通过；最终布局回归浏览器 181 项、Electron 246 项通过。连同核心回归与原生检查，本轮最终计入 840 项断言。新增套件分别清理 8/12 个测试创建身份，剩余身份与专用 HTTP/CDP 监听均为 0。

| 验收 | 结果与证据 |
|---|---|
| I01 | 锁定依赖还原、完整 solution Release、前端 typecheck/build/static 通过；最终网络修复后 Host Release 再构建为 0 警告、0 错误。真实 CsWin32 / JSON.NET / 管理员边界由原生回归与生产启动链核验。 |
| I02 | 专用真实 Host EXE 在进程列表、进程详情、详细信息列表及从用户会话定位的进程中显示可解码 PNG。独立 `ExtractIconEx` 与 API 图标均为 16×16，蓝色/白色像素分别 92/4；完全不透明像素与 alpha 轮廓一致。64 个非完全不透明像素的 RGB 转换不同，因此没有把两种 Windows 图标 API 的逐字节一致作为条件。用户账号行保留 UserRound 账号语义。 |
| I03 | 第二个专用真实 Host 退出、从实时快照消失后才首次请求该 identity 的历史图标，仍返回 200，像素与真实 Shell 源一致。错误历史身份、缺来源、清历史后返回 204；非法 kind 返回 400。 |
| I04 | 唯一真实 Startup `.lnk` 指向本次隔离 Host：列表和详情均有真实 PNG；运行服务、停止服务、关联服务按钮均显示实际二进制图标。结束树预览包含专用 root 与两个子进程的精确创建身份，成员数与真实预览 API 一致，显示真实 PNG；只预览、取消，不结束用户进程。 |
| I05 | 实际观察到 26 个 WindowsApps 路径进程，验证真实 AppsFolder 32×32 PNG，其中 628 个像素非透明；没有创建或伪造打包应用。 |
| I06 | 与独立 CsWin32 `GetIfTable2` 全集逐 LUID 比较：原始 54，保留当前存在的硬件 2（1 Up、1 Down）；排除 3 个 Windows 明确报告 NotPresent 的已移除硬件及其余 49 个非硬件/过滤/回环/隧道接口。Down 硬件保留 null 速率与 disconnected 原因；系统汇总只等于 Up 硬件分项之和；逐网卡历史没有被排除身份；旧虚拟网卡偏好回退到真实可用资源。 |
| I07 | 浏览器与正式 Electron 均覆盖全部含应用页面及用户/性能的 1380×860、960×640 截图；Electron 额外逐页实际 125% 缩放与原生窗口截图。应用图标 16px、占位 21px、紧凑行高 32px，无页面横溢。滚动/切页最大请求并发为 2；观察 Blob 替换及卸载释放。无 runtime/CSP 错误、服务控制或 IFEO 写。 |

首次核验发现仅 HardwareInterface 不能排除 Windows 保留的已移除设备；最终生产代码与独立 oracle 均按公开 `IfOperStatusNotPresent` 排除，不按名称猜测。最终截图采用活动硬件，仍保留已断开硬件条目。缓存测试观察的是本机真实列表数量，没有制造 512 个应用或声称已穷尽 LRU 上限。

## 可重现命令

在仓库根目录普通 PowerShell 发起标准 UAC worker；worker 会用独立 probe 验证实际提升 Token，然后执行 Node 脚本。正常 shell 不绕过产品权限边界。

构建采用 `dotnet restore TaskManager.sln --locked-mode`、`npm --prefix src/TaskManager.Web ci`、`npm --prefix src/TaskManager.Web run typecheck`、`npm --prefix src/TaskManager.Web run build`、`npm --prefix src/TaskManager.Web run test:static`、`dotnet build TaskManager.sln -c Release --no-restore -p:ElectronSkipExecCommands=true`。最终网卡修复后再运行 `dotnet build src/TaskManager.Host/TaskManager.Host.csproj -c Release --no-restore -p:ElectronSkipExecCommands=true`，结果仍为 0 警告、0 错误。既有固定 Electron 45 运行时复制到隔离运行目录，没有重新安装或修改依赖。

```powershell
$worker = Join-Path (Get-Location) 'tests/application-icons-adapters-worker.ps1'
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $worker + '"'))
# 前一 worker complete 后再执行正式 Electron；不并发使用其运行目录或端口。
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $worker + '"'),'-Desktop')

$layout = Join-Path (Get-Location) 'tests/windows-layout-worker.ps1'
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $layout + '"'),'-Suites','windows-layout')
# 等待前一 worker complete。
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $layout + '"'),'-Suites','windows-layout','-Desktop')
```

原始运行结果位于 `output/playwright/application-icons-adapters` 与 `output/playwright/windows-layout`；提交证据只保留紧凑 JSON、50 张截图和独立图标，没有导出完整机器清单或令牌。补充核心回归使用既有真实提升 worker：`ui-smoke` 36 项、`recovery-e2e` 4 项、`desktop-e2e` 42 项全部通过；原生 `native-api` 78 项、`native-metadata-v11` 5 项通过。最终后端的 `native-telemetry-v11` 24 项通过：[27 个真实样本](evidence/application-icons-adapters/native-telemetry-summary.json)，采样 p95 65.85ms、Host 整机 CPU 0.34%、句柄 614→613、网卡 2；其测量范围为 headless Host 与专用真实负载，不含 renderer。

## 截图

- [正式 Electron 1380 进程](evidence/application-icons-adapters/desktop-1380-进程.png)、[详细信息](evidence/application-icons-adapters/desktop-1380-详细信息.png)、[历史](evidence/application-icons-adapters/desktop-1380-应用历史记录.png)、[启动](evidence/application-icons-adapters/desktop-1380-启动应用.png)、[服务](evidence/application-icons-adapters/desktop-1380-服务.png)、[用户](evidence/application-icons-adapters/desktop-1380-用户.png)、[性能](evidence/application-icons-adapters/desktop-1380-性能.png)。
- 同目录 `browser-1380-*`、`browser-960-*`、`desktop-960-*`、`desktop-125-*` 为对应逐页证据；截图前等待实际 PNG 解码。`desktop-125-native.png` 是测试所属 HWND 的原生捕获。
- [进程详情](evidence/application-icons-adapters/desktop-process-detail.png)、[退出后的历史](evidence/application-icons-adapters/desktop-exited-history.png)、[真实启动快捷方式详情](evidence/application-icons-adapters/desktop-startup-detail.png)、[运行服务](evidence/application-icons-adapters/desktop-running-service-detail.png)、[停止服务](evidence/application-icons-adapters/desktop-stopped-service-detail.png)、[关联服务](evidence/application-icons-adapters/desktop-associated-services.png)、[结束树预览](evidence/application-icons-adapters/desktop-tree-preview.png)。

只清理测试创建且创建时间匹配的进程、临时偏好/端点，以及校验 target 后的唯一 Startup `.lnk`。最终 JSON 记录精确身份清理和端口释放结果。
