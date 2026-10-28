# Windows 11 全页标准布局：真实验收结果

> 历史验收记录：本文保留当时的产品名、路径与构建结果。自 2026-10-28 起产品统一命名为 Takao；当前命令与目录以 [README](../README.md) 为准，历史记录不作为本次重命名的验收证据。

日期：2026-10-24，Asia/Shanghai。以 [WINDOWS-LAYOUT.md](WINDOWS-LAYOUT.md) 和 [L01–L10](ACCEPTANCE.md) 为合同；在本机真实 Windows、生产 CsWin32 采集器和正式 Release Electron.NET 中执行，没有业务 mock 或截图假数据。

实现基准为 `92098f7`；验收确认的小修分别提交为 `750a999`（summary 键盘与小曲线 DOM）、`1e73d76`（CPU/内存重复图表标签）、`80ba660`（GPU 精确身份 tooltip/可访问名称）、`6cef09f`（断连/过期的可见状态）。最终 UI 构建对应 `6cef09f`；测试与证据在本报告所在提交。未新增 npm/.NET 依赖或协议，没有容器操作、权限校验绕过或生产采集器替换。

## 验收结论

L01–L10 全部通过。原 `npm run test:e2e` 覆盖的 19 个套件逐项执行，并增加浏览器/桌面网格、共享搜索、布局与清理检查：共 26 个运行组，25 个通过、1 个保留 Windows 窗口可观察性部分结果、0 个最终失败；可计数断言共 3,701 条，性能另外验证 3 项实测预算。逐套统计见 [suite-summary.json](evidence/windows-layout/suite-summary.json)。运行组名 `desktop` 是管理员 worker 的日志分组；pages/recovery/native-management 仍按其源代码运行真实无窗口 Host，不冒充 Electron 检查。

| ID | 结果 | 实际证据与行为 |
|---|---|---|
| L01 | 通过 | Web typecheck/生产构建、.NET Release 构建成功，0 警告/0 错误；正式目录同步最新 wwwroot，JSON.NET/CsWin32/管理员入口保留 |
| L02 | 通过 | 空搜索下八页 1380×860 截图；48px 顶栏、56px 单行命令栏、固定左导航/底部设置；无常驻底栏、品牌版本徽标或多层操作栏 |
| L03 | 通过 | 六表默认列顺序、资源表头 58px/普通 36px、表头与虚拟单元格像素对齐；列宽互相独立、表内滚动、更多/详情/树/筛选/列/CSV 全链路保持 |
| L04 | 通过 | CPU、内存及实际磁盘/网卡/GPU统一资源列，逐个选择右图；本机实测 63 个资源磁贴，曲线使用真实独立时间点；无横排类别 tabs/第二层设备侧栏 |
| L05 | 通过 | Windows 纵向设置行、原生来源/能力折叠；查询自动展开命中组；IFEO 保持挂载，筛选不重读或重放写入 |
| L06 | 通过 | 浏览器/桌面七网格与全部列排序/调宽/持久化、虚拟化、未知末尾、跨页返回和旧 version 1 偏好回归；两种运行方式共享搜索各 150 项通过 |
| L07 | 通过 | 冻结时后端 sampleId 继续增长；暂停时稳定；断连保留原身份/数值和真实时刻；重启恢复、令牌轮换；用专用偏好文件的真实 I/O 写入失败验证错误可见与恢复 |
| L08 | 通过 | 960×640 compact、1380×860、真正 Electron 125%、reduced-motion；菜单 Enter/Space/Escape、嵌套筛选/列不误关、焦点恢复；最小物理 960×640 与125%组合也通过原生详情/菜单检查 |
| L09 | 通过 | 专用真实进程运行、定位、详情、优先级和效率模式实际读回/恢复、取消及结束/结束树；身份/令牌/自身保护保留。用户服务主命令先读权限、点击后再次读原生状态，固定目标确认后只取消，未发送该 UI 控制请求 |
| L10 | 通过 | 正式 output/TaskManager-win-x64 的 Electron.NET、真实窗口最小化/最大化/关闭、置顶/尺寸/缩放保存与重启；逐页截图；最后应用/负载身份和所有验收端口审计为零 |

## 截图与缩放核验

[证据目录](evidence/windows-layout) 保存 49 张仅应用范围的截图；[manifest.json](evidence/windows-layout/manifest.json) 映射原始生成路径。每页均有 `browser-1380-*`、`browser-960-*`、`desktop-1380-*`、`desktop-960-*` 和 `desktop-125-*`，英文页名依次为 processes/performance/history/startup/users/details/services/settings。

- [桌面进程](evidence/windows-layout/desktop-1380-processes.png)、[性能](evidence/windows-layout/desktop-1380-performance.png)、[设置](evidence/windows-layout/desktop-1380-settings.png)。
- [960px 进程](evidence/windows-layout/desktop-960-processes.png)、[125%完整设置](evidence/windows-layout/desktop-125-settings.png)、[125%原生窗口](evidence/windows-layout/desktop-125-processes-native.png)。
- [冻结](evidence/windows-layout/desktop-frozen.png)、[暂停](evidence/windows-layout/desktop-paused.png)、[断连](evidence/windows-layout/browser-disconnected.png)、[恢复](evidence/windows-layout/browser-reconnected.png)、[偏好写入失败](evidence/windows-layout/desktop-preference-save-failure.png)、[服务确认后取消](evidence/windows-layout/desktop-service-confirm-cancel.png)。

初次使用 Playwright `page.screenshot` 时，Electron 125% 图像被裁到 CSS 尺寸 1104×688；它不是完整视觉证据，已经替换。最终截图采用 CDP `Page.captureScreenshot(fromSurface=true,captureBeyondViewport=false)`，输出完整物理客户端 1380×860，并保存 Win32 窗口 1396×868 的独立补图（含窗框）。真实读回为 innerWidth/Height=1104×688、visualViewport=1104×688/scale1、DPR=1.25；设置页窗口控件最右1104、命令最右1080、设置控件最右≤1068 CSS px。全部页面控件边界检查通过，表格的水平滚动局限于自身。未缩小 UI 来规避截屏或溢出。完整几何记录在 `output/playwright/windows-layout/desktop-results.json`。

## 性能

正式桌面在120个真实历史点后测量约33.8秒、28个 UI 样本；[原始统计](evidence/windows-layout/performance-results.json) 与 [120点截图](evidence/windows-layout/desktop-120-real-points.png) 可复核。

| 指标 | 本机实测 | 预算 |
|---|---:|---:|
| Host 整机 CPU | 0.2495% | ≤1% |
| 真实采样耗时 p95 | 334.36ms | ≤500ms |
| 搜索到下一 requestAnimationFrame p95（含自动化协议，非像素呈现时延） | 25.2ms | ≤100ms |

63个真实资源磁贴共653个后代 DOM、78条分段 path，没有装饰网格、逐点 title 或 circle；主图保留逐点提示和单点/断流语义。默认进程视图实测30行、797个 DOM，虚拟化仍有界。这些是本机本轮结果，不代表所有设备的固定耗时。

## 真实边界与初轮修正

系统入口检查最终为 `PARTIAL_WINDOWS_OBSERVABILITY`，详见 [system-locations-results.json](evidence/windows-layout/system-locations-results.json)：当前用户 Startup、公共 Startup、Windows 启动应用设置、Windows 服务管理器四项均获真实 Shell 受理；公共 Startup/设置/服务窗口已独立核实，当前用户 Startup 的精确路径窗口本轮未能独立观察。没有把 Shell 受理写成四个窗口都已验证。

B12 的自建 Windows 服务成功启停/重启/超时链未执行，本轮未创建专用服务；SCM 创建权限与只读动作权限已实测。不得以用户现有服务补齐此成功链。初轮沿用旧 native-management 时，曾对一个实际拒绝停止权限的用户服务发送不可能 expectedPid=4294967295 的 stop 请求，生产 API 返回403；随后检查确认状态与PID均和请求前一致（当轮断言 `permission rejection leaves user service state unchanged` 通过），没有实际停止服务。此旧探测已移除；最终 `native-management-v11` 改为两次只读权限/状态核验，并通过89条断言。不能把“请求被拒绝、无实际变更”表述为“初轮从未发送控制”。

旧测试中的菜单 role、横向资源类别、常驻状态文字、启动项下方详情带等断言已改为当前真实入口；没有为兼容旧测试加回旧布局。另修复测试自身的 details 开关复用、原生窗口就绪与进程退出事件等待竞态。原生文件/系统入口初轮超时后曾遗留两个测试 Electron CDP：独立创建时刻与对应本轮套件吻合，使用各自原来已验证空闲的CDP会话 `Browser.close` 关闭；未按名称或端口直接杀未知进程。随后套件在原端口重跑。

最终 [cleanup-audit.json](evidence/windows-layout/cleanup-audit.json) 在 2026-10-24 10:01:09 +08:00 记录：专用应用/运行时/负载路径下进程0，28个本轮HTTP/CDP端口监听0。文件/系统入口仅关闭测试新建且精确核实的窗口，已有窗口保留。Host原始 stdout 没有落盘；操作令牌只保留内存，没有提交本机完整进程转储、用户偏好或秘密。

## 可复现命令

在仓库根目录使用 PowerShell。生产管理员校验始终保留；普通 shell 通过 Windows 标准 RunAs 启动固定验收 worker，worker 自行确认真实 TokenIsElevated。不要同时执行共享端口的套件。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -SkipRestore
# 小修后重新生成UI并同步两个真实测试目录（已实际执行）
npm run build
Copy-Item -LiteralPath src/TaskManager.Host/wwwroot -Destination output/TaskManager-win-x64 -Recurse -Force
Copy-Item -LiteralPath src/TaskManager.Host/wwwroot -Destination src/TaskManager.Host/bin/Release/net10.0-windows10.0.19041.0 -Recurse -Force

# 浏览器与原生API套件，须等待该worker完成再启动下一个
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList @(
  '-NoProfile','-ExecutionPolicy','Bypass','-File',"$PWD/tests/windows-layout-worker.ps1",
  '-Suites','static-v11,native-api,native-metadata-v11,native-core-v11,native-management-v11,native-telemetry-v11,gpu-identity-v11,process-ui-v11,pages-ui-v11,ui-smoke,recovery-e2e,grid-ui,shared-search,windows-layout')

# 正式桌面；worker显式把grid指向output/TaskManager-win-x64，排除历史目录
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList @(
  '-NoProfile','-ExecutionPolicy','Bypass','-File',"$PWD/tests/windows-layout-worker.ps1",
  '-Suites','desktop-e2e,process-desktop-v11,native-desktop-v11,system-locations-v11,restricted-process-ui-v11,product-icon,taskbar-integration,desktop-performance-v11,grid-ui,shared-search,windows-layout,windows-layout-audit',
  '-Desktop')
Get-Content output/playwright/windows-layout/worker-desktop-status.json
```

上面两组是本轮实际分组命令的等效套件并集，已通过的无关原生组没有为了得到单条 `npm run test:e2e` 输出再重复执行。每套实际命令为 `node tests/<suite>.mjs`；最终逐套日志在 `output/playwright/windows-layout/<suite>-<worker模式>.log`，各批次退出码/耗时在 `worker-*-YYYYMMDD-HHmmss.json`，完整网格/共享搜索/nativeDesktop结果在 `output/playwright`，性能在 `output/performance`。仅保留必要摘要与应用截图到Git。
