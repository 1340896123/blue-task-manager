# 1.1.0 Preview 1

[公开仓库](https://github.com/1340896123/blue-task-manager) · [下载与校验附件](https://github.com/1340896123/blue-task-manager/releases/tag/v1.1.0-preview.1)

React、Tailwind CSS、Electron.NET和CsWin32真实Windows链路，应用C# JSON统一JSON.NET。业务基线`251897e`；发布标签记录最终文档、观测器及打包工具提交。

## 本版变化

- 进程虚拟树、实时详情、TCP/UDP端点、文件位置/属性、CSV、优先级、效率模式、服务操作、真实GPU/磁盘/网卡资源和可恢复偏好。
- 统一Windows蓝色设计Token，产品SVG/多尺寸ICO覆盖Web、Host EXE、Electron窗口与任务栏提供的图标。
- 设置增加“任务栏右键中的任务管理器”。它接管Windows原有任务管理器入口，也影响Ctrl+Shift+Esc；默认关闭，需管理员写权限，不覆盖其他工具。启用前固定完整目录并确保目标账户可读取，移动/删除前先在原目录取消。
- 受限进程保留真实基础行。SGuard64.exe可按exe名称/PID查找；无法读取身份字段时可能独立显示，Windows文件说明“ACE-Guard Client”尚未作为已接入搜索字段。

## 验收

最新业务代码顺序执行11组回归334项，以及正式Electron42、进程34、原生桌面22、任务栏23、图标16、受限进程9项，共480项通过，见[完整报告](ACCEPTANCE-RESULTS.md)。不使用mock或模拟业务数据。

满120个真实历史点、窗口全程可见、约34秒/30个UI样本，本机1083～1089进程、28逻辑处理器：Host平均整机CPU0.207%、含Electron整应用0.520%；采样p95 56.52ms，搜索至下一rAF回调p95 25.6ms。整应用私有提交约510MiB；工作集含共享页面，rAF不等同像素上屏，本机短期测量不代表所有机器或长时保证。

发布时将最终ZIP解压到包含空格的新目录，再以`TASKMANAGER_RELEASE_DIRECTORY`指定该目录执行`npm run test:desktop`。独立解压复验结果在Release附件`E2E-RESULTS.json`，其中绑定ZIP的SHA256；ZIP逐文件内容哈希也由打包工具核对。

## 运行

需要Windows x64和Microsoft.NETCore.App 10.0、Microsoft.AspNetCore.App 10.0（兼容后续10.0补丁）；这是框架依赖便携版。完整解压`TaskManager-win-x64`，运行`start.cmd`或`TaskManager.Host.exe`，无需Node/npm。默认普通账户运行，不自动UAC；生产默认不开CDP。

附件为完整便携ZIP、`SHA256SUMS`和`E2E-RESULTS.json`。可用`Get-FileHash -Algorithm SHA256`与清单对照。ZIP包含隐藏的`.electron`、Electron EXE、生产wwwroot、图标、说明和依赖许可；不含本机偏好、环境凭据或目录链接。

## 预览范围

当前标准账户的服务创建、全局任务栏注册实际返回Windows错误5；拒绝/保护和entry复用已验收，管理员成功服务启停/超时、成功任务栏注册/取消及真实任务栏点击重定向未执行。没有为测试启用本机全局接管，也未操作SGuard或用户业务服务。

逐进程网络吞吐尚未接入，端点不冒充吞吐；启动策略编辑、保护绕过及驱动分析不在本版。当前用户Startup文件夹前台可见性仍未确认，其他三个系统入口已确认；热插拔、非常态树部分执行、长时泄漏、其他Windows版本及完整读屏认证未覆盖。详见[验收矩阵](ACCEPTANCE-RESULTS.md)。
