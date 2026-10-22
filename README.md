# 蓝域任务管理器

面向 Windows 的真实系统任务管理器，技术栈为 React、Tailwind CSS、Electron.NET、ASP.NET Core 与 CsWin32。界面采用参考图的标题搜索区、固定侧栏、命令栏和高密度数据表，以 Fluent 蓝色 Token 统一视觉。

本项目按阶段实施，设计与接口约定先于应用代码。实施和验收进度以 Git 提交及验收记录为准。

- [产品需求](docs/PRD.md)
- [架构与数据合同](docs/ARCHITECTURE.md)
- [工程规范](docs/CONVENTIONS.md)
- [设计系统与 Token](design-system/task-manager/MASTER.md)
- [真实链路验收矩阵](docs/ACCEPTANCE.md)
- [开源研究与决策](docs/RESEARCH.md)

只显示真实 Windows 数据。无法获取的指标以“不可用”及原因呈现；进程 I/O 不冒充磁盘物理吞吐，应用历史明确限定为本次采集会话。最终交付需同时通过后端、浏览器 UI 和 Electron 桌面验证。

最终交付必须包含 Windows x64 Release 可运行目录及一键启动脚本，并记录实际输出路径与运行前提；不要求安装器。E2E需在真实 Electron.NET 窗口内完成搜索和真实任务启动/结束闭环，不能以单独浏览器验证替代桌面应用验证。
