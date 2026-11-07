# 系统信息 Qt 接线与组件验收

日期：2026-11-07（Asia/Shanghai）。后端基线 `37bcc3f`；本阶段实现 [SYSTEM-INFORMATION.md](SYSTEM-INFORMATION.md) 的 Qt 页面、HWiNFO 可见页移除及直接相关偏好兼容，不运行其它业务页、进程操作、安装更新或完整回归。

“系统信息”位于服务下方和底部设置之间；技术身份为 `systemInformation`、索引8/Alt+9，设置保留索引7/Alt+8。概览通过既有回环HTTP异步读取七类原生信息，按参考顺序以左侧矢量图标/类别、右侧型号/事实呈现；沿用浅色Fluent、Windows蓝色、MASTER字阶与空间。内容限宽1080px、类别112px，自然换行并纵向滚动，型号可选择。内存速度明确为固件MT/s，磁盘为实际GiB/TiB，软件适配器明确标记，缺失字段保留“—”和原因。

搜索筛选本页真实硬件，空结果在现有页头说明；刷新有忙态、采集时刻、错误/部分可用状态，不新增成功通知条。独立硬件内容不随每秒snapshot重建，仅采集时刻改变复用现有控件，硬件内容改变按objectName恢复焦点并保留滚动。Esc清空硬件搜索后焦点回本页滚动区。新页不读取隐藏表格的列/选中/CSV偏好；原页面搜索和设置身份保持。HWiNFO磁贴、传感器模型/表格、共享内存说明和Qt读取定时器均删除；旧HWiNFO选中回退并保存为CPU，CPU总体/逐逻辑处理器控件保留。

## 实际验证

| 项目 | 结果与证据 |
|---|---|
| Qt6.8.3/MSVC19.52 Release | `Takao.Qt` 与定向组件目标构建退出0，无编译警告/错误；CMake只提示非必需Vulkan头未发现，不影响Widgets。 |
| 生产Host/HTTP/原生Qt组件 | 60项通过，`output/system-information/stage3/components/results.json`。所有硬件从本次生产HTTP取得，没有模拟行或固定业务值。 |
| 七类与逐设备 | 控件型号与CPU/GPU/每盘/显示器真实DTO逐一对应；逐DIMM插槽和MT/s可读；无页面水平滚动。 |
| 搜索/刷新/偏好 | 实际控件搜索、无匹配文字、真实刷新时刻前进、搜索焦点/查询及有滚动时的位置保持，新页查询成功保存；旧hwinfo偏好回退并持久化CPU。 |
| 性能/设置代表 | 性能设备列表没有HWiNFO，CPU总体/逻辑处理器切换可用；原设置入口和页头保持。不遍历未修改页面。 |
| 精确清理 | 最新组件运行测试Host94696已退出0，见同目录`cleanup.json`；使用独立目录/端口/偏好，没有关闭用户Takao实例。 |

组件测试调用Qt公共控件行为，**不宣称实际键鼠或WGC显示测试通过**。1920×1080 WGC实拍、实际FYLHelper键盘和跨阶段最终检查由主代理单独记录；本报告不挪用历史图像。现场枚举出的不同LUID适配器均忠实显示，物理设备/别名区分属于后续原生来源审查，不能按同名粗暴合并。

## 可复现命令与运行目录

```powershell
& scripts/import-msvc.ps1
cmake -S src/Takao.Qt -B output/system-information/qt-build -G Ninja -DCMAKE_PREFIX_PATH=E:/taskManager/output/Qt -DCMAKE_BUILD_TYPE=Release -DTAKAO_BUILD_INTEGRATION_TESTS=ON
cmake --build output/system-information/qt-build --target Takao.Qt Takao.Qt.SystemInformation --parallel 4
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-system-information-components.ps1
```

独立运行目录为 `output/system-information/release`：复制既有已发布自包含运行库/相同包版本的deps和runtimeconfig，更新本轮无新增依赖的Host DLL/EXE及Qt EXE；不覆盖用户现有目录。不以此方式声称已重新打包或验证安装器。组件脚本再复制到专用随机测试目录，用本轮组件EXE代替该副本Qt入口，测试后按原Host进程句柄清理。

代码审查采用用户指定UI/UX技能的桌面适用项：现有语义色、4/8px节奏、中文可访问名称、可见焦点、文字状态及可选换行。没有Qt专用栈匹配或手机/暗色主题验收的声明。真实拔插、硬件字段缺失、超时与网络故障未人为诱发，本阶段不计为现场已覆盖。
