# 视觉层级验收结果

2026-10-24（Asia/Shanghai）。规格提交 `39f8b7c`，受测界面提交 `48b8909`，对应 [视觉层级规范](VISUAL-HIERARCHY.md)。本轮使用生产 UI → 同源 HTTP → JSON.NET → CsWin32 → 真实 Windows 数据；无业务 mock。提升 worker 的实际令牌均为 `elevated: true`。

## 结果

| 检查 | 实际结果 |
|---|---|
| V01 构建 | `npm run typecheck`、`npm run build` 通过；.NET Release 构建 0 警告、0 错误 |
| V02 标题与数据层级 | 正式 Electron 八页、三种尺寸/缩放共24个只读样式样本：每页一个24px/600 h1；设置 h2 为16px/600、24px行高，无原设置h3跳级；表头汇总与性能关键指标600字重 |
| V03 表头与列表 | 普通/汇总表头36/58px、紧凑/标准行32/40px；列名13px/500，排序列600字重、浅蓝背景与方向；网格排序、调宽、持久化与虚拟行对齐通过 |
| V04 尺寸与可访问性 | 1380×860、960×640、真实Electron125%（逻辑视口1104×688，DPR1.25）、reduced-motion通过；无页面横溢；主代理视查截图未发现标题/命令重叠；所采主要、次级及蓝色文字的实际组合背景最小对比4.858:1 |
| V05 真实链路 | 八页导航、共享搜索、真实性能、专用进程树/详情/优先级/结束动作通过；网格包含真实专用TCP/UDP连接表；未向用户服务或IFEO发出写操作 |
| V06 桌面证据 | 正式Electron窗口逐页截图、960及125%原生窗口截图已保存；本次测试身份全部退出，专用HTTP/CDP监听全部释放 |

非空样式覆盖包含5个汇总样本、2个性能关键指标样本、18个表列样本及3个设置样本；不把页面初始化时空节点当作指标通过证据。样式原始值在 `output/playwright/visual-hierarchy/computed-styles.json`，采集器只读取测试Electron CDP，没有修改页面或请求返回。

| 现有套件 | 模式 | 断言数 | 结果 |
|---|---|---:|---|
| static-v11 | 静态 | 3 | 通过 |
| windows-layout | Chromium | 182 | 通过 |
| windows-layout | 正式Electron | 247 | 通过 |
| grid-ui | 正式Electron | 1190 | 通过 |
| shared-search | 正式Electron | 150 | 通过 |
| process-desktop-v11 | 正式Electron | 34 | 通过 |
| windows-layout-audit | 只读清理审计 | 2 | 通过 |

以上套件均退出0；断言数包含逐列及逐尺寸检查，并非独立功能数。样式采样另列，不合并成自动化断言总数。相关A05/A06/A07/A16/A17/A18、B17/B18及L/G/S受影响场景由以上现有套件覆盖，本轮未新增测试文件。

## 可复现命令与证据

在 `E:\taskManager` 执行；两个worker按顺序运行，须等前一个 `complete: true` 才启动下一个。

```powershell
npm run typecheck
npm run build
dotnet build TaskManager.sln -c Release --no-restore -p:ElectronSkipExecCommands=true
Copy-Item -LiteralPath src/TaskManager.Host/wwwroot -Destination output/TaskManager-win-x64 -Recurse -Force
$taskWorker = Join-Path (Get-Location) 'tests/windows-layout-worker.ps1'
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $taskWorker + '"'),'-Suites','static-v11,windows-layout')
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $taskWorker + '"'),'-Suites','windows-layout,grid-ui,shared-search,process-desktop-v11,windows-layout-audit','-Desktop')
```

- 本轮worker结果、各套日志与结构化结果：`output/playwright/visual-hierarchy/results/`；Release构建日志：同目录上一级 `release-build.log`。
- 实际前后截图裁切对照：`output/playwright/visual-hierarchy/comparison.png`；仅裁切应用截图并增加“调整前/后”标签，采样内容未修改。
- 正式桌面截图：`output/playwright/visual-hierarchy/after/desktop-1380-进程.png`、`desktop-1380-performance.png`、`desktop-1380-settings.png`、`desktop-960-settings.png`；实际125%截图同目录 `desktop-125-*-native.png`。完整八页及各尺寸均已归档。

本轮为样式回归，未重新构建/安装NSIS包，未操作全局IFEO或用户服务，未重复全部原生硬件采集及安装生命周期专项；这些项目不计为本轮通过。主代理负责截图视查、最终差异检查与阶段提交。

## 2026-10-25 本机更新

用户明确要求更新本机后，重新执行 `scripts/build-installer.ps1 -SkipRestore -MakeNsisPath C:\nsis\makensis.exe` 和 `scripts/verify-installer.ps1`，完整生成自包含安装包及ZIP。安装包SHA256为 `291f44e1081aea1ab47e5a64d23f2893a1d57d76d5e8d3246459ecd4fedd691e`，169,316,083字节；独立解包核验3,242个文件及实际启动清单通过。

通过标准RunAs执行 `output/local-update-visual-20261024/install-worker.ps1`，核验安装标记和真实提升令牌后，在 `C:\Program Files\BlueDomainTaskManager` 原位静默安装，安装器退出0。安装文件全部逐一核对SHA256，现有偏好与入口配置的安装前后核对通过。版本号沿用1.1.1，以包SHA256区分此次构建。

安装目录的真实Electron以隔离偏好完成22项检查：八页24px/600标题、蓝色资源汇总、CPU关键指标、16px设置h2、实际Windows采样、捆绑runtime和renderer隔离均通过。三张安装版截图及测试身份/5310、9310监听清理结果在同一证据目录。

最后重新打开使用日常偏好的安装版，确认真实管理员Host、Electron窗口及5188端口，HTTP实际提供的index、JS、CSS原始字节均与核验包一致，日常偏好哈希保持不变，窗口按交付意图保持运行。最初使用Windows PowerShell 5解码HTML文本的核对因响应未声明charset而误报，已改为原始字节SHA256核对，通过后未重复安装；原始错误与修正复核分别保留在 `initial-verification-error.json`、`final-verification.ps1`。

可复现安装/验证脚本、构建日志、包核验、22项桌面结果、截图及最终PASS记录保存在 `output/local-update-visual-20261024/`；最终结果为 `install-result.json`、`reopened-application.json` 和 `install-status.json`。本次未执行实际安装的卸载或全局入口写入。
