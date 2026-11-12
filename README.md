# Takao

Windows 10/11 x64 任务管理器，使用 **.NET 10 + MewUI Win32/Direct2D**。采集服务、受保护操作、回环 API 和原生窗口都运行在同一个 `Takao.Host.exe` 进程中；真实 Windows 采集使用 CsWin32，C# JSON 使用 JSON.NET。

界面保留顶部产品区、204px 左侧导航、单命令栏和九个页面：进程、性能、应用历史记录、启动应用、用户、详细信息、服务、设置和系统信息。视觉采用 Windows Fluent 中性浅色层次、蓝色强调、可见焦点和清晰状态文本。当前迁移合同见 [MewUI 原生单进程迁移](docs/MEWUI-MIGRATION.md)，设计 Token 见 [MASTER.md](design-system/takao/MASTER.md)。

## 运行

完整目录位于 `output/Takao-win-x64`。运行 `start.cmd` 或 `Takao.Host.exe` 并同意 Windows UAC；关闭窗口会停止同一进程内的采样服务和监听端口。不要单独复制或启动 UI 子程序。自包含发布无需预装 .NET、Node 或联网。

已接管 Windows 任务管理器入口时，移动目录前先在设置取消。`restore-windows-task-manager.cmd` 保留原生产维护入口。旧 `TaskManager.Host.exe` / `TaskManager.Launcher.exe` 别名只用于已有入口兼容，不自动改写用户系统配置。

## 从源码构建

需要 .NET SDK（见 `global.json`）。MewUI 通过 NuGet `Aprillz.MewUI.Windows` 0.21.1 引入；普通构建不需要 Qt、CMake、Ninja 或 Electron。

```powershell
./scripts/build.ps1
./scripts/publish-desktop.ps1
./scripts/build-installer.ps1 -SkipBuild -OutputDirectory output/mewui-release-local
./scripts/verify-installer.ps1 -InstallerPath output/mewui-release-local/Takao-v1.1.1-win-x64-setup.exe
```

`build.ps1` 生成开发目录；`publish-desktop.ps1` 生成包含 .NET/ASP.NET 和 MewUI 依赖的离线目录。构建不使用容器。

## 验证

```powershell
dotnet restore Takao.sln --force-evaluate
dotnet build Takao.sln -c Release --no-restore
```

桌面显示验收只使用 1920×1080（100%）。提升后的 Windows 会话中运行真实 UI 验收；`Takao.Host.exe --headless --port=5191` 仅启动真实后端和 API，不创建窗口。未知、无权限和预热状态保持明确原因，曲线与指标只使用真实成功采样。
