# 默认窗口尺寸调整验收

日期：2026-11-04（Asia/Shanghai）。尺寸实现提交 `9b014f8`，最大化/还原持久化修复提交 `4a52f8d`。

正常启动默认 **1100×720 逻辑像素客户区**，最小960×640；保留用户调整、最大化及还原。旧精确默认1600×950、1380×860和旧测试残留1920×1080迁移到新默认，其他自选尺寸继续恢复。

## 实际结果

| 检查 | 结果 | 本机证据（相对仓库根目录） |
|---|---|---|
| Qt Release | 编译通过 | `cmake --build output/qt-build --target Takao.Qt --parallel 4`，阶段1执行，退出0 |
| .NET Host Release | 编译通过，0警告0错误 | `dotnet build src/Takao.Host/Takao.Host.csproj -c Release --no-restore`，阶段1执行，退出0 |
| 真实正常窗口与偏好 | 37项通过 | `output/compact-window/size-results.json`：1100×720默认、960×640最小、1190×780自由调整/最大化/还原/保存/重启、三个旧尺寸迁移并保存、1230×810自选尺寸保留 |
| 八页实际鼠标导航 | 29项通过，8张WGC截图 | `output/compact-window/physical-results.json`、`01-compact-page.png`～`08-compact-page.png`；每次检查真实客户区及OCR页标题 |
| 真实后端 API | 79项通过，退出0 | `node tests/native-api.mjs`；`output/compact-window/native-api.txt` |
| 正式 Qt 生命周期 | 20项通过，退出0 | PowerShell 7运行 `tests/qt-lifecycle.ps1 -OutputDirectory output/compact-window/lifecycle`；`output/compact-window/lifecycle/results.json` |
| 本机当前用户窗口 | 已应用1100×720，其他偏好保持 | `output/compact-window/current-user-window.json`：由生产Qt正常resize→偏好API保存，未直接编辑配置文件，Host退出0 |

主屏实测2560×1440、窗口96DPI/100%；没有修改显示分辨率。最新用户要求优先于旧版强制1920×1080窗口画面合同：紧凑窗口截图保持真实1100×720客户区，含原生标题栏/边界的WGC图像为1102×752；没有拉伸为1080P。现有生命周期专项仍使用其原1920×1080窗口模式。

八页均实际读取生产Host→HTTP→JSON.NET→CsWin32数据。视觉复核了标题、命令栏、蓝色导航、性能资源双栏和真实曲线、设置卡片及240px控件；数据表使用自身横向/纵向滚动，设置与资源使用内部纵向滚动。截图裁切到应用，没有采集无关桌面。

本机实际应用前由API读回的窗口记录为1388×640，应用后为1100×720、缩放1、置顶false；其他完整偏好前后相等，随后独立读取真实用户配置确认尺寸已保存。早先只读观察到过2560×1369，该值不作为最终应用前记录。证据以 `current-user-window.json` 为准。

## 发现与修复

首次隔离测试确认：客户区从1190×780最大化后再还原虽显示1190×780，保存的偏好却变成主屏最大化尺寸2560×1369。Qt/Windows窗口状态通知与resize事件的先后顺序使同步检查仍可能保存最大化边界。修复将几何读取放到下一事件循环，确认最终正常状态后读取Qt `normalGeometry()`；复测真实API保存1190×780，关闭并重新启动仍保持该尺寸。原始失败诊断保存在 `output/compact-window/maximize-persistence-diagnostic.json`，不累计为通过结果。

## 复现与边界

验证脚本与完整Host日志保存在忽略的 `output/compact-window/`：管理员运行 `run-verification.ps1` 中的 `verify-window.py` 和 `verify-pages.py`；默认尺寸检查使用临时 `TASKMANAGER_DATA_DIR`，无需任何模拟业务数据。实际导航全部由指定windows-computer-control技能的FYLHelper产生，截图仅使用WGC；窗口管理API只用于前置、尺寸、最大化和关闭。

较早诊断中的Alt+2物理快捷键没有切换页面，没有记为通过；最终八页使用基于WGC画面的真实鼠标点击。生命周期首次被Windows PowerShell 5的UTF-8脚本解析阻断，产品尚未执行；改用本机PowerShell 7后原脚本20项通过，结果以 `lifecycle-exit.txt` 为准。

没有运行安装/升级/卸载、修改IFEO、启动项或用户服务；所有测试进程都由本次测试创建并精确清理。未重复完整Qt组件/更新/网络专项矩阵，不把历史结果作为本轮通过。正文/控件沿用指定ui-ux-pro-max桌面规则与已有Token，不新增Qt以外的渲染栈。

实际验证产物SHA256：

- `Takao.Qt.exe`：`CE2F816935E44B8502668C4AD5773D2E29B9401D1FBA84B178B446E1EE421E96`。
- `Takao.Host.dll`：`9538851107BA75BB1D872AF1F821D9EEE0044ACCB0967CC30383EBF87AED84AD`。
