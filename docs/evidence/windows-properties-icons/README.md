# Windows文件属性图标对照验收

2026-10-26，Windows x64。用户要求进程列表参考Windows属性页中`C:\Program Files\WindowsApps\OpenAI.Codex_26.1021.3705.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe`的透明底深灰图标。

打包桌面应用现在优先以CsWin32 ExtractIconEx提取真实EXE嵌入小图标，再回退AppsFolder ICONBACKGROUND，最后沿用文件Shell回退。仅真实资源覆盖包图标：SHGetFileInfo也会为无资源EXE成功返回通用图标，不能用其非空结果判断资源存在。普通进程及启动快捷方式继续使用既有Shell语义；缓存、独立COM STA与原生句柄释放沿用原合同，不按品牌改色或画背景。

| 检查 | 结果 |
|---|---|
| 全Solution Release构建 | 通过，0警告、0错误，锁定10.0.401 SDK |
| Web TypeScript与生产构建 | 通过 |
| 静态规范检查 | 通过，3项 |
| 浏览器完整图标套件 | 通过，143项，[报告](browser-results.json) |
| 正式Electron完整图标套件 | 通过，156项，[报告](desktop-results.json) |
| 当前真实Codex进程 | 两种模式均11/11通过；API图像与独立ExtractIconEx小图标的opaque像素和alpha轮廓差异均0 |
| Codex真实图像 | 16×16、175个非透明像素、101个白底可见像素；实际16px行也为101个可见像素，无蓝色底板 |
| 无嵌入资源MSIX | 独立资源计数观察8个，真实cowork-svc.exe验证保留32×32 AppsFolder图像，1024个白底可见像素 |
| 关闭与清理 | 浏览器8个、桌面12个测试自有身份全部退出；专用端口释放；精确自有ETW会话清理并读回 |
| 图标缓存/请求/全页几何 | 两种模式通过；正式Electron包含125%实际缩放读回与七页截图 |

对照文件：[正式桌面截图](codex-desktop.png)、[API PNG](codex-api.png)、[独立EXE小资源](codex-resource-small.png)、[独立EXE大资源](codex-resource-large.png)、[无资源MSIX回退PNG](packaged-fallback.png)。图像保留Windows原生透明度，与用户属性页参考的来源一致。32px大资源用于对照属性页，16px小资源用于进程列表。

隔离自包含候选目录为`E:\taskManager\output\windows-properties-icons-release`，Host DLL SHA256为`F9600CAFF606980E5A47545FA8D77A876075D6DC91EEEECF259EF1E16DFCAC20`。测试仅操作专门创建的Host、workload、唯一Startup快捷方式与其精确ETW会话；未结束用户Codex进程、写系统入口或控制服务。

可重现命令（仓库根目录；图标worker在真实管理员PowerShell中运行）：

```powershell
& 'E:\taskManager\output\tools\network-sdk-10.0.401\dotnet.exe' build TaskManager.sln -c Release --no-restore -p:ElectronSkipExecCommands=true
npm run typecheck
npm run build
node --check tests/application-icons-adapters.mjs
node tests/static-v11.mjs
$env:TASKMANAGER_RELEASE_DIRECTORY='E:\taskManager\output\windows-properties-icons-release'
$env:TASKMANAGER_ICONS_PORT='15371'
$env:TASKMANAGER_ICONS_CDP_PORT='19371'
powershell -NoProfile -ExecutionPolicy Bypass -File tests/application-icons-adapters-worker.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/application-icons-adapters-worker.ps1 -Desktop
```

自包含候选使用同一锁定SDK执行`dotnet publish src/TaskManager.Host/TaskManager.Host.csproj -c Release -r win-x64 --self-contained true --no-restore -o E:/taskManager/output/windows-properties-icons-publish/ -p:UseAppHost=true -p:PublishTrimmed=false -p:PublishSingleFile=false -p:PublishAot=false -p:ElectronSkipExecCommands=true`生成。将其实际`bin`运行时/生产Web文件叠加到上轮完整固定Electron45隔离包副本，保留`.electron`、离线.NET/ASP.NET运行时和许可。测试端口已在启动前实际绑定验证，未改Windows排除端口范围。
