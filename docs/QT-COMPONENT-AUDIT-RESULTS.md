# Qt 组件 Fluent 审查与修复结果

日期：2026-11-04（Asia/Shanghai）。组件清单、缺陷与固定开源依据见 [QT-COMPONENT-AUDIT.md](QT-COMPONENT-AUDIT.md)。修复提交：`cb59511`；确认框 Enter 行为修复：`307ee20`。本轮显示检查仅为 1920×1080 可见物理像素、100% 测试缩放。

## 修复范围

| 项目 | 最终实现 |
|---|---|
| 复选框 | 共用 `FluentStyle` 绘制圆角、蓝色勾选/部分选中和禁用状态；保持原生 QCheckBox 语义 |
| 数值输入 | `Theme::SpinBox` 保留原生编辑、步进范围和命中区域，在真实按钮区域叠加统一箭头；修复 QSS 吞掉箭头的问题 |
| 下拉列表（用户追加） | 沿用并完善工作区已有列表封装；统一箭头、展开/按下/禁用状态、完整32px条目和单一滚动条；已提交选择有独立3×16px蓝色指示，移动高亮不会冒充提交 |
| 菜单/表格/树 | 共用勾线与轮廓箭头；排序读取 QStyleOptionHeader 的实际 sortIndicator，区分两个方向 |
| 七处业务确认 | 共用 Fluent 标题、正文、24px边距、44px中文动作；取消初始焦点/default；明确聚焦确认后 Enter 可以确认；保留原有业务请求与身份校验 |
| 启动错误 | 共用中文关闭弹窗，保留非法启动返回码2的代码路径 |

新主题模块为 `src/Takao.Qt/FluentStyle.*` 和 `FluentDialogs.*`，现有 `Theme.h` 引用统一角色。没有引入第三方 UI 运行时、模拟业务数据或新协议。Windows 原生文件选择器、现有导航/设置卡片/图表及 .NET/CsWin32/JSON.NET 链路保留。

## 实际验证

| 检查 | 本轮结果与证据 |
|---|---|
| .NET Release | `dotnet build Takao.sln -c Release --no-restore`：退出0，0警告/0错误；SDK 10.0.403 |
| Qt Release | Qt 6.8.3/MSVC；`output/qt-tests-build` 的 `Takao.Qt`、`Takao.Qt.Integration`、`Takao.Qt.Fluent.Tests` 编译/链接退出0 |
| 专项组件 | **26项通过，6张WGC截图**；真实 Qt 绘制、checkbox原生可访问角色、spin步进/边界/箭头可见性、排序方向、下拉完整条目/选择指示/滚动末项、确认框焦点与 Qt Enter 事件；无模拟业务数据。`output/fluent-component-audit/controls/results.json`、`captures.json` |
| 真实业务组件链路 | **80项通过，20张WGC截图**；八页、真实资源/采样/偏好、四处业务下拉展开、新任务失败保留输入、专用真实进程启动/身份确认/取消/结束及反馈。`output/fluent-component-audit/after/native-results.json`、`captures.json`。通过调用真实Qt控件方法驱动，**不等同物理键鼠通过** |
| 对比度 | **28项通过**；白色勾线/蓝填充5.674:1，关键边界/白底3.452:1，说明/白底6.385:1；包括选择/热度合成背景。`output/fluent-component-audit/contrast.json` |
| 正式生命周期 | **20项通过**；正式自包含运行库、真实采样/专用进程身份终止、Qt/Host关闭/端口释放、精确自有Job/ETW崩溃清理。`output/fluent-component-audit/lifecycle/results.json` 保存专用PID、创建FILETIME和实际终止结果 |
| 物理键鼠全流程 | **未通过**。`desktop-final/results.json` 保存八页及搜索共21项实际检查，随后 Alt+8 未切到预期设置页，采样选择器未找到；更早一次为临时窗口在输入前消失。另两次导航诊断也未完成，不能累计为通过。记录在 `desktop*/results.json`，后续下拉选择/列宽/专用任务等物理流程未由本轮完整脚本证明 |

真实链路检查中的采样间隔、密度、文字大小、CPU模式使用产品中的下拉控件；进程状态筛选通过同一 `Theme::ComboBox` 接入，源码核对与共用组件检查覆盖其主题，物理展开检查未完成。长列表展示使用明确标注的技术组件选项，只检验原生滚动，不填造任务管理器业务列表/指标。

组件截图的弹层由真实 WGC 窗口图像按实际屏幕坐标合成，未拉伸或重绘。专项使用捕获完成应答保持弹层存活，避免截取已销毁的临时窗口。所有界面截图均为1920×1080；本机输入设备初始化分辨率2560×1440与测试窗口尺寸分别记录。

## 可复现命令

在 `E:\taskManager` 的 PowerShell 7 执行：

```powershell
dotnet build Takao.sln -c Release --no-restore
& scripts/import-msvc.ps1
cmake -S src/Takao.Qt -B output/qt-tests-build -G Ninja -DCMAKE_PREFIX_PATH=E:/taskManager/output/Qt -DCMAKE_BUILD_TYPE=Release -DTAKAO_BUILD_INTEGRATION_TESTS=ON
cmake --build output/qt-tests-build --target Takao.Qt Takao.Qt.Integration Takao.Qt.Fluent.Tests --parallel 4
Copy-Item output/qt-tests-build/Takao.Qt.exe output/Takao-win-x64/Takao.Qt.exe -Force
& 'C:\Users\jieok\.codex\skills\windows-computer-control\.venv\Scripts\python.exe' tests/qt-fluent-capture.py --release output/Takao-win-x64 --executable output/qt-tests-build/Takao.Qt.Fluent.Tests.exe --output output/fluent-component-audit/controls
& scripts/test-qt-components.ps1 -SkipBuild -OutputDirectory E:\taskManager\output\fluent-component-audit\after
python tests/qt-contrast.py --output output/fluent-component-audit/contrast.json
# 在已提升权限的 PowerShell 7 中执行正式生命周期检查：
& tests/qt-lifecycle.ps1 -OutputDirectory E:\taskManager\output\fluent-component-audit\lifecycle
# 本轮未通过的物理键鼠脚本，保留其失败证据：
& scripts/test-qt-desktop.ps1 -Extended -NativeFileDialogs -OutputDirectory E:\taskManager\output\fluent-component-audit\desktop-final
```

发布目录 `output/Takao-win-x64/Takao.Qt.exe` 已更新，最终 SHA256 为 `a772ac6672866cc630cac793772785f2b2d4e6784c9e20c443ab0bb00c3b102d`。源码/二进制散列及工作树状态在 `output/fluent-component-audit/provenance.json`。保留既有未提交工作与其他并行任务提交；不将其全部并入本轮修复。

本轮使用了工作树中既有的扩展验收脚本，其中部分仍未提交；已将实际脚本和 Qt 源码保存至 `output/fluent-component-audit/verification-sources/`。复现相同的80项及物理脚本范围需同时保留这些版本，不能仅凭修复提交假定检查数量一致。

## 边界

没有控制用户服务、写启动项/IFEO、安装更新或改动输入设备配置。结束操作仅针对测试创建并核对创建身份的专用进程。实际更新/服务控制成功、未真实诱发的权限/断连分支、全套物理键鼠仍不宣称通过。基线72项/20张截图只保存在 `before/` 作对照，不计入修复后的80项。
