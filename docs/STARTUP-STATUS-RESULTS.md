# 启动应用真实状态验收（2026-10-28）

> 本报告保留 Takao 项目改名前的受测文件名、目录、散列与截图。当前项目和复现入口使用 Takao 名称，见 [README](../README.md) 与 [命名验收](TAKAO-RENAME-RESULTS.md)。

启动页“全部无法确定”已修复。真实 Windows Run、Run32 和 StartupFolder 批准记录现在贯通 CsWin32 → JSON.NET → React：可识别的启用/禁用状态显示中文标签，未知格式、跨范围冲突及 RunOnce 保留具体原因。浏览器专项 **56** 项、正式 Electron 专项 **58** 项、已有 11 组回归 **357** 项及正式桌面基线 **43** 项通过，共 **514 项断言**。

## 构建与环境

- 日期：2026-10-28，Asia/Shanghai。Windows 11 企业版 x64，10.0.26200.0；Node v24.16.0；.NET SDK 10.0.401；Electron 45.0.0。
- 规格提交 `bf5bef6`，生产修复提交 `d7935d223020f9f6d19f01cd2853f8baef427761`。独立并行的命名文档提交 `49f0367` 不属于状态业务实现；本报告截图以实际受测构建为准。
- 专项和桌面基线均运行完整不可变目录 `output/startup-status-release-d7935d2`；已有回归读取 `src/TaskManager.Host/bin/Release/net10.0-windows10.0.19041.0`，其 Host DLL 与该目录的散列一致。没有使用旧安装版或伪造页面代替。
- `npm run typecheck`、`npm run build`、锁定 SDK 的 solution Release 构建通过，C# 编译0警告、0错误；静态规则检查在回归中通过。根目录及Web的 `npm ci --prefer-offline --no-audit --no-fund`、solution `dotnet restore --locked-mode` 均exit 0，npm/NuGet锁文件无改动。恢复后前端再次构建的JS/CSS与受测产物相同。
- NuGet恢复出现5条NU1900，原因是nuget.org漏洞数据源不可达；这不影响依赖锁恢复通过，但本轮不能声称漏洞审计通过。该恢复警告与上面的C#编译0警告是不同执行结果，见 [构建/依赖记录](evidence/startup-status/build-checks.json)。

| 实际构建文件 | SHA256 |
|---|---|
| TaskManager.Host.exe | `965f4b48881e5471d4718fef5fea3b26bc9cfa23034feeefbf717dd862068f34` |
| TaskManager.Host.dll | `21dbc78cd26f90311c29f80a971979790b9acf8d9fc497980148e6c1a7857850` |
| wwwroot/index.html | `df4fedce8bb8f18f130960b739f1c9bbfaedfa8ff5923ef0bdeb6fdba8e127fe` |
| index-C5jaTmM-.js | `905311e84a54b57d6b1a8b0ad5000dc33c3e22289ba4b64be92d565e7ea4fbb0` |

完整构建文件大小、JS/CSS 散列和实际入口保存在 [浏览器报告](evidence/startup-status/browser-results.json) 与 [Electron 报告](evidence/startup-status/desktop-results.json)。两种模式的固定 worker 都由标准 Windows RunAs 启动，并由独立 CsWin32 探针核实实际提升令牌；未修改 UAC、目录 ACL 或安全策略。

## 本轮 S01～S07

| 验收 | 结果与实际证据 |
|---|---|
| S01 构建与规则 | 根/Web npm锁恢复、NuGet锁恢复、TypeScript、生产前端、C# Release、静态 CsWin32/JSON.NET/无业务 mock 检查通过；锁文件无变，恢复后前端产物散列一致。NU1900漏洞源不可达单独保留 |
| S02 现有真实来源 | 浏览器和桌面各读取39项：22个原机已有项及17个专用来源。全部与独立 PowerShell/.NET Registry 只读 oracle 一致；已有项9个启用、13个禁用，未修改它们。报告仅保存统计，不归档全机启动清单 |
| S03 Run 状态与边界 | GUID 专用真实 HKCU Run 值覆盖2/3、未知1/6/7、错误类型、4字节短值、16字节超长值及无批准记录。显示值与原因一致；未知保持 null，缺失明确默认启用 |
| S04 Startup 文件 | 用标准 WScript.Shell 创建当前用户启用/禁用和公共禁用三个真实 `.lnk`；批准值匹配完整文件名。另加相反状态的无扩展名记录，证明不会按显示名误配。快捷方式未执行 |
| S05 视图/一次性/冲突 | HKLM32/64 同名同命令保留两行，并分别从原生 Shell Run32/Run 取相反状态；HKCU共享视图无重复。RunOnce不套同名Run批准；显式跨hive冲突及缺失默认值与另一范围禁用冲突均保留未知 |
| S06 UI与正式桌面 | 真实中文状态及tooltip、两方向状态排序且null保持最后、搜索/键盘详情/Escape、开放详情在外部修改专用注册值后刷新更新、可见刷新命令及官方Windows设置入口通过。1380×860、960×640、正式Electron实际125%均检查，无文档级溢出；reduced-motion有效，无renderer异常 |
| S07 回归与清理 | 下表11组回归和原有正式桌面成功。专项每次精确清理32个专用注册值与3个快捷方式；清理按名称GUID、类型、已写内容或快捷方式目标/参数/描述核验，Host关闭失败也独立尝试fixture清理。额外独立只读复核残留值0、启动文件0、测试端口监听0 |

专项没有调用启动项写入产品接口，没有修改系统入口或用户服务，也没有执行用户启动命令。测试准备曾遇到 `.cmd` 写入被 Windows 拒绝，改用标准真实 Shell 快捷方式完成；测试脚本的 PowerShell stdin UTF-8 与排序按钮定位问题已修正后重跑。初次准备失败也完成精确清理，不计入通过断言。

## 基线回归

| 套件 | 结果 | 断言数 |
|---|---|---:|
| static-v11 | PASS | 3 |
| native-api | PASS | 80 |
| native-metadata-v11 | PASS | 5 |
| native-core-v11 | PASS | 25 |
| native-management-v11 | PASS | 85 |
| native-telemetry-v11 | PASS | 24 |
| gpu-identity-v11 | PASS | 12 |
| process-ui-v11 | PASS | 34 |
| pages-ui-v11 | PASS | 49 |
| ui-smoke | PASS | 36 |
| recovery-e2e | PASS | 4 |
| desktop-e2e | PASS | 43 |

上述基线覆盖真实采样、专用进程启动/结束、身份与同源保护、全部页面、暂停/恢复、断连恢复及 Electron 原生窗口关闭。A01构建和A02～A19适用范围的证据分别见 [11组汇总](evidence/startup-status/regression-v11.json)、[正式桌面基线](evidence/startup-status/desktop-regression.json)。本次启动状态114项专项独立于这400项基线，不重复计算回归中的静态3项。

## 可复现命令

```powershell
# 使用项目固定SDK；全局10.0.403不能满足global.json的严格10.0.401。
npm ci --prefer-offline --no-audit --no-fund
npm --prefix src/TaskManager.Web ci --prefer-offline --no-audit --no-fund
& .\output\tools\network-sdk-10.0.401\dotnet.exe restore TaskManager.sln --locked-mode
npm run typecheck
npm run build
& .\output\tools\network-sdk-10.0.401\dotnet.exe build TaskManager.sln -c Release --no-restore -p:ElectronSkipExecCommands=true

# 固定 worker 校验实际提升令牌。等待对应worker-*.json complete:true/exitCode:0。
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File "E:\taskManager\tests\startup-status-worker.ps1" -Mode browser -ReleaseDirectory "E:\taskManager\output\startup-status-release-d7935d2"'
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File "E:\taskManager\tests\startup-status-worker.ps1" -Mode desktop -ReleaseDirectory "E:\taskManager\output\startup-status-release-d7935d2"'
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File "E:\taskManager\tests\startup-status-worker.ps1" -Mode regression -ReleaseDirectory "E:\taskManager\output\startup-status-release-d7935d2"'
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File "E:\taskManager\tests\startup-status-worker.ps1" -Mode desktop-regression -ReleaseDirectory "E:\taskManager\output\startup-status-release-d7935d2"'
```

这些模式本次按顺序执行。专项运行结果在 `output/playwright/startup-status`；默认也可传当前完整 Release 目录，必须先构建生产UI和Host。原有11组回归仍读取源码Release目录，不能仅凭改变专项目录参数宣称已更换全部基线输入。

## 截图与清理证据

- [浏览器1380状态列](evidence/startup-status/browser-1380-states.png)
- [正式Electron1380](evidence/startup-status/desktop-1380-states.png)、[960窄窗](evidence/startup-status/desktop-960-states.png)
- [正式Electron实际125%原生窗口](evidence/startup-status/desktop-125-states-native.png)
- [外部禁用后实时更新的详情](evidence/startup-status/desktop-detail-disabled.png)
- [独立清理复核](evidence/startup-status/cleanup-check.json)

截图仅显示本次真实专用来源，截取本应用区域，未注入API或业务数据。Host/Electron与专用进程均按本次创建身份清理；没有按进程名称批量终止。

## 未执行与已知边界

- GitHub和微软在线抓取仍受TLS/连接失败限制；不能把候选链接说成已读取固定源码。来源研究边界见 [STARTUP-STATUS.md](STARTUP-STATUS.md)。
- 没有通过真实Windows设置验证6/7的Shell语义；本次仅证明应用对它们保留未知，未按奇偶猜测。没有强改现有StartupApproved键ACL来制造访问拒绝，也未修改用户组策略验证优先级。
- RunOnce不提供最终执行状态；默认启用、显式批准状态都不保证文件存在或最终执行成功。此次不增加Appx StartupTask来源、策略编辑或启用/禁用写接口。
- 未重跑安装器/升级签名生命周期、长期性能压力、其他Windows版本或完整读屏认证；这些不计入本轮514项通过。
