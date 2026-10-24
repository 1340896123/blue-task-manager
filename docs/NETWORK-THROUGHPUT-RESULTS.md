# 网络吞吐验收结果

日期：2026-10-25（Asia/Shanghai）。当前记录为原生后端阶段；浏览器与正式 Electron 的 N06/N07 将由后续阶段补齐，不以本记录代替界面验收。

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

## 尚待后续阶段

N06/N07 的真实浏览器/正式 Electron 网络列、排序、详情、CSV、960×640/1380×860/125%截图及 N08 桌面正常关闭和开销记录尚未在本阶段执行。N05 的系统 ETW 槽位耗尽、真实丢事件/缓冲、权限撤销故障未主动制造；相应生产错误/容量/过期与解析拒绝分支已实现，不能将隔离边界断言当作这些故障的真实成功采样。
