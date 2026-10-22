# 阶段 3：真实 React 界面验收

日期：2026-10-23（Asia/Shanghai）。前端采用 React 19.3.1、TypeScript 5.9.3、Vite 8.3.5、Tailwind CSS 4.3.3、TanStack React Virtual 3.14.22、Lucide React 1.61.0；精确版本与完整传递依赖由两个 npm lockfile 固定。

## 实现范围

- 参考图的标题搜索区、左侧七个业务导航、底部设置、命令栏、资源汇总表头、虚拟滚动进程表与状态栏。Windows 蓝色 Token 贯穿八页；本地 Windows 字体与单一 SVG 图标族，无外部字体请求。
- 全部数据走同源真实 Windows API，集中 TypeScript DTO 和数据客户端。禁止演示数据；未知指标保留 null 语义、可访问原因和“—”。进程 I/O 明确区别于物理磁盘吞吐；本次历史只显示后端实际观测增量。
- 串行轮询、超时、卸载取消、重新 bootstrap 获取重启后令牌；断线保留最后快照，采样过期与暂停分别标记。性能图只绘制实际时间戳的采样点，超出当前采样周期容差的时间段不连线。
- 按进程身份选择，数值排序与 aria-sort，名称/PID/路径/发布者搜索；对话框有原生 dialog、Tab 焦点边界、Escape 与焦点恢复。运行与结束经后端令牌验证；确认仅终止一个身份。
- 采样周期、暂停/继续实际修改后端采样器；密度偏好存本地。浏览器无窗口模式禁用窗口操作并解释，桌面阶段由 bootstrap.desktop 启用真实窗口按钮。

## 可复现命令

从仓库根运行：

```powershell
npm ci
npm ci --prefix src/TaskManager.Web
npm run typecheck
npm run build
dotnet build src/TaskManager.Host -c Release
dotnet build tests/TaskManager.TestWorkload -c Release
npx playwright install chromium
npm run test:ui
```

UI 构建输出到 `src/TaskManager.Host/wwwroot`，生成文件不入 Git。浏览器冒烟脚本自行启动端口 5192 的真实生产主机，工作目录设为 `src/TaskManager.Host`，使用生产 UI，创建专用真实负载并清理本次创建的句柄和身份。未使用 mock、route.fulfill、假返回或注入数据。

## 已执行结果

- TypeScript strict 检查：通过；Vite 生产构建：通过，CSS 约 28 KB、JS 约 304 KB（gzip 约 95 KB）。
- `npm run test:ui`：36 个断言通过，浏览器运行时错误为 0。Playwright 1.64.1 + Chromium 156.0.8078.4。
- 真实 PID 搜索、虚拟渲染、真实终止能力选择、GPU/网络缺失原因、身份信息、排序语义、暂停后 sampleId 稳定、全部页面、采样间隔、密度、无效 Win32 启动错误、Escape 焦点恢复均通过。
- 实际 UI 新建专用负载、独立 Windows 进程存在检查、取消结束仍存活、确认结束后实际退出、另一测试进程保持存活：通过。
- 1380×860 与 960×640 无页面级横向溢出；详细宽表在自身容器内滚动。截图经阶段代理及主代理目视检查，符合参考布局与 Windows 蓝色方向。

本地可重现证据（截图是本机真实进程列表，非演示数据）：

- `output/playwright/stage3-processes-1380.png`
- `output/playwright/stage3-performance-1380.png`
- `output/playwright/stage3-processes-960.png`
- `output/playwright/stage3-smoke-results.json`

本阶段是生产浏览器 UI 验证。Electron.NET 窗口启动、最大化/最小化/关闭、真实桌面闭环及关闭后端口释放由阶段 4 继续验收；不以浏览器通过替代桌面通过。
