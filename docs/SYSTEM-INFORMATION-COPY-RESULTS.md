# 系统信息复制定向验收

日期：2026-11-08（Asia/Shanghai）。规范提交 `5b730f6`，功能提交 `d9b64fd`。仅检查新增复制命令、系统信息文本、剪贴板、直接影响的命令栏与原进程页隐藏行为。

系统信息页右上角“刷新”左侧新增“复制”，沿用32px中性次命令。复制当前完整七类硬件概览、采集时间、状态与已有不可用原因；搜索隐藏的内容仍会复制，不触发硬件采集。使用标准Qt剪贴板，成功后固定宽按钮显示“已复制”1800ms；支持鼠标和原生键盘，未取得快照时禁用。文本在既有View格式化时同源积累，无新API、采集器、依赖或模拟业务数据。

## 实际结果

| 范围 | 结果与证据 |
|---|---|
| SIC01 / Qt构建 | Qt6.8.3/MSVC Release生产与定向组件目标构建退出0；.NET源码及采集链未改动，不重复构建/回归后端。 |
| SIC01 / 真实剪贴板 | [组件记录](evidence/system-information-copy/components.json)66项通过：生产Host真实快照，七类及逐设备显示事实/原因进入剪贴板，含时间与单位；首次快照前禁用。 |
| SIC02 / 完整复制与反馈 | 真实类别搜索、无匹配搜索均复制完整快照；查询、焦点、采集时间保持；“复制/已复制”切换不移动命令，反馈到时恢复。完整硬件在本机1080P能全部显示，真实滚动最大值为0；不将非零滚动位置保持记为实测通过。 |
| SIC03 / 正式桌面 | [物理记录](evidence/system-information-copy/desktop.json)37项通过；FYLHelper实际鼠标、Space、Enter、搜索、滚轮和F5；剪贴板哨兵证明键盘确实写入，F5后复制最新时间。未重跑性能/设置等旧页面矩阵。 |
| SIC03 / 显示 | [普通态](evidence/system-information-copy/overview-1080p.png)、[已复制与焦点](evidence/system-information-copy/copied-1080p.png)为WGC原始1920×1080窗口像素、96 DPI/100%，按钮对齐刷新，反馈无裁切或重叠，蓝色焦点可见。复用既有语义色，按指定ui-ux-pro-max桌面适用规则复核。 |
| 专用实例清理 | 最终组件Host77360退出0，见[清理记录](evidence/system-information-copy/components-cleanup.json)；物理Host46268/Qt82280退出，端口14834关闭，Host退出0。未关闭用户原有实例。 |

主屏实际2560×1440，测试只调整本次专用窗口至1920×1080，没有更改用户显示模式，也没有测试其它画面尺寸。物理轮滚输入确已执行，但内容没有溢出，因此“滚动区外文本”由同源文本实现审查支持，非零位置分支未在本机诱发。未人为诱发剪贴板被其它应用锁住、断线、刷新失败或硬件权限缺失，相关错误/旧快照标记为代码审查，不计现场通过。

## 可复现命令与交付目录

```powershell
. ./scripts/import-msvc.ps1
cmake --build output/system-information/qt-build --target Takao.Qt Takao.Qt.SystemInformation --parallel 4
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-system-information-components.ps1 -CopyOnly -ReleaseDirectory E:\taskManager\output\system-information-copy\release -QtExecutable E:\taskManager\output\system-information\qt-build\Takao.Qt.SystemInformation.exe -OutputDirectory E:\taskManager\output\system-information-copy\components-verified
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-system-information-desktop.ps1 -CopyOnly -ReleaseDirectory E:\taskManager\output\system-information-copy\release -OutputDirectory E:\taskManager\output\system-information-copy\desktop
python -m py_compile tests/qt-system-information-desktop.py
git diff --check
```

独立可运行目录 `output/system-information-copy/release`：从既有`output/system-information/release`复制未改动的生产Host与自包含运行库，替换本轮Qt EXE，以`start.cmd`启动。[SHA256](evidence/system-information-copy/release-checksums.json)确认发布目录Qt与当前构建字节一致。本次没有更新用户安装或制作安装包。

## 检查修正

阶段2初次连续复制同文本时曾遇Windows/OLE空读回；相同剪贴板文本现在跳过重复写入，保留读回检查及失败文字。组件按真实事件循环观察结果，最终连续复制、搜索和物理输入通过。早期失败保留于`output/system-information-copy/components`与`components-debug`，未计通过。

独立复核时发现组件在刷新从忙态恢复后、命令栏布局请求尚未处理便记录位置，导致布局稳定断言误报；现先处理已排队布局再测量，最终定向组件重新构建/运行退出0。该失败保留于`components-reviewed`；同时增加实际滚动范围记录，避免把0位置检查描述成非零滚动验收。生产功能和物理验收构建未因测试说明修正而改变。
