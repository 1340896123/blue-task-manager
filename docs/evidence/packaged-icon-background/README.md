# 打包应用白色图标修复验收

2026-10-26，Windows x64。用户报告的实际路径为`C:\Program Files\WindowsApps\OpenAI.Codex_26.1021.3705.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe`。

原实现成功取得AUMID和32×32 PNG，但ICONONLY返回白色透明底图形，放到当前白色图标位后不可见。修复使用Windows公开的SIIGBF_ICONBACKGROUND，由Shell按应用注册背景色绘制底板；未硬编码品牌、解析包资源或修改Codex文件。接口、PNG缓存、CsWin32和JSON.NET链路沿用原合同。

| 检查 | 结果 |
|---|---|
| 全Solution Release构建 | 通过，0警告、0错误，使用仓库本地10.0.401 SDK |
| Web TypeScript、生产构建 | 通过 |
| 浏览器完整图标套件 | 通过，141项，[原始报告](browser-results.json) |
| 正式Electron完整图标套件 | 通过，154项，[原始报告](desktop-results.json) |
| 当前真实Codex进程 | 两种模式均11/11通过，32×32 PNG最少881个像素在白底上可见 |
| 实际16px行图标 | 两种模式均251个可见像素，[桌面截图](codex-desktop.png)、[原始PNG](codex-api.png) |
| 关闭与清理 | 浏览器8个、桌面12个自有身份全部退出，测试端口释放；精确自有ETW会话清理后读回 |
| 图标缓存/请求/全页几何 | 两种模式均通过；正式Electron含125%实际缩放读回及七页相关截图 |

测试使用隔离自包含目录`E:\taskManager\output\packaged-icon-fix-release`，Host DLL SHA256为`F0186345E235ECBB5AF7FC36876617C57A410C1141C4460157CCEA6A4553A1C5`。现有5188端口的用户实例未关闭或替换；截图为专门启动的验收实例。

可重现命令（仓库根目录，套件worker须在真实管理员PowerShell中执行）：

```powershell
& 'E:\taskManager\output\tools\network-sdk-10.0.401\dotnet.exe' build TaskManager.sln -c Release --no-restore -p:ElectronSkipExecCommands=true
npm run typecheck
npm run build
node --check tests/application-icons-adapters.mjs
$env:TASKMANAGER_RELEASE_DIRECTORY='E:\taskManager\output\packaged-icon-fix-release'
$env:TASKMANAGER_ICONS_PORT='15371'
$env:TASKMANAGER_ICONS_CDP_PORT='19371'
powershell -NoProfile -ExecutionPolicy Bypass -File tests/application-icons-adapters-worker.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/application-icons-adapters-worker.ps1 -Desktop
```

自包含候选通过锁定SDK执行`dotnet publish src/TaskManager.Host/TaskManager.Host.csproj -c Release -r win-x64 --self-contained true --no-restore -o E:/taskManager/output/packaged-icon-fix-publish -p:UseAppHost=true -p:PublishTrimmed=false -p:PublishSingleFile=false -p:PublishAot=false -p:ElectronSkipExecCommands=true`生成；将其实际`packaged-icon-fix-publishbin`运行时文件和`packaged-icon-fix-publishapp`桥接文件叠加到既有固定Electron45的隔离副本，保留完整`.electron`与离线.NET运行时。

首次运行发现Windows的TCP排除范围5285–5384覆盖旧测试端口5371，Kestrel返回10013；实际绑定确认15371/15372/19371可用后重跑，未更改系统端口策略。旧桌面测试还缺少首次启动“暂不替换”处理、依赖键盘缩放和单次退出事件，已在本套件补齐隔离偏好延后选择、产品缩放API实际读回、有界退出轮询与精确ETW清理。两次最终结果如上，未把此前失败算作通过。
