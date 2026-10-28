# 1.1.1 预览构建：磁盘信息与简约图表

2026-10-28。此轮沿用 `1.1.1` 应用版本，发布工作流按实际运行序号生成新的 `v1.1.1-build.<运行序号>` 标签；具体版本与下载附件见 [GitHub Releases](https://github.com/1340896123/blue-task-manager/releases)。

## 本版变化

- 性能页补齐物理磁盘型号、HDD/SSD 介质类型和总线类型。通过 Windows 原生设备编号验证与性能计数器的关联，无法读取的字段保留明确原因，不从型号或盘符猜测类型。
- 磁盘页面聚焦读取/写入吞吐：主图和左侧小曲线均显示真实读写速率，移除活动时间数值、百分比与曲线，保留设备信息和读写事实项。
- CPU、逐逻辑处理器、内存、磁盘、网卡与 GPU 图表统一采用白底、浅灰网格、蓝色主实线和深灰次虚线，保留实际时间、单位、图例与未知状态。

## 验证与下载

此前专项验证使用生产 UI、Release Host 和真实 Windows 采样链，结果与截图见 [磁盘身份验收](DISK-IDENTITY-RESULTS.md)、[磁盘读写展示验收](DISK-PRESENTATION-RESULTS.md)和[图表配色验收](CHART-PALETTE-RESULTS.md)。这些记录分别对应其标明的业务提交，不能代替新安装包的发布验收。

新安装包须通过现有 Windows 工作流的安装生命周期、真实采样与 Electron 桌面检查后发布。请以对应 Release 的 `acceptance.json`、`acceptance.md`、源码提交和安装器 SHA256 为本次交付证据，并使用同一 Release 的 `SHA256SUMS` 校验 Setup.exe。构建与发布方式见[手动安装包流程](MANUAL-INSTALLER-BUILD.md)。

## 适用范围

Windows x64 安装包内置 .NET 10、ASP.NET Core 与 Electron，安装及运行需要管理员权限；安装器尚未数字签名。CI 的实际提升令牌验收不代表交互式 UAC 同意或普通用户桌面已覆盖。

真实磁盘拒绝访问、部分属性失败、RAID/虚拟设备、热插拔与编号复用等分支尚无本轮真实成功证据；未为验收拔除用户磁盘或伪造业务快照。详细范围以专项报告及对应 Release 的实际验收报告为准。
