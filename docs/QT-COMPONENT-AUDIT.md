# 当前 Qt 组件提取与 Fluent 一致性审查

日期：2026-11-04。对象：当前工作树 `src/Takao.Qt`，Qt 6.8.3 Widgets / C++17；.NET 10、CsWin32、JSON.NET 与既有 HTTP API 不变。

这是本次源码审查与修复验收清单；源码结论不等同于真实桌面交互通过。显示验收环境仅 1920×1080 / 100%，正常启动仍使用现有 1100×720 默认窗口。运行结果、截图和命令由验收阶段另记。

## 1. 提取方法与实际组件

检查全部 `.cpp/.h` 的创建语句、静态对话框工厂、基类、绘制委托与全局 QSS；不把仅有 `#include` 的类型计作正在使用的界面。例如 `QInputDialog`、`QDialogButtonBox` 虽在 Actions.cpp 中包含，但没有实例或静态工厂调用；`QTextEdit` 仅有主题规则，没有当前产品实例。实例数量会随列数、资源数和对话框变化，以下列出实际组件类型与用途，不将源码创建位置数冒充运行实例数。

| Qt 组件 / 项目封装 | 实际入口 | 当前用途及主题情况 |
|---|---|---|
| `QApplication`、`QMainWindow` / `MainWindow` | main.cpp；MainWindow.cpp | 原生窗口与全局 Palette/QSS；保留 Windows 系统标题栏 |
| `QWidget`、`QFrame` | MainWindow.cpp；Settings.cpp | 内容面、页面、InfoBar 容器、设置分组和 SettingsCard；已有中性表面/4px 与 8px 圆角 |
| `QLabel` | MainWindow.cpp、Actions.cpp、Settings.cpp | 页面/资源标题、说明、状态、错误、设置项；Theme::type 统一字阶 |
| `QPushButton` / `Theme::NavigationButton` | MainWindow::button/buildUi；Theme.h | 命令、主次动作、八页导航；导航透明中性底、独立蓝色指示与焦点 |
| `QLineEdit` | buildUi；runTask | 共享搜索、程序路径、参数；已有边界、选中文字、焦点与禁用主题 |
| `QComboBox` / `Theme::ComboBox` | buildUi；Settings.cpp；filters | 性能模式、采样间隔、密度、文字大小、进程状态筛选；封装下拉箭头与原生弹出生命周期 |
| `QListView` / `Theme::ComboItemDelegate` | Theme::ComboBox | 下拉列表；已有圆角、hover/selected/focus、32px 条目和单一滚动条 |
| `QCheckBox` | Settings.cpp；columnMenu；filters | 冻结、置顶、显示列、当前账户筛选；文本与焦点已有主题，indicator 缺少主题绘制 |
| `QSpinBox` | columnMenu；filters | 列宽数值与会话 ID；输入面已有主题，步进按钮/箭头缺少主题绘制 |
| `QTreeView`、`QHeaderView` / `RowDelegate` | MainWindow::createTable | 业务列表、HWiNFO、详情连接表；model/view、有界绘制、数值对齐、选择/热度/状态与焦点；排序/树箭头仍依赖底层 style |
| `QAbstractItemModel` / `DataModel`、`QSortFilterProxyModel` / `DataProxy` | DataModel.h/.cpp | 数据模型、原始值排序、筛选、进程树；属于数据/视图基础，不是可单独截图的控件 |
| `QListWidget`、`QListWidgetItem` / `ResourceDelegate` | buildPerformance | 性能资源栏；集中表面、蓝色选择指示、实采样小曲线与焦点 |
| `QStackedWidget` | buildUi；buildPerformance | 八页和性能图/传感器切换，不额外绘制独立视觉 |
| `QScrollArea`、`QScrollBar` | 设置、性能、显示列对话框及视图 | 内部滚动；已有细轨道/圆角滑块，保持命令区可见 |
| `QMenu`、`QAction` | contextMenu；moreMenu | 右键、更多、优先级子菜单及 checkable 项；文字/分隔/hover 已统一，check/子菜单箭头待补齐 |
| `QShortcut` | buildUi | 导航和键盘等效入口；不单独绘制 |
| `QDialog` / `ActionDialog`、`IntegrationDialog` | Actions.cpp；Settings.cpp | 新任务、结束、详情、列设置、筛选、首次接管、系统接管；已有 Theme::dialogLayout 标题、间距、焦点恢复；busy 期间禁止 Esc 误关沿用 |
| `QMessageBox` | MainWindow.cpp、Actions.cpp、Settings.cpp、main.cpp | 七处业务确认与一处启动错误；没有接入共用对话框布局，见 A03 |
| `QPlainTextEdit` | 进程树确认、presentText、进程详情 | 只读详情及可复制文本；已有统一表面、文本、焦点和滚动 |
| `QTabWidget` / 内部 `QTabBar` | details | 进程信息/连接页签；已用中性表面、蓝色下划线、hover/focus |
| `QFileDialog` 原生工厂 | runTask；exportCsv | 选择 exe、保存 CSV；使用 Windows 系统对话框，属于有意保留的原生外观 |
| `QToolTip` | Qt 工具提示 | 使用全局主题；不是手工构造的持久页面 |
| `Chart : QWidget` | Chart.h/.cpp；buildPerformance | CPU、内存、磁盘/网卡/GPU 图；白底、浅网格、蓝实线/灰虚线、真实时间缺口 |
| `QVBoxLayout`、`QHBoxLayout`、`QGridLayout`、`QFormLayout` | 三个界面源文件 | 布局对象，不是视觉控件；继续使用现有 4/8px 节奏 |

`Theme::icon` 的 SVG、`Theme::paintStatus`、`RowDelegate`、`ResourceDelegate` 和 `Chart` 是项目自绘内容；它们使用相同 Token。导航 SVG 的统一轮廓与线宽本身符合当前规范，不能仅因不是某个官方图标包就认定缺陷；本次无需引入新图标依赖或替换真实应用图标。

## 2. 可证实的不一致

以下位置为审查时源码行号，后续改动以符号为准。

| ID / 优先级 | 证据与影响 | 修复要求 |
|---|---|---|
| A01 / P1 | Theme.h 的 `apply` 选择 `Fusion`；`QCheckBox::indicator` 仅配置 18×18 尺寸，没有填充/边界/勾选形状。设置、列设置、筛选继续使用 Fusion 勾选原语，而其他控件为 Fluent 中性/蓝色层次 | 统一 unchecked/checked/partial/hover/pressed/disabled 的边界与填充，checked 用 accent+白色勾线；保留原生 QCheckBox 的 Tab、Space、三态、禁用与辅助功能语义。可通过集中 `QProxyStyle` 绘制原语，避免业务页自绘交互 |
| A02 / P1 | `columnMenu`（Actions.cpp 840）与 `filters`（884）使用原生 QSpinBox；Theme.h 只给输入面 QSS，没有 up/down-button/arrow。小型 Fusion 步进按钮与已统一的 ComboBox 不一致 | 主题集中定义中性步进区域、hover/pressed/disabled 和轮廓箭头；使用原生 subControlRect / hitTest 及 QSpinBox 数值行为，保留上下键、编辑、边界与加速步进 |
| A03 / P1 | 七处静态 `QMessageBox::question`：MainWindow.cpp 418，Actions.cpp 492/515/542/770，Settings.cpp 243/267；仅靠 QSS 改背景/按钮 min-height，无法得到现有 Theme::dialogLayout 的 20px 标题、24px 内容间距、44px 命令与焦点恢复；全局没有加载 Qt 中文翻译，标准 Yes/Cancel 不能保证中文。涉及清除历史、优先级、效率模式、服务控制、更新、重置 | 统一接入 Fluent 确认构造器，使用明确中文动作+取消、现有标题/正文 Token、44px 动作、取消初始焦点、Esc 与关闭恢复发起焦点。请求/API/身份/busy 合同不变；不让纯外观修复自动执行更新、重置或服务操作 |
| A04 / P2 | main.cpp 31 使用静态 `QMessageBox::critical`，启动错误又回到默认错误图标/按钮/布局 | 同一主题错误展示入口，中文关闭按钮、同一标题/间距；保持非法启动返回码 2 和本地启动引导 |
| A05 / P2 | QMenu 的 checkable 项及 submenu 指示、QHeaderView 排序箭头、QTreeView 的展开分支没有主题覆盖；整体菜单/表格已主题化但局部还会回退 Fusion | 在集中 style 中统一箭头/check 原语，明确方向、选中/禁用颜色；保持菜单键盘/勾选与表格排序/展开语义。实际修复效果以组件截图和键鼠结果确认 |

判断边界：没有发现把整个界面重写为 WinUI、移除 Fusion 基础 style 或引入第三方 Qt 控件框架的必要。项目现有 Palette/QSS/model-view 架构可以覆盖这些遗漏，不新增业务/API/协议。面板的 1px 轻分隔不承担状态含义，不应为了 3:1 把所有轻描边加深成重灰框。

## 3. 应保留的 Fluent 实现

- Segoe UI Variable Text / Segoe UI / 中文系统回退，12/14/16/20/28/32px 字阶；数值单独使用本地等宽字体。
- 中性 app/content/surface 分层、Windows 蓝主色、4px 控件/8px 面板、透明导航与独立选择指示。
- 单页一个命令栏、主要动作 44px、次要动作中性，表格 32/40px 密度、40px 表头、有界 model/view 绘制。
- 现有 Theme::ComboBox 的原生生命周期、单一滚动条、键盘选择，以及现有对话框 busy 关闭保护。
- 五组限宽 SettingsCard，路径可选/换行、真实偏好/接管/更新状态；白底蓝实线/灰虚线真实图表。
- unknown/null/预热/权限/断连原因与真实链路；Windows 原生文件选择器和系统窗口装饰。

## 4. 本次实际核验的成熟来源

本阶段通过 GitHub API/原始文件实际读取下列固定提交，既有 FLUENT-DESIGN 的来源继续有效；不复制整个框架，不引入专有协议。

| 来源 | 实际观察与采用边界 |
|---|---|
| [WinUI CheckBox，b229fbd…](https://github.com/microsoft/microsoft-ui-xaml/blob/b229fbd15ceb090a2952985955805650e41390d8/controls/dev/CommonStyles/CheckBox_themeresources.xaml) | 41～44 未勾选边界分态；57～60 checked 使用 AccentFill 及 hover/pressed/disabled；69～76 勾选形状使用 TextOnAccentFill。采用语义与状态，不硬搬 XAML |
| [WinUI ContentDialog，同一提交](https://github.com/microsoft/microsoft-ui-xaml/blob/b229fbd15ceb090a2952985955805650e41390d8/controls/dev/CommonStyles/ContentDialog_themeresources.xaml) | 16：按钮间隔8；18：padding24；66：OverlayCornerRadius。统一现有 Qt 对话框构造器，保留原生模态和取消路径 |
| [WinUI NumberBox，同一提交](https://github.com/microsoft/microsoft-ui-xaml/blob/b229fbd15ceb090a2952985955805650e41390d8/controls/dev/NumberBox/NumberBox_themeresources.xaml) | 步进按钮独立中性表面/边界角色。参考按钮主题分离，不替换现有数值控制行为 |
| [PyQt-Fluent-Widgets CheckBox，3c8299ac…](https://github.com/zhiyiYo/PyQt-Fluent-Widgets/blob/3c8299acf530a066bf7deb04529e35ba0c88cb9e/qfluentwidgets/components/widgets/check_box.py) | `CheckBox(QCheckBox)` 保留原生类型语义并绘制圆角状态；本项目只借鉴原生语义与视觉分离，不增加 Python/PyQt 运行时 |
| [同项目 SpinBox](https://github.com/zhiyiYo/PyQt-Fluent-Widgets/blob/3c8299acf530a066bf7deb04529e35ba0c88cb9e/qfluentwidgets/components/widgets/spin_box.py) | QSpinBox 保留编辑/数值控制，SpinButton 独立设置尺寸与主题；采用集中绘制与原生 hit area 的组织方式，不移植实现 |
| [同项目 MessageBoxBase](https://github.com/zhiyiYo/PyQt-Fluent-Widgets/blob/3c8299acf530a066bf7deb04529e35ba0c88cb9e/qfluentwidgets/components/dialog_box/message_box_base.py) | 共用正文容器、24px 内容边距和 yes/cancel 动作；源码默认焦点为 yes，本项目危险动作按现有安全合同使用取消初始焦点。借鉴共用入口，产品按钮使用明确中文动作 |

本次再次运行指定技能 `keyboard focus modal --domain ux`：命中 Focus States、Focus Not Obscured（Enhanced/Minimum）。使用其桌面适用的可见焦点、键盘闭环、对比度与稳定几何规则。Qt 不在其 stack 清单中，不冒称 Qt 栈命中；不追加手机、多分辨率或暗色主题测试。

## 5. 修复验收清单

| 检查 | 需要证据 |
|---|---|
| 组件提取完整性 | 对照本表覆盖页面、菜单、列设置、筛选、业务确认、启动错误，不能只截图八个主页面 |
| 复选框 | 同一组件场景验证 unchecked/checked/partial/disabled/hover/pressed；Space/Tab/禁用不操作；焦点可见且不改变几何 |
| 数值框 | 列宽/会话数值实际可编辑，步进按钮鼠标与上下键可用，最小/最大处禁用状态清晰；不改变实际范围与单位 |
| 菜单与表格 | 更多菜单的 checkable 项、优先级 submenu、排序箭头、树展开箭头同族；真实键盘/鼠标仍可操作 |
| 共用确认 | 七处业务入口与错误入口均走主题；标题、正文、动作中文与层级一致；取消/Esc 不写、焦点恢复；危险操作确认逻辑不减弱 |
| 样式回归 | 1920×1080 显示环境下八页与关键组件截图；正文≥4.5:1、焦点/信息图标/checked 形状≥3:1，记录实际背景与结果 |
| 构建/真实链路 | .NET/Qt Release 与现有组件/真实桌面检查；实采样、未知状态、身份保护、专用测试进程启动/取消/结束与生命周期继续成立 |

测试只能操作本次创建并核对身份的专用进程。不为获得“通过”改变用户启动项、接管配置、控制用户服务或安装更新；无法安全执行的分支明确记为未执行。阶段1只完成提取、源码/成熟实现核验及修复准则，不宣称上述交互已通过。
