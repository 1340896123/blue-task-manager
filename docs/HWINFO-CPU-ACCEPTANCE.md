# HWiNFO 与 CPU 图表验收记录

> 历史验收记录：本文保留当时的产品名、路径与构建结果。自 2026-10-28 起产品统一命名为 Takao；当前命令与目录以 [README](../README.md) 为准，历史记录不作为本次重命名的验收证据。

日期：2026-10-24（Asia/Shanghai）。Windows 11 企业版10.0.26200，28个实际逻辑处理器；Node24.16.0，.NET SDK10.0.401。业务代码最终提交为 `dd9f8ca87dcdabaa94d872b9e6d4ffd99bdf8278`，阶段提交为规格624550b、后端6fb1241、界面577d514及读数列顺序修复dd9f8ca。

CPU双击、原生选择器、Enter/空格均切换总体/逐逻辑处理器图，并保存偏好。HWiNFO SM2只读来源及完整传感器表已实现。本机没有提供HWiNFO共享映射，**成功读取真实HWiNFO及发布端停止/恢复未执行**，不以解析器检查替代。

## 构建与复现

在项目根目录执行；真实API/桌面验收使用管理员终端或现有提升worker，保留正式权限校验。

```powershell
npm ci
npm --prefix src/TaskManager.Web ci
dotnet restore TaskManager.sln --locked-mode
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-desktop.ps1 -SkipRestore
npm run test:hwinfo-cpu
npm run test:hwinfo-cpu-ui
npm run test:hwinfo-cpu-desktop
```

依赖恢复、Web类型检查/生产构建、整解决方案Release、自包含win-x64发布均成功；C#构建0警告0错误。输出 `output/TaskManager-win-x64/start.cmd`，内置.NET/ASP.NET10.0.12、Electron45.0.0及许可。新增测试仅使用隔离配置和5306/9306端口。

亦可从已提升终端依次执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/windows-layout-worker.ps1 -Suites hwinfo-cpu
powershell -NoProfile -ExecutionPolicy Bypass -File tests/windows-layout-worker.ps1 -Suites hwinfo-cpu -HwInfoMode browser
powershell -NoProfile -ExecutionPolicy Bypass -File tests/windows-layout-worker.ps1 -Suites hwinfo-cpu,shared-search,windows-layout-audit -Desktop
```

最终原始结果在 `output/playwright/hwinfo-cpu/native.json`（17项）、`browser.json`（34项）、`desktop.json`（35项），共86个顶层断言；`final-worker.json`记录以上三组、共享搜索及清理审计退出0。原生组另调用生产解析器验证13个独立坏输入场景，见`malformed-layout.txt`；不创建共享映射或提供模拟传感器业务数据。

## H系列结果

| 项目 | 结果 | 实际证据与边界 |
|---|---|---|
| H01 | 通过 | 干净锁定恢复与构建；旧配置缺少cpuGraphMode仍保留页面偏好并显示total；合法模式、传感器排序/列宽可保存，非法模式400且不覆盖原值。 |
| H02 | 通过（本机范围） | 28个真实group/index与独立Get-Counter Processor Information实例一致；专用真实CPU负载下各CPU数值独立变化；百分比0～100，历史含独立来源时间。多处理器组机器未覆盖。 |
| H03 | 通过 | 浏览器及正式Electron双击往返、Enter/空格、选择器、跨页、重启恢复；暂停时切换不清历史，恢复后真实时间缺口形成独立曲线段。 |
| H04 | 通过 | API status=unavailable、source.code=notAvailable、无虚构当前读数；真实缺失原因和Shared Memory Support指引可见；其他Windows采样继续。 |
| H05 | 未执行 | 本机未提供真实HWiNFO映射；未与HWiNFO应用逐项比对成功数据、原名、单位和会话统计。 |
| H06 | 部分通过 | 应用暂停/恢复与缓存保持已验；HWiNFO发布端真实过期、退出和重开无运行条件，未执行。 |
| H07 | 通过（本机范围） | 960×640、1380×860、正式Electron125%和reduced-motion均无文档溢出；HWiNFO表入口、搜索/排序控件、键盘调宽及偏好、串行/离开停止轮询通过；实际传感器数据行仍受H05限制。超过48CPU的分页未在真实多核机器上执行。 |
| H08 | 通过 | 自包含正式Electron真实窗口→HTTP→JSON.NET→CsWin32；模式、来源状态、重启与关闭通过；精确测试身份退出，HTTP/CDP端口释放。 |

13个解析器坏输入涵盖短头、签名、版本、零/未来时间、短stride、数量上限、宽整数越界、重叠分区、非终止文本和非法传感器引用。真实HWiNFO有效/非有限值分支仍受H05条件限制。

## 原有必验与性能回归

以下23个回归命令最终退出均为0：static-v11、native-api、native-metadata-v11、native-core-v11、native-management-v11、native-telemetry-v11、gpu-identity-v11、process-ui-v11、pages-ui-v11、ui-smoke、recovery-e2e、desktop-e2e、process-desktop-v11、native-desktop-v11、system-locations-v11、restricted-process-ui-v11、product-icon、taskbar-integration、desktop-performance-v11、windows-layout、grid-ui、shared-search、windows-layout-audit。覆盖A01～A19及本次相关B/L/G/S；测试只结束自建目标，不更改用户HWiNFO或业务服务。

首轮shared-search默认从开发bin目录启动，未等到Electron页面。提升worker已统一使用正式便携目录；在自包含输出上重跑42秒通过，未改应用就绪条件。`regression-initial.json`保留首轮诊断，最终以`final-worker.json`及`validation-summary.json`合并结果为准。

`system-locations-v11`保留原有`PARTIAL_WINDOWS_OBSERVABILITY`：四个Shell请求被Windows受理，公共Startup、启动设置、服务管理器实际窗口确认；当前用户Startup地址未独立确认。此项不写成四个真实窗口全部通过。完整细节在`output/playwright/system-locations-v11-results.json`。

满120个真实历史点后的可见默认进程页性能通过：Host全机CPU0.294%（目标≤1%）、基础采样P95104.157ms（≤500ms）、搜索到下一帧交互P9520.300ms（≤100ms）；29个稳定样本。报告为`output/performance/desktop-v11-release.json`。该预算测量针对默认进程页，不外推HWiNFO8192项、逐CPU满历史绘制或长期运行；采样耗时不含HTTP序列化/传输。

## 截图与清理

- 总图：`output/playwright/hwinfo-cpu/desktop-cpu-total.png`
- 逐CPU：`output/playwright/hwinfo-cpu/desktop-cpu-logical-1380.png`
- 最小窗口：`output/playwright/hwinfo-cpu/desktop-cpu-logical-960.png`
- 实际125%：`output/playwright/hwinfo-cpu/desktop-cpu-logical-125.png`
- HWiNFO缺失状态：`output/playwright/hwinfo-cpu/desktop-hwinfo.png`

已检查应用截图，使用既有Windows蓝色Token，无模拟曲线。最终`output/playwright/windows-layout/cleanup-audit.json`为PASS，processes和openPorts均空（包含5306/9306）。未为测试安装、配置或关闭用户HWiNFO，未操作用户业务进程。

## 安装包复现

从上述自包含目录执行标准打包与独立静态提取校验，不运行安装器、不更改本机现有安装：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-installer.ps1 -SkipBuild -MakeNsisPath C:\nsis\makensis.exe
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-installer.ps1
```

产物路径为`output/releases/BlueDomainTaskManager-v1.1.1-win-x64-setup.exe`、同名ZIP与`SHA256SUMS`；该功能包沿用仓库当前版本号。打包日志在`output/playwright/hwinfo-cpu/installer-build.log`和`installer-verification.log`，逐文件/manifest校验结果在对应`output/installer-verification-*`目录；不把静态提取校验称为实际安装/卸载验收。
