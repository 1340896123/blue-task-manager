# Takao

Windows 10/11 x64 任务管理器，使用 **Qt 6 Widgets + .NET 10**。Qt 原生界面通过标准本机 HTTP/JSON API 访问后端；真实 Windows 采集与操作使用 CsWin32，C# JSON 使用 JSON.NET。

八个页面包括进程、性能、应用历史记录、启动应用、用户、详细信息、服务和设置。保持 Windows 蓝色设计、进程身份保护、未知值及原因、管理员启动和原有系统入口兼容。当前规格见 [Qt 迁移说明](docs/QT-MIGRATION.md)，旧技术栈的文档与截图仅为历史记录。

## 运行

完整目录位于 `output/Takao-win-x64`。运行 `start.cmd` 或 `Takao.Host.exe`，同意 Windows UAC；Host 就绪后启动 `Takao.Qt.exe`，关闭窗口会停止本次后端及采样。不要单独复制 EXE，也不要直接启动 Qt 子程序。自包含发布无需预装 .NET、Qt、Node 或联网。

已接管 Windows 任务管理器入口时，移动目录前先在设置取消。`restore-windows-task-manager.cmd` 保留原生产维护入口。旧 `TaskManager.Host.exe` / `TaskManager.Launcher.exe` 别名只用于已有入口兼容，不自动改写用户系统配置。

## 从源码构建

需要 .NET SDK（见 global.json）、Visual Studio C++ x64 Build Tools、CMake、Ninja、7-Zip。首次构建自动下载并核验固定 Qt 6.8.3 MSVC SDK；可用 `-QtPrefix` 或 `QT_ROOT_DIR` 指向已安装的同版本 SDK。

```powershell
./scripts/build.ps1
./scripts/publish-desktop.ps1
./scripts/build-installer.ps1 -SkipBuild -OutputDirectory output/qt-release-local
./scripts/verify-installer.ps1 -InstallerPath output/qt-release-local/Takao-v1.1.1-win-x64-setup.exe
```

安装器脚本同时生成 ZIP；输出目录应使用未有同名产物的目录，以保留已有发布包。真实 API 测试需管理员 PowerShell，Qt 测试脚本会通过标准 Windows RunAs 启动测试工作进程。

`build.ps1` 生成需要本机 .NET 的开发目录；`publish-desktop.ps1` 生成包含 .NET/ASP.NET、Qt DLL、Windows platform 插件、VC Runtime 与许可的离线目录。安装器沿用 NSIS 每机安装及受保护更新流程。构建不使用容器。

## 验证

```powershell
dotnet build Takao.sln -c Release
node tests/native-api.mjs
dotnet run --project tests/Takao.StartupTests -c Release
dotnet run --project tests/Takao.NetworkTests -c Release
dotnet run --project tests/Takao.DiskTests -c Release
./scripts/test-qt-desktop.ps1
./scripts/test-qt-components.ps1
```

本轮显示测试仅 **1920×1080 窗口画面物理像素、100%测试缩放**。`test-qt-components.ps1` 以与生产 EXE 相同的 Qt UI 静态库驱动原生组件，接入真实后端并用 WGC 截图；它不发送 Windows 输入，不代表真实键鼠验收。`test-qt-desktop.ps1` 是独立的 WGC/FYLHelper 硬件输入 E2E。本机硬件输入受鼠标坐标不匹配阻塞，实际范围见 [本轮记录](docs/QT-MIGRATION-RESULTS.md)。仅结束测试专门创建的进程；不为覆盖测试更改用户启动项、服务或系统入口。验收采用用户指定 ui-ux-pro-max 的桌面适用标准，参见 [验收矩阵](docs/ACCEPTANCE.md) 和 [设计规范](design-system/takao/MASTER.md)。

后端独立调试：`Takao.Host.exe --headless --port=5191`；仅监听 `127.0.0.1`。未知/无权限/预热保留明确状态；曲线只绘制真实采样，进程 I/O、磁盘与网络保持各自口径。

Qt 动态链接与源码/许可说明见 [Qt NOTICE](assets/licenses/Qt/NOTICE.md)。
