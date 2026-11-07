# 系统信息最终定向验收

日期：2026-11-07（Asia/Shanghai）。规格提交 `30f1ee8`，原生采集 `37bcc3f`，Qt页面 `3235a30`，显卡物理身份修复 `c5106eb`。最终实拍与物理交互使用 `c5106eb` 对应构建；本次只检查新增系统信息、移除HWiNFO后的性能页及设置入口兼容。

## 交付行为

左侧“服务”之后增加“系统信息”，底部设置位置保持。新页按处理器、显卡、主板、内存、硬盘、显示器、系统的顺序展示本机真实信息，逐条内存、逐块物理磁盘及显示器独立呈现；支持共享搜索、文字空态、Esc清空及F5/按钮刷新。型号可选择和换行，内容可纵向滚动，使用现有Windows蓝色和浅色Fluent Token。

性能页的HWiNFO磁贴、传感器表格/空态、共享内存配置说明及Qt请求已删除；旧HWiNFO选中偏好回退并保存为CPU。CPU总体/逻辑处理器模式及原设置技术身份保持。后台旧HWiNFO只读API保留兼容，新硬件概览不依赖HWiNFO运行。

硬件通过.NET/JSON.NET/CsWin32与标准HTTP接入：SMBIOS、DXGI、存储IOCTL、显示模式/EDID、系统注册表。硬件缓存与实时采样独立；未知值保留原因，不归零。显卡按确认的单节点物理PnP实例合并逻辑别名，既不重复显示本机同一显卡，也不按型号合并不同设备。工艺、显存颗粒、芯片组及内存时序没有本轮可靠通用来源，未填充推算或参考图数据。现有HWiNFO SM2主要是传感器读数，不能替代完整硬件资料接口。

## 已完成的相关检查

| 范围 | 实际结果 | 证据 |
|---|---|---|
| SI01 构建与技术边界 | Host/.NET10与Qt6.8.3 Release成功；当前发布Host DLL和Qt EXE与最新对应构建SHA256一致 | [原生报告](SYSTEM-INFORMATION-NATIVE-RESULTS.md)、[Qt报告](SYSTEM-INFORMATION-QT-RESULTS.md)、[显卡报告](SYSTEM-INFORMATION-GRAPHICS-RESULTS.md)、`output/system-information/release-checksums.json` |
| SI02 真实七类来源 | CPU/主板/内存条/五盘/显示模式/Windows版本与独立只读Windows查询对照；固件边界10项、生产HTTP21项通过 | `output/system-information/stage2/{firmware-tests.json,api-results.json}` |
| SI03 缓存、刷新及实时采样 | 60秒缓存、8个重叠刷新合并、采集时刻前进、实时snapshot继续推进；物理F5也推进真实硬件时刻 | 同上及下列最终物理记录 |
| SI04 新页1080P显示与搜索 | 七类真实事实、参考顺序、服务下方入口、可选长型号、搜索空态、滚动/恢复与刷新均通过；实拍视觉复核无重叠、裁切或页面水平溢出 | [系统信息实拍](evidence/system-information/overview-1080p.png)、`output/system-information/desktop-final/02-system-information-empty-1080p.png` |
| SI05 HWiNFO移除及兼容 | 原生组件确认整个资源列表无HWiNFO、旧偏好持久回退CPU、CPU两模式可选、设置身份保持；最终实际鼠标访问性能与设置正常 | [性能页实拍](evidence/system-information/performance-1080p.png)、`output/system-information/stage3/components/results.json`（60项） |
| SI06 物理交互与清理 | FYLHelper真实鼠标/键盘，Ctrl+F、Esc、搜索、滚动、F5和导航；22项通过，退出0，五张WGC图像；本次Host/Qt及端口均已释放 | [最终物理记录](evidence/system-information/desktop-results.json) |
| 显卡重复修复 | 四个DXGI逻辑LUID均指向同一PCI实例，最终一条NVIDIA物理卡及一条明确的软件适配器；独立CIM对照7项通过 | `output/system-information/graphics/results.json` |
| 新页颜色/焦点 | 正文/内容16.531:1，说明/内容6.065:1，蓝色/侧栏5.113:1，焦点/选中5.401:1，均达到桌面规则 | [四项定向对比度](evidence/system-information/contrast.json) |

显示验收画面均为1920×1080可见窗口物理像素、96 DPI/100%缩放，没有缩放图片。测试主屏实际2560×1440，仅调整本次专用窗口到1080P，没有改变用户显示设置；不执行其它测试画面尺寸、暗色或手机矩阵。型号和数值均来自本次生产Host，没有模拟业务行。截图采用WGC，实际鼠标/键盘仅通过FYLHelper，输入会话先初始化真实主屏分辨率。

最终测试Host `106392`、Qt `104544`、回环端口 `6304` 均已退出/释放，Host退出码0。所有测试使用专用偏好/端口，只关闭本次拥有的进程；未关闭用户原有Takao实例、修改启动配置或控制用户业务服务。独立运行目录为 `E:\taskManager\output\system-information\release`，没有覆盖用户现有安装或另发安装包。

## 可复现命令

```powershell
dotnet build src/Takao.Host/Takao.Host.csproj -c Release --no-restore
dotnet run --project tests/Takao.SystemInformationTests/Takao.SystemInformationTests.csproj -c Release -- output/system-information/stage2/firmware-tests.json
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-system-information.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-system-information-graphics.ps1
# Qt/组件的构建与独立运行目录组装方法见上述分阶段报告。
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-system-information-components.ps1
# 最终实际鼠标/键盘与WGC定向流程：
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-system-information-desktop.ps1 -OutputDirectory E:\taskManager\output\system-information\desktop-final
```

物理脚本在需要时使用项目既有RunAs隐藏worker取得真实管理员令牌，不绕过Host检查。默认使用独立`output/system-information/release`；另一个完整构建目录可通过`-ReleaseDirectory`明确指定。测试的生产启动与硬件HTTP共用同一Host。

## 测试修正及实际边界

物理脚本首轮全帧OCR缩小后漏识“显卡”，改为对原始像素类别区域识别；硬件较长时显示器和系统在滚动区域下方，不能以首屏缺字判断业务数据缺失。一个清理分支把无文本剪贴板当成字符串，曾掩盖原始失败，已修复并先保存失败记录。随后发现异步窗口偏好可在测试调整尺寸之后恢复1100×720，改为等待真实窗口状态读回并启用已有1080P测试开关后再定位。失败运行未计为通过，保留在`output/system-information/desktop*`各次目录。

当前机器的Alt数字快捷键在物理流程中未稳定观察到切页；最终页面切换使用实际鼠标，Ctrl+F/Esc/F5使用实际键盘。Ctrl+End未观察到滚动，本轮滚动通过实际鼠标滚轮确认，不能据此宣称该组合键已验收。原生组件的公共控件检查不代替实际输入；最终记录也没有把未观察到的快捷键写成通过。

未人为诱发热插拔、真实权限拒绝、原生调用超时、固件缺失、双同型号GPU或多节点聚合GPU，相关现场分支不计作已运行。不同PnP实例不合并及多节点/失败保留逻辑来源已做实现审查。CPU模式及部分偏好通过原生控件检查，实际物理流程只覆盖报告列出的行为。不重跑未改动的进程操作、全部服务/采集器、安装/更新或完整页面矩阵。
