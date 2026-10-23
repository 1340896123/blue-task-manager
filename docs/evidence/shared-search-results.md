# 顶部共用搜索真实链路验收

日期：2026-10-23（Asia/Shanghai）。Windows 11 x64 / 10.0.26200.0，Node v24.16.0，.NET SDK 10.0.401。功能提交 `6609911`，本轮执行时仓库 HEAD `3ec7dc7`。合同见 [SHARED-SEARCH.md](../SHARED-SEARCH.md)。

全部使用生产 Release Host、生产 React 页面、JSON.NET 与 CsWin32；未注入假快照或拦截伪造 API。以标准 `Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden` 启动独立管理员 worker，未修改管理员校验。实际令牌证明为 `output/shared-search/worker-elevation.json`（`elevated: true`）。每个测试使用独立临时偏好目录；生产账户偏好不改动。

## 本轮结果

| 实际命令 | 结果 | 断言 | 本地证据 |
|---|---|---:|---|
| `node tests/native-core-v11.mjs` | 通过 | 25 | `output/shared-search/regression-native-core-v11.log` |
| `node tests/ui-smoke.mjs` | 通过 | 36 | `output/shared-search/regression-ui-smoke.log`、`output/playwright/stage3-smoke-results.json` |
| `node tests/recovery-e2e.mjs` | 通过 | 4 | `output/shared-search/regression-recovery-e2e.log`、`output/playwright/final-recovery-results.json` |
| `node tests/process-ui-v11.mjs` | 通过 | 34 | `output/shared-search/process-ui-v11.log`、`output/playwright/process-ui-v11-results.json` |
| `node tests/pages-ui-v11.mjs` | 通过 | 49 | `output/shared-search/pages-ui-v11.log`、`output/playwright/pages-ui-v11-results.json` |
| `node tests/shared-search.mjs`（浏览器） | 通过 | 150 | `output/shared-search/shared-search-browser.log`、`output/playwright/shared-search-browser-results.json` |
| `node tests/shared-search.mjs`（正式 Electron.NET） | 通过 | 150 | `output/shared-search/shared-search-desktop.log`、`output/playwright/shared-search-desktop-results.json` |
| `node tests/desktop-e2e.mjs`（正式 Electron.NET 核心） | 通过 | 42 | `output/shared-search/regression-desktop-e2e.log`、`output/playwright/final-desktop-results.json` |

以上共 490 条最终通过断言。正式桌面读取 `src/TaskManager.Host/bin/Release/net10.0-windows10.0.19041.0`，包含实际 `.electron`，没有使用旧 `output/TaskManager-win-x64`。浏览器和桌面搜索各自无跳过项、运行时错误或 IFEO/服务写请求。

## 搜索覆盖

| 合同 | 实际验证 |
|---|---|
| S01 / A07 | 同一不存在词逐页进入全部八页：文本原样保留、立即出现空态，无旧行、无不相关性能图或设置卡片；不依赖 Enter 或刷新。 |
| S02 / A06 | 由生产新任务对话框创建专用 TestWorkload；独立 Windows CIM 核验 PID 与创建时间（CIM 仅精确到微秒，动作仍使用精确 native FILETIME）。同一 PID 在进程、详细信息、会话历史精确匹配。服务、启动、用户本机均有真实记录，按实际名称/会话 ID 匹配成功。 |
| S03 | 五类资源均可搜；按本机实际网络别名、接口 ID、物理磁盘名称、GPU 名称与 LUID 筛选。临时切入匹配设备后清空均恢复原内存选择，没有覆盖资源偏好。 |
| S04 | 逐项搜更新速度、自动采样、冻结、密度、默认进程视图、侧栏、置顶、缩放、偏好、CsWin32 来源、进程详情能力与任务栏入口；相关条目可见且无关行隐藏。连续筛选未增加入口接管 GET，未执行接管或服务控制。 |
| S05 / A16 / A17 | 八页 Ctrl+F、清空后焦点归还；旧选择被过滤后结束按钮禁用。原进程套件继续覆盖树祖先、真实详情/连接、优先级与效率恢复、CSV 全结果、专用目标终止、冻结/暂停/条件 GET/密度持久化。页面套件保留 960×640、1104×688、1380×860 回归，正式桌面核心包含真实 125% 缩放与 reduced-motion。 |
| S06 | 新任务及历史真实身份定位更新共用词；随后输入新词并跨性能、详细信息重挂，旧 PID 不再覆盖新输入。错误创建时间终止为 409、缺写令牌为 403；仅专用目标参与写测试。 |
| S07 / B10 | 等待 JSON.NET 原子落盘后实际关闭/重启 Host 恢复 `searchQuery`；旧 version 1 文件仅有 `pages.processes.query` 能迁移；清空后保存并重启不被旧词复活。恢复默认清空共用词；257 字符与 NUL 输入被后端 400 拒绝且原偏好不变，文件不含 token。桌面同样执行实际重启。 |

本轮保留旧进程测试的真实操作数量，仅把“各页独立 query”改成共用词，并在两套既有 UI 测试进入设置前清空搜索。

## 可重现命令

在仓库完成依赖恢复、生产 Web 构建与 Host Release 构建后，从标准 UAC 管理员 PowerShell 执行：

```powershell
Set-Location -LiteralPath 'E:\taskManager'
$env:TASKMANAGER_RELEASE_DIRECTORY = 'E:\taskManager\src\TaskManager.Host\bin\Release\net10.0-windows10.0.19041.0'
node tests/native-core-v11.mjs
node tests/ui-smoke.mjs
node tests/recovery-e2e.mjs
$env:TASKMANAGER_PROCESS_UI_PORT = '5293'
node tests/process-ui-v11.mjs
$env:TASKMANAGER_PAGES_UI_PORT = '5294'
node tests/pages-ui-v11.mjs
$env:TASKMANAGER_SHARED_SEARCH_PORT = '5290'
$env:TASKMANAGER_SHARED_SEARCH_CDP = '9290'
$env:TASKMANAGER_SHARED_SEARCH_DESKTOP = '0'
node tests/shared-search.mjs
$env:TASKMANAGER_SHARED_SEARCH_DESKTOP = '1'
node tests/shared-search.mjs
node tests/desktop-e2e.mjs
```

本机实际提升启动：`Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File E:\taskManager\output\shared-search\search-worker.ps1' -Verb RunAs -WindowStyle Hidden`。worker 将每套退出码与日志分别落到 `output/shared-search`；浏览器首次通过结果保留在其独立 JSON 与日志中，后续 worker 仅重跑桌面及两套旧 UI。

## 截图及清理

- 搜索浏览器：`output/playwright/shared-search-browser-{all-pages-empty,settings-filter,performance-restored,real-pid,settings-clear}.png`。
- 搜索桌面：`output/playwright/shared-search-desktop-{all-pages-empty,settings-filter,performance-restored,real-pid,settings-clear}.png`。已实际查看设置筛选与专用 PID 截图。
- 原 UI：`output/playwright/v11-process-tree.png`、`v11-process-960.png`、`v11-resources.png`、`v11-service-details.png`、`v11-settings-960.png`。
- 桌面核心：`output/playwright/final-desktop-1380.png`、`final-desktop-960.png`、`final-desktop-125-percent.png`、`final-desktop-complete.png`；恢复：`final-recovery-disconnected.png`、`final-recovery-connected.png`。

新增脚本只终止自身新任务的 PID+创建 FILETIME，临时文件仅删除本次 `mkdtemp` 目录。每轮关闭核验本 Host/Electron 的精确创建身份均退出，HTTP/CDP 端口释放；正式桌面核心额外确认无关测试负载不受关闭影响。

首次桌面核心运行在窗口初始化 `GetContentBoundsAsync` 的 1000ms 框架时限处退出；在新增搜索桌面链结束后串行重跑通过 42 项。初次浏览器 smoke 使用的 Release `wwwroot` 尚未同步，重新构建 Host 同步生产资源后通过。新增测试在调试中修正 CIM 时间精度、Windows 子进程 `signalCode` 退出判断、等待异步实际桌面初始化三项测试适配；最终结果以上表为准。

本轮未执行 NSIS 安装/卸载、IFEO 接管写入或扩展服务控制成功链；不将只读搜索回归等同于这些范围通过。完整网格、硬件性能预算与安装包验收仍由各自专门矩阵负责。
