# 项目工作约定

## 用户约束（优先于下面的实现建议）

- 当前技术栈为 Qt 6 Widgets + .NET 10；删除 Electron/Electron.NET 桌面方式与 React/Tailwind 渲染入口。本机 Windows 为目标平台，按用户参考图布局，采用 Windows 蓝色视觉风格。现行迁移规格见 docs/QT-MIGRATION.md。
- 本轮显示测试只执行 1920×1080（1080P），按用户指定的 `C:\Users\jieok\.skills-manager\skills\ui-ux-pro-max\SKILL.md` 桌面适用规则验收；旧文档的其他分辨率/Electron测试矩阵仅为历史记录。
- 测试仅覆盖本次调整及其直接影响范围，不默认执行全量回归。按实际变更选择相关构建、组件、界面或真实链路检查；共享组件采用必要的代表页面，不因局部修改重跑全部页面、API、采集器、安装流程或完整 E2E。确有跨模块影响时只扩展到受影响部分，并说明依据；下方及历史文档的完整矩阵是用例参考，不是每次修改的必跑清单。
- 禁止模拟业务数据；必须接入真实 Windows 采集与操作链路。Windows 原生接口通过 Microsoft.Windows.CsWin32 生成绑定调用。
- C# 的 JSON 序列化、反序列化统一使用 JSON.NET（Newtonsoft.Json），禁止 System.Text.Json。npm/TypeScript 等工具链要求的 JSON 清单仍遵循其标准格式；JavaScript 的原生 JSON 处理只用于标准 HTTP 数据交换及本地偏好，不替代后端 JSON.NET。
- 新增功能先研究优秀开源实现，优先采用标准协议。底层设计参考 [TaskExplorer](https://github.com/DavidXanatos/TaskExplorer)，研究记录见 docs/RESEARCH.md；不移植其专有远程协议或内核驱动。
- 按阶段顺序使用多个子代理开发；同一时间只有一个阶段代理写业务代码。每阶段完成检查后进行独立 Git 提交。
- 优先制定 PRD、规范、设计 Token；涉及真实链路的改动采用对应的定向 E2E 验收，测试结束进程只针对测试创建的专用进程。
- 所有工作自主推进，日常实现、检查、修复不再请求用户介入。

<!-- CODEGRAPH_START -->
## CodeGraph

In repositories indexed by CodeGraph (a `.codegraph/` directory exists at the repo root), reach for it BEFORE grep/find or reading files when you need to understand or locate code:

- **MCP tool** (when available): `codegraph_explore` answers most code questions in one call — the relevant symbols' verbatim source plus the call paths between them, including dynamic-dispatch hops grep can't follow. Name a file or symbol in the query to read its current line-numbered source. If it's listed but deferred, load it by name via tool search.
- **Shell** (always works): `codegraph explore "<symbol names or question>"` prints the same output.

If there is no `.codegraph/` directory, skip CodeGraph entirely — indexing is the user's decision.
<!-- CODEGRAPH_END -->

## 标准容器构建流程

本机为 Podman，Windows 宿主、WSL VM `podman-machine-default`，没有 Docker CLI。仅当任务实际需要容器构建时依序执行：

1. 停止当前构建，强杀 Podman：`powershell -Command "Get-Process podman | ForEach-Object { Stop-Process -Id $_.Id -Force }"`。
2. `wsl --shutdown`，关闭所有分发。
3. `podman machine start`。
4. 先以小型 build 验证恢复，再正式构建。

应用本身不依赖容器，不为普通 .NET/npm 构建执行上述步骤。

## 可用工具

- IDA Professional 9.3：`C:\Program Files\IDA Professional 9.3\ida.exe`。
- 实现前阅读 docs/PRD.md、docs/ARCHITECTURE.md、docs/CONVENTIONS.md 和 design-system/takao/MASTER.md。
- 遇到权限不足、采样预热、进程退出、平台能力缺失时返回明确状态；不得把未知值转换成 0。
- 交付前根据本次 diff 从 docs/ACCEPTANCE.md 选择直接相关项目，保存可重现命令、结果与必要截图路径，不默认运行完整矩阵。
