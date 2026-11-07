# 顶部汇总与提示清理验收

日期：2026-11-07（Asia/Shanghai）。需求/Token提交 `db8b67a`，业务实现提交 `d3f226df5fca30d42de5591da1f7f8eec237a32c`。范围见 [SUMMARY-CLEANUP.md](SUMMARY-CLEANUP.md)。本轮没有修改后端采集、协议、权限或进程身份保护。

顶部 CPU、内存、进程采用12px标签及24px/600的蓝色等宽数字，100%下顶栏仍56px。内容布局内的通知条、提示关闭按钮及通知计时器已删除；成功结果通过真实列表、设置及对话框变化体现。连接、暂停、冻结、空结果和失败显示于已有页头次级位置，完整原因可通过可访问描述及悬浮提示读取。

## 本轮实际检查

用户最新要求：**只测试本次调整部分，不再每次执行全量测试**。已停止后续完整桌面/Extended/E2E重跑，撤销本阶段为全量流程增加的导航焦点及逐页OCR回退代码，仅保留三项汇总、布局、暂停页头的实际来源检查、证据文件有界重试，以及冻结页头的必要OCR区域更新。以下证据路径相对 `E:\taskManager`，退出码独立保存，不累计失败尝试或旧结果。

本次变更直接相关的结果：三组指标在真实暂停样本下与生产 `system` 逐项一致，24px/600/蓝色、可访问名称及56px顶栏通过；搜索不改变全机进程数；暂停与空搜索状态优先级通过；成功新建/结束后无通知布局位移；29对对比度通过。冻结状态检查只针对已经保存的真实物理画面做一次定向OCR，没有重新启动桌面测试，更新后页头区域成功识别“显示已冻结·后台继续采集”，见 `caption-focused-check.json`。没有据此宣称完整物理冻结/恢复流程通过。

最新范围要求到达前已经执行的额外流程如实留档，后续不作为局部修改的默认测试集：

| 检查 | 实际结果 | 证据 |
|---|---|---|
| 正式自包含发布 | 退出0；Qt6.8.3、SDK10.0.403、.NET/ASP.NET10.0.12、MSVC19.52.37025.0 | `output/summary-cleanup/stage3/publish.log`、`publish.exit`、`toolchain.json` |
| 发布依赖与许可 | 必需Qt插件、CRT、自包含.NET、图标及许可证均存在，无Electron载荷；产物SHA256留档 | `output/summary-cleanup/stage3/release-manifest.json` |
| 真实后端API | PASS79，退出0；实际Windows采样、身份/权限边界、暂停/恢复及专用进程创建/终止 | `output/summary-cleanup/stage3/native-api.log`、`native-api.exit` |
| Qt组件与三项汇总 | PASS88，退出0，20张1080P WGC截图 | `output/summary-cleanup/stage3/components-verified/native-results.json`、`captures.json`、`components-verified.exit` |
| 正式生命周期 | PASS20，退出0；Host→Qt、发布运行库、窗口/端口/ETW释放及Job异常退出清理 | `output/summary-cleanup/stage3/lifecycle/results.json`、`lifecycle.exit` |
| 对比度 | PASS29对，退出0；新增accent/app实际配对要求4.5:1并通过 | `output/summary-cleanup/stage3/contrast.json`、`contrast.exit` |
| 真实键鼠扩展 | 未完整通过；第三次执行到53项后，旧冻结提示OCR区域未包含新页头文字，按最新范围要求停止完整重跑 | `output/summary-cleanup/stage3/desktop-final/results.json`、`desktop-final.exit`（1） |
| 冻结页头定向检查 | PASS；对上述真实画面使用修正后的页头ROI做单次离线OCR，未发送新输入 | `output/summary-cleanup/stage3/caption-focused-check.json` |

组件流程在实际暂停采样后独立读取生产 `system`，逐一核对 CPU、内存及进程数与控件显示一致，同时检查24px、600、Windows蓝和可访问名称。实际空搜索没有伪造行，顶部仍显示全机数量；暂停状态不被空搜索覆盖，顶栏仍56px。暂停、空搜索、真实新建及确认真实退出后，进程表位置与尺寸均保持，主窗没有通知条或“关闭提示”。无效程序路径保留真实字段错误和输入；取消结束保留专用进程，确认后实际退出且对话框关闭。

此前已生成的八页原始图像均为1920×1080可见窗口物理像素、100%缩放，没有缩放图片冒充测试画面。已视觉复核八页概览及进程、性能、设置原图：三组汇总清晰，命令、导航和表格无重叠/裁切，无通知空白行；性能保留真实曲线、单位和未知状态。技能仅采用桌面适用的对比度、标签、焦点、字阶、稳定布局及有界绘制规则，没有声称Qt栈匹配或执行手机/暗色矩阵。相关证据另存于 [进程页](evidence/summary-cleanup/processes-1080p.png)、[暂停页头](evidence/summary-cleanup/paused-1080p.png)、[真实退出后](evidence/summary-cleanup/termination-1080p.png) 及 [冻结页头](evidence/summary-cleanup/frozen-1080p.png)。

## 已执行命令留档

以下为范围要求到达前的实际命令，便于审查证据，不代表后续局部修改需要重复这些完整流程；以后按 `AGENTS.md` 的变更范围约定选择直接相关检查。

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/publish-desktop.ps1 -SkipRestore
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-qt-components.ps1 -OutputDirectory E:/taskManager/output/summary-cleanup/stage3/components-verified
# 以下API与生命周期在真实提升的Windows worker中执行。
$env:TASKMANAGER_TEST_PORT = '5193'
node tests/native-api.mjs
pwsh -NoProfile -ExecutionPolicy Bypass -File tests/qt-lifecycle.ps1 -OutputDirectory E:/taskManager/output/summary-cleanup/stage3/lifecycle
python tests/qt-contrast.py --output output/summary-cleanup/stage3/contrast.json
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-qt-ui-integration-desktop.ps1 -OutputDirectory E:/taskManager/output/summary-cleanup/stage3/desktop-final -Extended -NativeFileDialogs
```

## 测试修复与限制

第一次组件运行在结束确认阶段遭Windows证据文件短暂共享冲突，原子替换失败；给现有测试证据写入加入最多10次、每次25ms的有界重试后完整通过。没有因此重试业务操作。第一次物理流程未观察到应用历史页Tab/Space切换，第二次的用户页实际已切换但等待区域同时命中页标题和同名表头，第三次实际已勾选冻结且新页头正常显示，旧OCR区域却从页头下方开始而误报。为前两次全量流程添加的无关导航回退已撤销，仅保留冻结文字检查的页头区域 `(210,65,1850,135)`。失败证据保留于 `components/`、`desktop/`、`desktop-verified/`、`desktop-final/`；完整物理流程均不计为通过。修正后的冻结区域只使用本轮已经保存的真实WGC图片完成一次定向OCR验证，没有再运行全量流程。

真实断连后恢复未主动诱发：正式Host退出会按Job合同关闭Qt，不能据此观察持续断连界面；本轮未改用户网络配置，也未用模拟服务替代生产接口。该分支按实现审查记录，不计作实际运行通过。未重跑采集器未改动的全部磁盘、启动项、网络专项；安装/升级/卸载及官方更新生命周期未在本阶段执行。实际后台偏好保存失败、CPU首样预热及非正常系统数据缺失没有人为注入，不计作现场已覆盖。

所有测试采用隔离偏好，只清理本轮Host创建的专用进程身份、端点及自有ETW会话，未结束用户进程、修改用户启动配置或控制用户服务。测试清理后、应用更新前核实仅剩原有用户Host82868及其Qt83156，没有本轮测试Host、Qt、专用workload或提升worker。验收代理没有更改安装目录。

## 已应用到当前应用

主代理在测试清理完成后，通过原API正常关闭旧Host82868及其Qt83156，备份并仅替换安装目录 `C:\Program Files\BlueDomainTaskManager\Takao.Qt.exe`，正常重启后新Host86076/Qt84144窗口就绪。该项实际返回PASS；Qt运行库版本一致，安装文件SHA256 `8D6BB1E806E6F64F7862BB4DFADBEEA16EAC0901068459DCAD6507F7BCAD3D11` 与本轮已测试发布文件一致。未替换后端、修改用户偏好或IFEO，也没有据此宣称安装/升级生命周期全量通过。

结果：`output/summary-cleanup/installed/results.json`。旧Qt备份：`output/summary-cleanup/installed/Takao.Qt.before-EC81E9B1502F.exe`。本次应用后按用户最新约定没有再次运行全量测试。
