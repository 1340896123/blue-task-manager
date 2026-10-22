# 阶段2：真实Windows后端验收

日期：2026-10-23（Asia/Shanghai）。Windows x64，.NET SDK 10.0.401，Node v24.16.0。此记录只覆盖原生API阶段，不代表浏览器或Electron桌面E2E通过。

执行命令：

```powershell
dotnet build src/TaskManager.Host -c Release
dotnet build tests/TaskManager.TestWorkload -c Release
node tests/native-api.mjs
```

两项Release构建均退出0，0警告0错误。锁定CsWin32 0.3.367、Newtonsoft.Json 13.0.4、AspNetCore.Mvc.NewtonsoftJson 10.0.10；包锁文件已提交。所有生产原生API来自CsWin32生成绑定，无手写DllImport/LibraryImport，无System.Text.Json应用调用。

2026-10-23首次完整成功运行：`PASS`，79个断言，真实主机PID 100484，首帧sampleId=1。测试令牌只保存在内存且未写入本报告。测试创建了专用TaskManager.TestWorkload进程，利用它的保存PID+FILETIME执行结束；没有终止用户进程。

| 验收项 | 本阶段结果 |
|---|---|
| A01后端构建 | 通过；前端构建后续阶段执行 |
| A02/A03 | 真实快照推进、真实主机PID存在；专用负载PID/名称/工作集与独立Get-Process比对一致 |
| A04 | 系统首帧CPU=null/预热，新进程首帧CPU=null；后续专用负载CPU和写I/O大于0；无负速率 |
| 原生慢列表/系统指标 | SCM和WTS返回真实非空列表；工作集来自PSAPI，系统内存来自GlobalMemoryStatusEx，运行时间来自GetTickCount64；GetIfTable2总量第二帧有有效值，非逐进程网络 |
| A08 | 所有进程GPU和网络为null且说明未接入；I/O字段命名与字节单位正确 |
| A09/A10 | API CreateProcess真实创建负载；不存在路径拒绝；TerminateProcess只结束专用负载，独立存活检查失败且后续快照移除 |
| A11 | 错误FILETIME返回409且专用负载继续存活；PID0、PID4、自身主机返回403 |
| A12 | 缺失令牌、错误令牌、外站Origin、错误Host端口均403；Kestrel只监听127.0.0.1 |
| A13 | 相邻观测CPU/I/O累计大于0；清除历史立即生效，起始时间推进 |
| A14 | 暂停后超过两个采样周期sampleId保持不变；设置周期与paused立即反映；继续后获得新样本 |
| A19后端部分 | 使用CsWin32 + JSON.NET；无业务模拟数据。Electron/React/Tailwind验证属于后续阶段 |

清理：测试负载已退出，测试主机进程已退出，端口5188与5191没有本应用监听，测试专用临时目录已删除。测试可以用同一命令重新执行，测试阶段不保存全机进程清单。UI、断连交互、可访问性、尺寸适配及真实Electron窗口关闭验收尚未执行。
