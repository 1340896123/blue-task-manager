# 项目工作约定

## 用户约束（优先于下面的实现建议）

- React + Tailwind CSS + Electron.NET；本机 Windows 为目标平台，按用户参考图布局，采用 Windows 蓝色视觉风格。
- 禁止模拟业务数据；必须接入真实 Windows 采集与操作链路。Windows 原生接口通过 Microsoft.Windows.CsWin32 生成绑定调用。
- C# 的 JSON 序列化、反序列化统一使用 JSON.NET（Newtonsoft.Json），禁止 System.Text.Json。npm/TypeScript 等工具链要求的 JSON 清单仍遵循其标准格式；JavaScript 的原生 JSON 处理只用于标准 HTTP 数据交换及本地偏好，不替代后端 JSON.NET。
- 新增功能先研究优秀开源实现，优先采用标准协议。底层设计参考 [TaskExplorer](https://github.com/DavidXanatos/TaskExplorer)，研究记录见 docs/RESEARCH.md；不移植其专有远程协议或内核驱动。
- 按阶段顺序使用多个子代理开发；同一时间只有一个阶段代理写业务代码。每阶段完成检查后进行独立 Git 提交。
- 优先制定 PRD、规范、设计 Token；最终以真实链路 E2E 验收，测试结束进程只针对测试创建的专用进程。
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
- 实现前阅读 docs/PRD.md、docs/ARCHITECTURE.md、docs/CONVENTIONS.md 和 design-system/task-manager/MASTER.md。
- 遇到权限不足、采样预热、进程退出、平台能力缺失时返回明确状态；不得把未知值转换成 0。
- 交付前运行 docs/ACCEPTANCE.md 中的必验项目，保存可重现命令、结果与截图路径。
