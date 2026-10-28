# 磁盘型号与类型验收结果

> 历史验收记录：本文保留当时的产品名、路径与构建结果。自 2026-10-28 起产品统一命名为 Takao；当前命令与目录以 [README](../README.md) 为准，历史记录不作为本次重命名的验收证据。

2026-10-26，Windows x64，业务代码 `3ee3ce4`（原生阶段 `8f2cec5`、规格阶段 `79de8bd`）。本轮使用当前 Release Host、生产 React 构建和官方 Electron.NET runtime，全部业务数据来自本机 Windows；未修改管理员校验或 `global.json`。本轮不包含安装、升级或用户当前安装目录替换。

Web 生产构建（含 TypeScript）、Host Release 构建和 `git diff --check` 通过。静态规范检查 3 项、生产原生 probe 56 项、真实浏览器 70 项、正式 Electron 75 项通过，共 204 项。浏览器与桌面两份专项报告均为 `PASS`、`cleanup:true`，两个标准 RunAs worker 均独立核实真实提升令牌。

## 实际设备与验收范围

| 原始 PDH 实例 | 原生型号 | 类型 | 总线 |
|---|---|---|---|
| 0 D: | WDC WD10EZEX-08WN4A0 | HDD | SATA |
| 1 E: | ST4000VX007-2DT166 | HDD | SATA |
| 2 G: | QUANXING C201 256GB | SSD | SATA |
| 3 F: | GALAX T0480A | SSD | SATA |
| 4 C: | Great Wall GT70 2TB | SSD | NVMe |

五盘的设备编号分别由原生 `STORAGE_DEVICE_NUMBER` 读回，与独立 `Get-PhysicalDisk`、`Get-Disk`、`Win32_DiskDrive` 和 `Get-Counter` 对照。型号取设备报告的 ProductId；独立 FriendlyName 的 `NVMe ` 前缀没有拼入产品型号。HDD/SSD 和总线全部逐盘一致。动态吞吐、活动率和 `_Total` 排除保持原合同；系统活动率仍是最忙磁盘，读取总速率仍只汇总各物理实例一次。

专项脚本允许合法的逐字段 `null + unavailable`；只对可读字段与确定的独立结果比较，完整UI成功样本从本机真实可读磁盘中挑选。本机五盘的四个身份字段均实际可读，没有以空值通过本次设备对照。

| 项目 | 结果与证据 |
|---|---|
| D01 | Web/Host 构建成功；静态 3 项确认 JSON.NET、CsWin32、Token 和真实数据边界。当前机器未安装固定的 SDK 10.0.401，构建直接调用已安装的 10.0.403 MSBuild，项目 SDK 固定文件未修改。 |
| D02 | 两个真实管理员 Host 均获得至少三帧 PDH 样本；五盘型号、介质、总线、设备号与四个独立系统来源匹配，逐字段可用状态正确。见 browser/desktop JSON。 |
| D03 | 原生 probe 验证严格实例解析、描述符偏移、终止符、非 ASCII、溢出、旧身份失效及已有失败原因保留；真实本机五盘全部可读。实际 accessDenied、不支持、部分属性失败、RAID/虚拟设备分支未触发，不能记作真实成功。 |
| D04 | 五盘均可键盘 Enter 选中；型号完整进入标题和事实项。按真实型号、HDD、SSD、SATA、NVMe 搜索均准确保留匹配设备；编号 0 正常显示。 |
| D05 | 浏览器及正式 Electron 的 1380×860、960×640，正式 Electron 实际 125%（API读回 `zoomFactor=1.25`）、reduced-motion 均通过；标题可换行和选择，事实项可达，无文档级横向溢出。已目视复核截图，无重叠。 |
| D06 | 原生 probe 实际等待31秒确认缓存刷新、移除实例淘汰和64轮/320次设备刷新；句柄数301→301，首次五盘约3.93ms、缓存约0.10ms。生产 API 暂停保持真实快照，恢复继续采样；元数据未复制进120点历史。未拔除用户磁盘，真实热插拔/编号复用未执行。 |
| D07 | 实际生产 UI→同源HTTP→JSON.NET→CsWin32→Windows 链路通过，覆盖 B14/L04/L08/L10 的相关磁盘行为；无UI运行时错误、IFEO写入或用户服务控制请求。测试只关闭本次创建的Host/浏览器与精确子进程身份，两端口及测试进程树均清理。 |

## 可复现命令

在仓库根目录执行；管理员测试由 Windows 标准 RunAs 启动固定 worker，不以请求受理代替实际令牌或结果证据。测试默认读取当前 `src/TaskManager.Host/bin/Release/net10.0-windows10.0.19041.0`，其中存在官方 `.electron` runtime，避免依赖旧 `output/TaskManager-win-x64`。

```powershell
npm run build
dotnet 'C:\Program Files\dotnet\sdk\10.0.403\MSBuild.dll' src/TaskManager.Host/TaskManager.Host.csproj /t:Build /p:Configuration=Release /p:ElectronSkipExecCommands=true /p:BuildProjectReferences=true /v:minimal /nologo
node tests/static-v11.mjs

# 原生阶段已实际执行；包含31秒缓存刷新和真实设备对照
tests/TaskManager.DiskTests/bin/Release/net10.0-windows10.0.19041.0/TaskManager.DiskTests.exe output/disk-native-backend.json

Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File "E:\taskManager\tests\disk-identity-worker.ps1" -Mode browser'
Get-Content output/playwright/disk-identity/worker-browser.json
# 等到 complete=true 且 exitCode=0 后再执行同端口的桌面套件
Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File "E:\taskManager\tests\disk-identity-worker.ps1" -Mode desktop'
Get-Content output/playwright/disk-identity/worker-desktop.json
git diff --check
```

## 保存证据

- [浏览器70项结果](evidence/disk-identity/browser-results.json)、[正式Electron75项结果](evidence/disk-identity/desktop-results.json)：逐盘实际字段、独立系统对照、实际构建文件SHA256、窗口几何、查询/键盘/暂停与清理结果。
- [原生56项结果](evidence/disk-identity/native-backend-results.json)：真实设备、首次/缓存开销和句柄边界；边界fixture与真实设备结果分别标注。
- [960窗口事实区](evidence/disk-identity/browser-960-facts.png)、[1380桌面事实区](evidence/disk-identity/desktop-1380-facts.png)、[125%原生窗口](evidence/disk-identity/desktop-125-heading-native.png)、[125%事实区](evidence/disk-identity/desktop-125-facts.png)。其余截图与worker日志保留于 `output/playwright/disk-identity/`。

本机最长真实型号已验证可读，尚无更长设备名可做真实样本；未以伪造快照补覆盖。上述未执行分支是当前证据的限制，不能由构建或边界fixture替代。
