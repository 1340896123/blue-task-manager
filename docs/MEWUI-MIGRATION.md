# MewUI 原生单进程迁移

Takao 现在由 `Takao.Host.exe` 同时承载 .NET 采集服务、受保护操作、回环 API 和 MewUI 原生窗口。窗口不再通过 Qt、Electron 或独立渲染进程启动，关闭窗口会停止同一进程内的 ASP.NET 主机和采样后台服务。

## 运行结构

```text
Takao.Host.exe
├─ SnapshotService / CsWin32 真实 Windows 采集
├─ ProcessActionService / 其他受保护操作
├─ ASP.NET 回环 API（headless、诊断和既有自动化继续使用）
└─ MewUI Win32 + Direct2D Window
```

MewUI 使用 `Aprillz.MewUI.Windows` 0.21.1，启动时只注册 `Win32Platform` 与 `Direct2DBackend`。窗口布局保持原任务管理器结构：48px 顶部产品区、204px 左导航、单命令栏和九个业务页；表格使用 MewUI `GridView`，数据来自 `SnapshotService.Current`，未知值保留为“—”。默认窗口为 1100×720，最小 960×640。

页面和动作映射如下：

- 进程：真实进程快照、搜索、排序、运行新任务和带身份确认的结束任务。
- 性能：真实 CPU、内存、网络和磁盘摘要，未采样时显示明确的未知状态。
- 历史、启动应用、用户、详细信息、服务、系统信息：展示对应快照计数、来源说明和真实采样时刻。
- 设置：采样间隔与暂停状态通过 `SnapshotService.ApplySettings` 写回现有偏好服务。

## 可复现检查

```powershell
dotnet restore Takao.sln --force-evaluate
dotnet build Takao.sln -c Release --no-restore
```

显示验收仍只使用 1920×1080（100%）。运行桌面验收需要提升后的 Windows 进程；`--headless` 继续只启动真实后端和回环 API，不创建窗口。当前环境未执行提升后的显示链路，因此窗口像素截图和真实结束进程链路仍需在 Windows 1080P 会话中验收。
