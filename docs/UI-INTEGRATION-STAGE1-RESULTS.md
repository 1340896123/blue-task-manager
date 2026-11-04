# 界面接入阶段1：盘点与后端结果

日期：2026-11-04。实现前基线 `69e8689d9ab101fe7b205738764d9a6c758cf17c`；规格见 [UI-INTEGRATION.md](UI-INTEGRATION.md)。本阶段不改Qt业务代码，保留原工作区未提交修改。

## 结果

- 原现场：727条进程网络全部null，ETW系统logger创建返回1450/capacity。系统CPU/内存、5块磁盘、2个GPU已可读取，并非占位。
- 新实现自动回退公开Microsoft-Windows-Kernel-Network manifest实时session。生产Host实测sample187：745条进程中733条有真实网络数值、27条正速率，source=kernelNetworkManifest、status=available、事件/缓冲丢失均0，3812个缓冲完成消费。其余不可确认身份继续明确未知。
- 专用TCP4/UDP4/TCP6/UDP6套接字收发均通过；每协议独立空闲进程与停止后完整窗口均真实归零。已观测全部8种manifest收发event ID/layout；602个缓冲完成消费。
- 网络专项 **PASS，68项检查**，覆盖原经典v2和新增manifest v0解析边界、真实四协议流量、空闲/停止、暂停恢复、身份变更、过期和释放/重建。
- 审计Host65416按路径+创建FILETIME134382267946366103核验，精确清理进程及1个自有ETW session；另2个既有Takao session保持。专项自身通过Dispose释放测试资源。

## 可复现命令与证据

1. `dotnet build src/Takao.Host/Takao.Host.csproj -c Release --no-restore`：退出0，0警告/0错误。
2. `dotnet build tests/Takao.NetworkTests/Takao.NetworkTests.csproj -c Release --no-restore`：退出0，0警告/0错误。
3. 提升令牌执行 `dotnet run --project tests/Takao.NetworkTests -c Release --no-build -- E:\taskManager\output\ui-integration\stage1-network-results.json`：退出0，68项通过；完整日志 `output/ui-integration/stage1-network.log`，退出码 `stage1-network.exit`。
4. `wevtutil gp Microsoft-Windows-Kernel-Network /ge:true /f:xml`：本机schema证据 `output/ui-integration/kernel-network-local.xml`；GitHub固定源码研究见规格。
5. 真实生产快照脱敏摘要 `output/ui-integration/stage1-live-snapshot.json`，精确清理证据 `output/ui-integration/stage1-audit-cleanup.json`；两份均PASS。
6. `git diff --check`：退出0。

未执行：本阶段不做Qt显示/物理键鼠/安装更新与全局系统接管，由后续阶段联合验收。现场没有HWiNFO共享内存，GPU无观测引擎、受保护进程和RunOnce状态不可推断的分支仍保留真实原因；不会以假值或驱动绕过。经典系统logger的有效路径仍由原解析检查覆盖，本机实际新链路走manifest回退。

阶段2交接：processNetwork新增source；按统一盘点补齐Qt已有字段、I/O与用户别名原因、性能独立来源、历史退出行详情、用户跨页筛选和无设备/外部来源反馈。阶段3运行最终Qt/原生真实验收。
