# 导航与命令按钮定向验收

日期：2026-11-08（Asia/Shanghai）。规格提交`2feae99`，实现提交`846f3ef`。仅覆盖用户标注的左侧导航和右上页头命令栏，共享样式选择进程、应用历史记录、系统信息三个代表页面。

导航统一44px高、侧栏内等宽；页头命令统一120×44px，4px圆角、20px线性图标、浅灰底和深色文字。覆盖原蓝色运行按钮、红色结束文字及透明命令；禁用仍弱化并保留原因，蓝色焦点与导航选择标识保持。复制反馈、暂停/继续图标、长命令简短标签及完整可访问名称均复用现有动作。样式通过shell专用属性限定，设置卡片和弹窗按钮沿用原规则。

## 已运行检查

| 范围 | 实际结果与证据 |
|---|---|
| SB01 / 构建 | Qt6.8.3/MSVC Release生产和定向组件目标退出0；Python语法及`git diff --check`通过。C#及HTTP/采集器未修改，没有扩展后端或安装回归。 |
| SB01 / 原生组件 | [33项通过](evidence/shell-buttons/components.json)：六个进程命令实际均为120×44，有图标，实际渲染灰底、启用文字为深色；导航等高有图标。长pageAction和复制均同尺寸，复制写入真实硬件快照，反馈和复位边界不变。 |
| SB01 / 对比度 | [六组语义色计算](evidence/shell-buttons/contrast.json)通过，文字/图标最低11.397:1、焦点最低4.131:1；普通、悬停/选中、按下均达指定桌面标准。禁用文字使用既有弱化语义。 |
| SB02/SB03 / 实机 | [17项物理检查通过](evidence/shell-buttons/desktop.json)：FYLHelper真实鼠标导航、更多菜单打开/关闭、历史短命令可见、系统信息复制鼠标及Space触发；剪贴板哨兵确认真实写入。所有数据均来自生产Host，没有模拟行。 |
| SB03 / 视觉 | WGC原始1920×1080、96 DPI/100%截图：[进程](evidence/shell-buttons/processes-1080p.png)、[历史](evidence/shell-buttons/history-1080p.png)、[已复制与焦点](evidence/shell-buttons/copied-focus-1080p.png)。人工复核按钮灰底黑字、图标、圆角、对齐与焦点，无文字裁切或重叠。 |
| 精确清理 | 组件Host52164退出0，见[清理记录](evidence/shell-buttons/components-cleanup.json)；物理Host88224/Qt90176退出、端口9335关闭、Host退出0。未关闭用户原有Takao。 |

显示只测1920×1080；主屏实际2560×1440，没有修改显示模式。自动溢出及大字号仅代码审查：按可用空间依次把详情、暂停、刷新、页面专用命令等收纳到已有更多，菜单复用原图标、enabled与clicked槽，专用命令去重；空间恢复时恢复按钮，布局不保存到用户偏好。未把其它尺寸、字号放大、溢出菜单实际触发、暂停状态变化、刷新忙态或危险确认操作记作本轮现场通过。

## 可复现命令与运行目录

```powershell
. ./scripts/import-msvc.ps1
cmake --build output/system-information/qt-build --target Takao.Qt Takao.Qt.SystemInformation --parallel 4
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-system-information-components.ps1 -ButtonsOnly -ReleaseDirectory E:\taskManager\output\unified-buttons\release -OutputDirectory E:\taskManager\output\unified-buttons\components-final
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-system-information-desktop.ps1 -ButtonsOnly -ReleaseDirectory E:\taskManager\output\unified-buttons\release -OutputDirectory E:\taskManager\output\unified-buttons\desktop-verified
python -m py_compile tests/qt-system-information-desktop.py
git diff --check
```

`output/unified-buttons/release/start.cmd`启动本轮独立运行目录：复用上一轮未改动的真实Host/自包含运行库，替换本轮Qt EXE。[SHA256](evidence/shell-buttons/release-checksums.json)确认Qt与构建一致，没有更新用户安装或另制安装包。

物理首轮OCR把新省略号图标与“更多”文字合为同一识别项，导致精确匹配误报；WGC确认按钮完整可见。仅调整命令栏范围内的文字包含匹配，仍由实际识别坐标点击，随后整条窄流程重新通过。失败记录保留于`output/unified-buttons/desktop`，不计通过。最终进程截图使用真实“Takao”搜索，仅减少无关业务行，不替换生产数据。
