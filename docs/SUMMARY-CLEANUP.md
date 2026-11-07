# 顶部资源汇总与提示清理

日期：2026-11-07。用户要求“CPU，内存，进程数量，显眼一些；占位的所有提示删除”。参考图分别标出顶部淡灰汇总与占据整行的成功通知。本增量优先于历史成功 toast / InfoBar 必须可见的要求；只调整既有 Qt 界面，不新增采集能力。

同日用户补充：仅测试本次调整部分，不每次全量测试。下列检查按变更选择执行，代表页面足以覆盖共享顶栏；未改动的后端API、采集器、安装与完整桌面E2E不属于本次默认回归范围。

## 行为与视觉

- 顶部 CPU、内存、进程数量拆成三组无边框的两行文字。标签为 caption 12px/secondary；值为新增 `SummaryMetric=24px`、600、accent，数字采用现有 numericFont。CPU/内存保留百分号，进程标签明确“进程”，值为真实全机进程数。三组预留稳定宽度，更新不推移搜索或其它指标，不画大卡片或增加顶栏高度。
- 数据继续来自当前展示快照的 `system.cpuPercent`、`system.memoryPercent`、`system.processCount`。筛选不能改变顶部全机数量；暂停/冻结显示最后真实值。未取得、预热或缺失使用“—”及可访问原因，不把 null/缺字段转换成 0。
- 移除内容布局内全部 `toastArea_`、toast、banner 及“关闭提示”控件，不留隐藏占位行、额外间距或新增浮动成功提示。正常完成、已复制、已打开、已定位等成功消息不再弹条；依靠真实列表、选中、设置状态和对话框完成变化反映结果。安全确认仍按原合同先确认再写入，结束任务成功仍须确认真实退出后自动关闭确认框。
- 连接错误、来源读取/空结果、暂停、冻结等实际状态合并到已有页头次级文字位置，不新建内容行。显示短状态，完整原因通过同一控件的可访问描述/tooltip及既有“来源与采样状态”入口读取。断连保持最后快照并明确过期，禁用危险动作；恢复后清除旧错误。暂停、冻结、断连优先于空搜索说明，不能被刷新或搜索覆盖。
- 操作失败仍可读：优先沿用现有操作对话框字段错误；无操作对话框时通过已有页头次级文字保留短错误及完整可访问原因，必要时复用有界、可关闭的 Qt 非阻塞错误框。不占主内容布局，不因后台轮询反复弹框、不堆积重复错误。保留原生键盘焦点、Esc和关闭后焦点恢复。
- 搜索输入框内部的 placeholder、字段标签、必需说明、未知字段原因与安全确认不属于截图所指额外占位条，保留。性能页真实来源/能力状态不伪装成正常数据。

## 成熟来源与技能依据

复用 [既有开源研究](RESEARCH.md) 已核验的 [TaskExplorer ProcessView.cpp](https://github.com/DavidXanatos/TaskExplorer/blob/9f3f95d22f07f759dd1e0114417529ba51decb01/TaskExplorer/GUI/TaskInfo/ProcessView.cpp) 的 Qt model/view 与身份保持方式，以及 [WinUI Gallery TypographyPage.xaml](https://github.com/microsoft/WinUI-Gallery/blob/7360ac322414e118eadfb0c7c2f4ecc31a218f59/WinUIGallery/Samples/Typography/TypographyPage.xaml) 的平台字阶思路；本轮仅复用研究记录，不宣称重新在线核验。界面采用 Qt 公共 QLabel/QLayout/QMessageBox/字体 API，继续既有 HTTP/JSON.NET/CsWin32，不新增依赖、驱动或私有协议。

已阅读用户指定 `C:\Users\jieok\.skills-manager\skills\ui-ux-pro-max\SKILL.md`、quick-reference 桌面规则及 pro-rules 的范围声明。定向查询 `font size scale --domain ux -n 1` 命中 Typography / Font Size Scale / All，采用12/24字阶、固定数值布局和清晰标签。Qt不在支持栈中，不冒称Qt栈匹配；正文/说明与数值对比≥4.5:1，焦点≥3:1，不增加位移动画、远程字体、手机或暗色验收。

## 实现落点与验收

`Theme.h` 集中新增汇总字号与宽度 Token；`MainWindow.h/.cpp` 替换单一 summary QLabel、删除 toast timer/布局，统一已有页头次级文案状态优先级与失败呈现。`Actions.cpp`、`Settings.cpp` 沿用真实完成/失败路径，不能为省略通知绕过后端确认或吞掉失败。

| ID | 必须验证 |
|---|---|
| SC01 | Qt Release构建；三组汇总来自生产真实 Windows 快照，当前单位及全机数量可读，缺失值不归零；无后端改动不重复要求.NET/API全量检查。 |
| SC02 | 1920×1080/100%显示环境检查必要代表页面；顶部56px、三组12/24字阶、蓝色600数值、无裁切/重叠，搜索可用，不要求八页全遍历。 |
| SC03 | 选择一个真实成功操作，确认没有成功通知、“关闭提示”或内容位移；若验证结束任务，只使用专用进程并核对真实退出。 |
| SC04 | 暂停/恢复、冻结/恢复、空搜索与真实断连/恢复可辨别，页头状态不被覆盖，表格几何不因状态变化移动。 |
| SC05 | 实际无效路径等失败保留可读错误；确认框和任何实际出现的错误框关闭后恢复焦点；后台错误不重复弹窗。未知值、受保护身份和取消不写仍满足原合同。 |
| SC06 | 保存当前SHA、命令/退出码、截图路径、对比度及真实通过/失败/未执行；显示测试仅1080P。仅清理测试创建的专用进程。 |

已有 `tests/qt-desktop-e2e.py` 与 `tests/qt-ui-integration-desktop.py` 的“冻结 banner”文字断言须改为页头状态合同；不要为保留旧断言重建通知条。Qt集成/接线测试须继续验证真实安全进程退出、暂停、搜索和焦点，成功反馈断言改为真实UI变化。沿用 [ACCEPTANCE.md](ACCEPTANCE.md) 现行Q系列适用回归，不把历史运行结果记成本轮通过。
