# 全页网格验收结果

> 历史验收记录：本文保留当时的产品名、路径与构建结果。自 2026-10-28 起产品统一命名为 Takao；当前命令与目录以 [README](../README.md) 为准，历史记录不作为本次重命名的验收证据。

日期：2026-10-23（Asia/Shanghai）。环境：Windows 11 企业版 x64 10.0.26200；Node 24.16.0；.NET SDK 10.0.401。生产实现基线为 `5d03351`，表头可读性修复为 `3ec7dc7`；规格为 `a808317`。其他并行任务的搜索更新独立提交，不属于本网格修改。

真实浏览器专项 **1189 项通过**，正式 Release Electron.NET 专项 **1190 项通过**。两者均覆盖 7 个网格、63 个可显示列；没有 mock API、替换采集器或虚构业务行。结果文件分别为 `output/playwright/grid-browser-results.json`、`output/playwright/grid-desktop-results.json`。JSON 仅保存检查名称、聚合行数、截图路径及清理结果，不保存令牌或全机器进程列表。

| 验收 | 结果与证据 |
|---|---|
| G01 | 通过。旧 version 1 配置缺少 columnWidths 正常读取；合法宽度经 JSON.NET 保存读回。未知 key、跨页 key、原型 key、超过 40 项、63/1601 越界和 NaN/Infinity 均返回 400，原偏好不变。类型检查、生产前端与 Host Release 构建通过，C# 0 警告 0 错误。 |
| G02 | 通过。进程/详细信息各 18 列、历史 7 列、启动 5 列、用户 7 列、服务 4 列、TCP/UDP 4 列均有真实排序按钮、分隔调整柄及列宽设置入口。性能与设置继续使用原有非表格布局。 |
| G03 | 通过。每表首/中/末列分别 +40/-24 CSS 像素，其余列宽不变；表总宽、列头与单元格边界误差小于 1.1 CSS 像素。真实采样跨两帧保持拖动，移出表头后可释放；Escape、pointercancel、lost capture、blur、卸载清理状态及 capture。拖动不排序或改变选择，64～1600 上下界有效，虚拟行 DOM 不超过 80。 |
| G04 | 通过。每列双向点击及每表 Enter/Space 与 aria-sort 一致。可见真实身份与原始快照对照，数字、累计 BigInt、时间戳和端点数字端口正确排序；已知首部、未知尾部及全未知列稳定顺序均检查。真实测试父子进程保留同级排序、折叠和选择身份，详情仍指向原 PID+FILETIME。 |
| G05 | 通过。pointermove 不落盘，完成后走既有防抖；跨页独立、隐藏列重显保留自定义宽度。真实关闭并重启 Host 后宽度恢复；页面及全局重置恢复默认。连接表切换页签、原生刷新保留宽度/排序。 |
| G06 | 通过。键盘方向键、Shift、Home、Enter、点击输入与双击复位均验证；嵌套列宽对话框 Tab 限制焦点，Escape 只关闭上层并回焦触发按钮。960×640、1380×860、浏览器 125% 等效视口及 Electron 实际 125% 缩放均无文档横向溢出，表头/数据保持对齐；启用 reduced-motion。修复数字列标题从左裁字，默认“内存（工作集）”完整显示，窄列从尾部省略。 |
| G07 | 通过。真实 UI→HTTP→JSON.NET→CsWin32→Windows。专用负载提供 IPv4/IPv6 TCP/UDP 共 4 个原生端点，与该负载记录端口一致。正式 Electron 窗口启动、关闭和重启成功，应用 HTTP/CDP 端口关闭，测试身份已退出。 |

复现命令（管理员 PowerShell，保留生产管理员入口要求）：

```powershell
npm run typecheck
npm run build
dotnet build src/TaskManager.Host/TaskManager.Host.csproj -c Release --no-restore
dotnet build tests/TaskManager.TestWorkload/TaskManager.TestWorkload.csproj -c Release --no-restore

# 浏览器：真实生产 Host + Chromium
$env:TASKMANAGER_GRID_DESKTOP = '0'
$env:TASKMANAGER_GRID_PORT = '5230'
node tests/grid-ui.mjs

# Electron：使用完整 Release 专用副本；独立端口可与浏览器并行
$env:TASKMANAGER_GRID_DESKTOP = '1'
$env:TASKMANAGER_GRID_PORT = '5231'
$env:TASKMANAGER_GRID_CDP = '9251'
$env:TASKMANAGER_GRID_RELEASE_DIRECTORY = 'E:\taskManager\output\grid-acceptance\desktop'
node tests/grid-ui.mjs
```

专项桌面副本由完整 `src/TaskManager.Host/bin/Release/net10.0-windows10.0.19041.0` 复制，修复后重新同步 `wwwroot`。浏览器默认使用此 Release 构建；不使用可能由其他任务更新的 `output/TaskManager-win-x64`。本次从普通 shell 以隐藏 `Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden` 启动受控 worker；对应复现脚本与退出状态保存在 `output/grid-acceptance/run-grid-{browser,desktop}.ps1`、`output/playwright/grid-{browser,desktop}-worker-status.json`。

截图（两种模式各一套）：

- `output/playwright/grid-{browser,desktop}-services-1380.png`
- `output/playwright/grid-{browser,desktop}-connections.png`
- `output/playwright/grid-{browser,desktop}-layout-960.png`
- `output/playwright/grid-{browser,desktop}-layout-1380.png`
- `output/playwright/grid-{browser,desktop}-zoom-125.png`
- `output/playwright/grid-{browser,desktop}-default-readable-titles.png`

既有回归由根代理独立运行：原生与静态 237 项、页面 49 项、恢复 4 项通过；正式更新后的 `process-ui-v11.mjs` 34 项通过（`output/grid-acceptance/process-ui-final-status.json`）。旧 ui-smoke/desktop-e2e 的固定搜索可访问名称与并行共享搜索更新不兼容，原脚本失败；仅在 output 中替换搜索定位器的兼容补验分别 36/42 项通过。合计 402 项回归检查通过，不宣称原始整个 regression 命令通过；旧搜索兼容问题与本网格无关。桌面回归截图为 `output/playwright/final-desktop-{1380,960,1600,125-percent,terminate-confirm,complete}.png`。

实测范围：本机会话来源只有 1 条用户行，因此未虚构额外会话验证跨用户数值分布；真实累计值使用 BigInt 对照，没有制造超过 JavaScript 安全整数的计数。排序专项对真实快照暂停采样以便准确对照，另有持续采样拖动专项验证刷新稳定性。清理仅针对本次创建的进程身份、临时负载文件和端点，不结束用户业务进程，不控制用户服务。
