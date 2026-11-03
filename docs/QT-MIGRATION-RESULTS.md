# Qt + .NET 迁移验收记录

日期：2026-11-04（Asia/Shanghai）。技术迁移已完成；验收存在下述明确受阻项，**不宣称全部验收通过**。

## 交付与范围

- Qt 6.8.3 Widgets 原生界面 + .NET 10；Qt 使用 C++17，C# 继续 JSON.NET/CsWin32。沿用回环 HTTP、Origin、写令牌、进程创建身份、管理员入口与系统操作服务。
- 移除活动 Electron.NET/Electron、预加载桥、React/Tailwind 渲染源码、npm UI 依赖和浏览器/CDP 测试入口。历史文档及证据保留；旧依赖缓存归档到忽略的 `output/retired-frontend-dependencies`，不进入产品。
- 显示检查统一为 **1920×1080 可见窗口画面物理像素（含原生标题栏）、100% Qt 测试缩放**。不运行手机、多分辨率或125%矩阵。
- 运行目录：`E:/taskManager/output/Takao-win-x64/start.cmd`。最终 NSIS/ZIP：`E:/taskManager/output/qt-release-final/`。Qt/.NET/VC CRT 动态运行库、platform 插件及许可一并交付；本机生成的安装包未数字签名。
- 阶段提交：规格 `c051dca`；Host `fb94f20`；原生UI `50f98ab`；身份/数值/偏好修复 `2e29926`；关闭服务重复停止修复 `3423d4c`；Qt字段值拷贝与原生状态显示修复 `791a39d`。构建/测试/文档以本报告所在交付提交为准。

## 实际验证

| 检查 | 结果 | 可复现命令/证据 |
|---|---|---|
| .NET + Qt Release / 自包含发布 | 通过，.NET 0警告0错误，Qt链接成功 | `./scripts/publish-desktop.ps1 -SkipRestore` |
| 真实 Windows API | 79项通过 | 管理员运行 `node tests/native-api.mjs`；`output/qt-acceptance/native-api.txt` |
| 真实启动项及专用GUID记录 | 66项通过 | `dotnet run --project tests/Takao.StartupTests -c Release --no-build`；`output/qt-acceptance/startup.txt` |
| 磁盘独立真实对照 | 56项通过 | `dotnet run --project tests/Takao.DiskTests -c Release --no-build`；`output/qt-acceptance/disk.txt` |
| 原生Qt组件→HTTP→JSON.NET→CsWin32 | 46项通过 | `./scripts/test-qt-components.ps1`；`output/qt-acceptance/components/native-results.json` |
| 正式产品Qt生命周期 | 20项通过 | 管理员运行 `./tests/qt-lifecycle.ps1`；`output/qt-lifecycle/results.json` |
| 1080P图像 | 19张，全部1920×1080 | `output/qt-acceptance/components/captures.json` |
| 对比度 | 14组通过 | `python tests/qt-contrast.py`；`output/qt-acceptance/contrast.json` |
| NSIS独立解包/逐文件SHA256/manifest | 逐文件通过，数量见最终验证JSON | `./scripts/verify-installer.ps1 -InstallerPath <最终setup路径>`；最终产物应同时核对同目录 `SHA256SUMS` |
| 实际安装/升级/卸载、更新安装、IFEO更改 | 未执行 | 没有修改本机已有安装、用户服务或任务管理器入口；CI已改为Qt生命周期，但未在本轮远程运行 |

已逐页核对真实字段；修复了同一QJsonObject中QJsonValueRef在插入字段后失效的问题，历史CPU/时间、启动位置、用户及服务显示字段均与原始DTO逐行对照。

原生组件测试将与生产 EXE 相同的 `Takao.Qt.Ui` 静态库链接进独立测试驱动，调用公开 Qt 组件方法，Host/采集器/API保持生产实现。验证八页、可访问名称及焦点能力、滚动恢复、CPU模式、真实资源、暂停/恢复/间隔/冻结/密度、真实空搜索、无效程序错误恢复、新建专用进程、创建身份选中、全部真实 TCP/UDP 端点、取消终止、确认真实退出、弹窗关闭和未知/小数值。它不发送 Windows 鼠标键盘事件，**不计作实际键鼠E2E通过**。

WGC保存八页及资源、暂停、任务弹窗。父窗口WGC捕获不包含独立原生弹窗，因此三张弹窗图由真实WGC父/子窗口帧按实际窗口坐标组合，来源写入 `captures.json`；其余为含原生标题栏的窗口直接捕获。未用静态业务行、拦截API或虚构采样补图。

正式生命周期检查独立运行原始 `Takao.Qt.exe`：验证真实提升令牌、从发布目录加载 coreclr/Qt Widgets、Qt PID保护、真实采样、新建/结束专用进程、正常退出码0和端口释放；另强制结束第二个专用Host，验证其Job Object清理Qt。测试仅清理带本次Host PID+创建FILETIME前缀的ETW会话，未停止其它采集器。

## ui-ux-pro-max 审查

依据用户指定的 [SKILL.md](C:/Users/jieok/.skills-manager/skills/ui-ux-pro-max/SKILL.md)、quick-reference及pro-rules桌面适用范围，查询与取舍见 [迁移规格](QT-MIGRATION.md)。Qt无该技能专用stack，不借用WPF结果，也不执行其手机/暗色主题检查。

| 项目 | 实际结论 |
|---|---|
| 层级/一致性 | 八页Windows蓝色侧栏、单一页头/命令栏；原生数据面与统一SVG图标，标题突出，无页面级横向溢出。表格和资源内部滚动按数据需要保留。 |
| 文字/状态 | 说明文字对白底6.385:1，正文17.404:1；未知显示破折号并有原因，启动状态有文字，数值保留单位，历史限定本次会话。 |
| 焦点/图表 | 焦点对白底6.308:1、对选中底5.401:1；蓝主线5.674:1、灰次线6.124:1，满足3:1。 |
| 交互/反馈 | 导航/主按钮44px；原生控件可访问名称和焦点策略已检查；模态输入标签、错误保留、忙态、取消默认焦点及真实完成反馈通过组件检查。 |
| 性能/动态 | Qt model/view仅绘制可见行；快照串行/ETag304、图标有界缓存；图表只用实际时间样本且缺口保留；没有装饰性循环/位移动画。未虚报性能基准。 |
| 实际键鼠操作 | 受设备故障阻塞，见下文；不能将组件方法调用或焦点属性检查代替实际Tab/快捷键/鼠标验收。 |

## 受阻与未执行边界

1. **实际键鼠E2E未通过**：`./scripts/test-qt-desktop.ps1` 在FYLHelper输入出现 `InputPositionMismatch: expected (97, 149), observed (0, 0)`，组合键未触发预期切页。WGC和桥接可加载不代表输入设备可用。遵循 [windows-computer-control/SKILL.md](C:/Users/jieok/.codex/skills/windows-computer-control/SKILL.md) 的“依赖缺失或设备失败时报告具体原因，按下述方式修复；不能偷偷更换后端。”，没有换用SendInput、UIA Invoke或浏览器自动化。物理Tab、快捷键、鼠标拖动、系统文件选择/CSV保存等交互没有被计为通过。
2. **ETW完整网络专项未通过**：管理员运行 `tests/Takao.NetworkTests` 返回 `capacity / native1450`（系统ETW资源不足），原普通权限尝试为1300。记录在 `output/qt-acceptance/network.txt`、`native-results.json`。没有清理其它会话以掩盖问题，UI保留真实不可用原因，网卡性能与TCP/UDP端点仍使用各自真实来源。
3. 当前机器无可用HWiNFO SM2，图像验证了明确缺失状态；未宣称实际HWiNFO传感器完整成功。没有控制用户服务、改变启动项、写IFEO或实际安装/更新系统产品来增加通过数量。

Q01/Q04/Q06/Q09及构建发布部分通过；Q02/Q03/Q08对应上述已执行链路通过；Q05/Q07的实际键鼠部分受阻；Q10仅发布/包验证通过，安装生命周期未执行。测试结果、截图和命令均保留在本机输出目录，历史Electron通过数不计入本轮。
