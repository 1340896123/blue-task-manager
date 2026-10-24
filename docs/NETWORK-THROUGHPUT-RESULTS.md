# 网络吞吐验收结果

日期：2026-10-25（Asia/Shanghai）。原生后端与浏览器/正式 Electron 网络专项已执行；最终自包含交付和完整性能测量由交付阶段补记。

## 已执行

- `dotnet build TaskManager.sln -c Release --no-restore -p:ElectronSkipExecCommands=true`：通过，0 警告、0 错误。
- 对全部 `tests/*.mjs` 执行 `node --check`：通过。
- 使用标准 Windows RunAs 隐藏启动固定 `tests/windows-layout-worker.ps1`，worker 独立确认真实提升令牌。`native-api`：78 项通过；`native-core-v11`：25 项通过；`static-v11`：3 项通过。
- `tests/TaskManager.NetworkTests/bin/Release/net10.0-windows10.0.19041.0/TaskManager.NetworkTests.exe output/playwright/network-throughput/native-final.json`（真实提升令牌）：51 项通过。生产采集器、真实本机专用进程和临时 TCP/UDP echo 端点，无替代生产来源。

| 传输 | 独立 socket 发送字节 | 独立 socket 接收字节 | 有效窗口接收 B/s | 有效窗口发送 B/s |
|---|---:|---:|---:|---:|
| TCP IPv4 | 4,669,440 | 4,669,440 | 804,223 | 804,223 |
| UDP IPv4 | 218,112 | 217,088 | 65,857 | 65,857 |
| TCP IPv6 | 3,506,176 | 3,506,176 | 371,878 | 371,878 |
| UDP IPv6 | 291,840 | 290,816 | 57,913 | 57,913 |

速率是某个已确认完整的 ETW 窗口，socket 字节是传输持续期间总量，二者不是同一时间范围，不据此声称精确等同。四种传输均验证方向非零、另一个空闲身份仍为零，以及停止传输后窗口起点晚于停传时刻的完整零窗口。实际事件版本均为2：TCP IPv4 send36B/receive28B，IPv6 send60B/receive52B；UDP IPv4 28B、IPv6 52B。来源窗口约1～2秒，独立后台 flush/消费者等待不计入基础进程采样路径。

另通过首次预热、暂停不发布、恢复丢弃暂停期并重新预热、创建身份变化不继承旧PID值、不可确认身份保持未知、过期清除旧非零值、Dispose 后重建独立会话。初版尝试比较 BuffersWritten/BuffersRead 在真机失败，因此最终使用同一消费流的标准 ETW 唯一采样标记；两缓冲计数保留为诊断，不当作完整性判据。

进程强杀诊断实际确认系统 logger 会残留。强杀测试现由独立 CsWin32 probe 取得本次 Host 的原始创建 FILETIME，退出后只停止捕获的 `PID+FILETIME+GUID` 精确会话。正常生产关闭仍通过 STOP/CloseTrace/等待消费线程/释放 provider 与本地内存完成。未停止用户 ETW、内核 logger 或其他进程。未通过安装驱动、改权限校验或新增生产 shutdown API 规避测试。

原生可复现证据：[native.json](evidence/network-throughput/native.json)；本机输出 `output/playwright/network-throughput/native-final.json`、`output/playwright/windows-layout/native-api-browser.log`、`native-core-v11-browser.log`、`static-v11-browser.log`。fixture 由测试直接创建并按已持有的 Process 句柄结束，临时目录/端点已释放。强杀诊断只清理其捕获的单个自有会话。

## 界面专项与真实桌面

前端 `npm run typecheck`、`npm run build` 和 Host Release 构建通过。进程与详细信息网格沿用原有网络列 key、列宽、排序和选择；网络列显示真实接收+发送，详情分开呈现收/发/合计和独立 ETW 来源时间。复制信息、CSV 包含网络值与来源时间；CSV 数字使用原始 bytes/sec，未知留空并附原有不可用原因。更多菜单及设置可查看当前来源、预热/暂停/失败原因，硬件网卡图保持独立口径与实线/虚线图例。

`tests/network-throughput-ui.mjs` 使用生产 Host、真实专用 echo 进程、本地 TCP 流量和 Playwright，未替换 API 或采集器。浏览器最终 **34 项通过**，正式 Electron **34 项通过**。检查同 identity 的 API/列/详情/CSV 值一致、独立时间不冒充请求时间、数值升降序及未知末尾、选择保持、键盘调宽与 JSON.NET 持久化、复制、960×640/1380×860、实际 Electron 125% 读回、reduced-motion、硬件网卡性能图和来源状态。Electron 正常窗口关闭后，精确本实例 ETW 会话消失，未借测试 helper 代清理；应用身份、fixture、端口均已释放。浏览器故意结束其无窗口 Host 时使用精确 FILETIME 清理 helper。

浏览器独立 TCP socket 最终发送与接收各 16,924,672 字节。采样从 1 秒切换为 2 秒、5 秒后，分别得到真实新来源窗口 1,744.68ms、5,040.83ms，目标双向速率有效；速率分母采用实际窗口，不强行写成配置周期。Electron 另有约 8MB 双向真实传输，源窗口约 2,022ms。详情截图已滚动到网络收/发/合计/来源字段，避免仅拍到概览上半部。

验收期间实际遇到 Windows ETW `1450`：快照继续刷新至 sample43、真实 socket 双向持续传输，但网络接收/发送/合计均为 null 且给出 `capacity` 原因，证明资源不足不会冒充0。只读审计确认一条属于本轮失败回归的残留会话；主代理通过归档 worker58284、pages-ui 失败及创建时窗确认 ownership，只清理该精确 PID+FILETIME+GUID，会话释放后专项恢复。另修复测试用 `logman query` 的已知 `0x80071068` 枚举竞态：仅此状态最多重试4次，其他错误仍失败，查询失败不当空列表。N05 的容量不足分支已有实际证据；真实丢事件/缓冲及权限撤销仍未主动制造。

系统 .NET10 在验收中变为不可用，测试从本机官方 NuGet 10.0.12 包构建局部标准运行时 `output/tools/network-dotnet`，仅在提升 worker 中设置 `DOTNET_ROOT`/`DOTNET_ROOT_X64`，未修改系统运行时安装或管理员校验。界面专项使用独立目录 `output/network-throughput-release`，不覆盖并行工作的原 release。最终交付阶段另做标准自包含 publish，不能用测试环境变量替代离线交付。

可复现命令（仓库根目录 PowerShell；正常已有 .NET10 时可省略 RuntimeDirectory）：

```powershell
$networkWorker = (Resolve-Path 'tests/network-throughput-worker.ps1').Path
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $networkWorker + '"'),'-Mode','browser','-ReleaseDirectory','E:\taskManager\output\network-throughput-release','-RuntimeDirectory','E:\taskManager\output\tools\network-dotnet')
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $networkWorker + '"'),'-Mode','desktop','-ReleaseDirectory','E:\taskManager\output\network-throughput-release','-RuntimeDirectory','E:\taskManager\output\tools\network-dotnet')
```

两模式顺序执行，等待 `output/playwright/network-throughput/worker-<mode>.json` complete。原始报告为同目录 `browser.json`/`desktop.json`，均 clean=true；容量诊断保留为 `desktop-capacity.json`。审阅证据见 [browser.json](evidence/network-throughput/browser.json)、[desktop.json](evidence/network-throughput/desktop.json)、[desktop-capacity.json](evidence/network-throughput/desktop-capacity.json)。桌面截图：[网络列](evidence/network-throughput/desktop-process-1380.png)、[窄窗](evidence/network-throughput/desktop-process-960.png)、[125%](evidence/network-throughput/desktop-process-125.png)、[网络详情](evidence/network-throughput/desktop-detail.png)、[硬件网卡](evidence/network-throughput/desktop-adapter.png)。

## 最终交付待补

N08 的完整120点历史预热后 ≥30 秒 CPU/内存/句柄/HTTP 性能与最新源代码的正式自包含包验证仍由最终交付阶段执行。真实丢事件/缓冲、权限撤销故障未主动制造，不能将隔离边界断言当作这些故障的真实成功采样。
