# Fluent 全页调整与验收记录

日期：2026-11-04，Asia/Shanghai。八页调整已实现，以下区分实际通过、组件覆盖和受阻项；不宣称完整键鼠验收通过。

## 设计与实现

现行规范为 [FLUENT-DESIGN.md](FLUENT-DESIGN.md) 与 [MASTER](../design-system/takao/MASTER.md) 顶部映射。参考已核验固定提交的 Microsoft WinUI Gallery / microsoft-ui-xaml；来源、技能检索不匹配及桌面通用规则 fallback 均在规范中留档。Qt 不在 ui-ux-pro-max 的 stack 清单中，没有使用其它栈冒充 Qt 指导。

- 全局统一中性浅色分层、Windows 蓝色、系统字体、语义状态色及 SVG / 原生矢量图标。正文14px、页标题28px、分组20px、资源指标32px；内容边距24px，控件圆角4px、面板8px。
- 八页使用单一标题/命令栏、204px左导航、中性选中底和独立蓝色指示；普通表头40px，数据密度32/40px，主命令44px。
- 进程/历史/启动/用户/详细信息/服务统一表格与状态绘制；性能统一资源磁贴、蓝实线/灰虚线、设备说明和事实区；设置重组为五组限宽卡片，内容最大1080px、普通选择器240px。
- 共用弹窗统一字阶、间距、按钮与焦点恢复；危险确认保留默认取消、真实身份与后端完成反馈。资源磁贴高度随实际字体度量更新；选择器补齐明确的展开箭头。
- 技术栈保持 Qt 6.8.3 Widgets / C++17 + .NET 10.0.12，真实 Windows / CsWin32 / JSON.NET / 既有 HTTP 合同保持。采用 Fluent 视觉习惯，没有引入 WinUI 或 Web 渲染运行时。

阶段提交：规格 `6926b45`；八页实现 `19b2b95`；资源磁贴排版修复 `29d0723`；下拉箭头修复 `5f2a88c`。最终产品程序集 SourceCommit 为 `5f2a88c` 的完整 SHA，测试工具和本记录在后续独立提交中保存。

## 验证范围与命令

显示检查仅 **1920×1080 可见窗口物理像素，含原生标题栏，100% Qt 测试缩放**。窗口管理补偿 Windows 隐形边框，没有拉伸图像，也没有执行其它分辨率、125%、手机或暗色主题矩阵。

| 检查 | 实际结果 | 可复现命令 / 证据 |
|---|---|---|
| Release 与自包含发布 | 通过，.NET 0警告0错误；Qt成功链接 | `$env:TASKMANAGER_SOURCE_COMMIT = (git rev-parse 5f2a88c); ./scripts/publish-desktop.ps1 -SkipRestore` |
| 真实后端的原生组件回归 | **60项通过，退出码0** | `./scripts/test-qt-components.ps1 -OutputDirectory E:/taskManager/output/fluent-acceptance/components-release`；[native-results.json](../output/fluent-acceptance/components-release/native-results.json) |
| WGC图像 | 最终八页及相关状态共20张；每张1920×1080 | [captures.json](../output/fluent-acceptance/components-release/captures.json) |
| 正式生产进程生命周期 | **20项通过，退出码0** | 管理员运行 `./tests/qt-lifecycle.ps1 -OutputDirectory E:/taskManager/output/fluent-acceptance/lifecycle-release`；[results.json](../output/fluent-acceptance/lifecycle-release/results.json) |
| Fluent对比度 | 28组通过，包括16%热度合成背景 | `py -3 tests/qt-contrast.py --output output/fluent-acceptance/contrast.json`；[contrast.json](../output/fluent-acceptance/contrast.json) |
| 实际鼠标/键盘 | 受阻，不能计为交互通过；4项启动/真实来源前置检查完成 | `./scripts/test-qt-desktop.ps1 -OutputDirectory E:/taskManager/output/fluent-acceptance/hardware`；[results.json](../output/fluent-acceptance/hardware/results.json) |

组件驱动链接与生产 EXE 相同的 `Takao.Qt.Ui` 静态库，调用公开 Qt 控件方法，使用生产 Host、HTTP、JSON.NET、CsWin32 和真实 Windows 数据。覆盖八页、字体/表头/设置几何、滚动恢复、CPU模式、逐资源、采样间隔/暂停/冻结/密度、真实空搜索、程序错误保留、新建专用进程、身份选择、多个真实TCP/UDP端点、取消结束、真实结束与弹窗关闭。最终专用目标身份为 `28744:134382100349638084`（PID / 创建FILETIME），见14号确认图搜索框；取消保留目标，确认后真实退出并清理。**这些调用不发送操作系统输入，不能替代实际 Tab / 快捷键 / 鼠标验收。**

生命周期驱动独立使用生产 `Takao.Qt.exe`，验证管理员令牌、发布目录中的 coreclr / Qt Widgets、真实采样、专用进程、正常退出码0、端口释放，以及第二个专用 Host 崩溃时清理其自己的 Qt Job 子进程和精确自有 ETW 会话。没有结束用户其它程序或采集会话。

前期两个组件尝试分别在设置可见性、切页状态断言失败；已将切页检查改为等待真实标题及选中态就绪，最终结果使用独立 `components-release` 目录，早期尝试不计入通过数量。

## FUI标准逐项结论

| ID | 结论与边界 |
|---|---|
| FUI01 | 八页及设置底部1080P图像、单页头、导航/内容几何通过；表格保留内部滚动。 |
| FUI02 | 系统字体、14px正文/28px页标题、40px表头和统一Token检查通过；字号放大裁切按字体度量修复，本轮没有扩展显示缩放矩阵。 |
| FUI03 | 导航顺序、选中底/独立指示、可访问名称及焦点策略通过组件/图像检查；实际hover/press/收起键鼠路径未通过硬件验收。 |
| FUI04 | 主次命令/表格几何、实际40px密度切换、排序启用和数值角色通过组件/图像检查；逐列鼠标拖动与键盘调宽未执行。 |
| FUI05 | 28组实际Token及合成背景通过：正文对白17.404:1、说明对白6.385:1、说明对内容6.065:1、蓝主线5.674:1、灰次线6.124:1、焦点对选中底5.401:1。由QColor.darker生成的危险hover/pressed不在该字符串解析计数内，不把未计算项算通过。 |
| FUI06 | **实际键鼠受阻**；原生标签/焦点属性检查通过，未宣称物理Tab、Shift+F10、Esc、快捷键或拖动替代已通过。 |
| FUI07 | 真实暂停/冻结/空搜索、程序运行错误、HWiNFO缺失与更新HTTP404可读；其它权限、过期、断连、偏好保存失败等未真实诱发。 |
| FUI08 | 新任务、进程连接详情、结束确认的图像和真实请求路径通过；运行失败保留输入、busy禁重复、取消初始焦点、真实结束后关闭通过组件检查；其它弹层和菜单的物理输入路径未执行。 |
| FUI09 | Qt组件发起真实专用进程，身份核对、取消一次、确认结束一次、后端实际退出与成功提示通过；非物理输入E2E。 |
| FUI10 | CPU总体/逻辑处理器、磁盘/网卡/GPU及HWiNFO缺失状态图像通过；曲线使用真实样本，磁盘仅吞吐。HWiNFO成功链与CPU键盘切换未执行。 |
| FUI11 | 历史页本次会话/真实起点、表格和原始DTO值保留通过；清除统计物理交互未执行。 |
| FUI12 | 真实启动项名称/来源和已启用/禁用/无法确定三态图文通过；未更改用户启动项，右键/键盘菜单实际输入未执行。 |
| FUI13 | 真实会话、聚合值/数值层级和DTO保留通过；没有新增注销/断开空动作。 |
| FUI14 | 详细信息页真实字段/长路径及共享表格通过图像/组件检查；未逐一执行所有上下文操作。 |
| FUI15 | 真实SCM列表、状态和PID图像/DTO保留通过；未为验收控制用户服务，启停重启E2E未执行。 |
| FUI16 | 五组设置、1080px内容、240px选择器、纵向底部可达、真实采样间隔/密度/冻结通过；实际IFEO写入、更新安装、偏好保存失败未执行。 |
| FUI17 | 刷新/滚动/真实身份、暂停/恢复与冻结通过组件检查；无新增位置动画，未宣称实际键盘焦点稳定性或新性能基准通过。 |
| FUI18 | Qt/.NET构建、真实HTTP链路、生产生命周期通过；安全协议与身份保护保留并审查。旧API/ETW专项结果不计为本轮新增通过项。 |

所有20张最终图像均在本机输出目录。三张包含独立模态窗口的图像由真实父/子 WGC 帧按实际坐标组合，来源写入 captures.json；其余为直接窗口捕获。图像中的进程/速率/会话/错误均为当时实机结果。

## 受阻与未执行

本轮实际FYLHelper输入失败为 `InputPositionMismatch: expected (92, 142), observed (0, 3)`。遵循 [windows-computer-control/SKILL.md](C:/Users/jieok/.codex/skills/windows-computer-control/SKILL.md) 的“依赖缺失或设备失败时报告具体原因，按下述方式修复；不能偷偷更换后端。”，未改用SendInput、UIA Invoke或其它输入后端。设备检查/桥接加载成功不能当作实际输入成功。

本机无可用HWiNFO SM2，未安装或配置外部传感器来制造通过结果。真实更新读取返回HTTP404，按错误状态展示，未宣称下载/安装成功。未更改用户IFEO、启动项或服务；既有入口的实际冲突原样显示。本轮未重跑ETW完整网络专项，历史资源容量问题见 [QT-MIGRATION-RESULTS.md](QT-MIGRATION-RESULTS.md)。安装器只做静态提取/散列/manifest验证，没有执行安装、升级或卸载系统产品。

## 运行与发布

使用完整 [start.cmd](../output/Takao-win-x64/start.cmd) 或同目录 `Takao.Host.exe` 启动；需要标准UAC管理员授权。自包含目录包含.NET/ASP.NET 10.0.12、Qt6.8.3、Windows platform插件、VC CRT和许可，无需预装Node、Qt或.NET。

最终安装器与ZIP位于 `output/fluent-release-final`；本地构建未签名。可复现：

```powershell
./scripts/build-installer.ps1 -SkipBuild -OutputDirectory E:/taskManager/output/fluent-release-final -MakeNsisPath C:/nsis/makensis.exe
./scripts/verify-installer.ps1 -InstallerPath E:/taskManager/output/fluent-release-final/Takao-v1.1.1-win-x64-setup.exe -PayloadDirectory E:/taskManager/output/Takao-win-x64
```

最终散列见 [SHA256SUMS](../output/fluent-release-final/SHA256SUMS)。没有执行Git推送或远程发布；早先 `fluent-release` / `qt-release-final` 产物保留。
