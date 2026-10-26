# 更新安装器交接机制验收

日期：2026-10-26。对应 [GitHub 更新合同](GITHUB-UPDATES.md) 的 U01、U06、U07 部分。此阶段实现固定 NSIS 更新参数、受限等待、结果记录、成功重启和 CI 发布身份；没有更新用户正在运行的安装版，没有发布 GitHub Release。

## 实际通过

- 标准 NSIS 3.12 编译器对最新生产默认分支使用小型清单完成编译，对明确编译期测试分支也完成编译；生产默认产品键、产品 ID、快捷方式不随运行参数改变。测试分支必须显式定义 `TASKMANAGER_INSTALLER_TEST` 及专用产品身份，并完全省略公共快捷方式。
- [独立提升机制测试](evidence/update-installer-mechanism/results.json) **24 项通过**。独立产品键和新建专用目录实际安装了小型测试 Host：无效尝试/无安装拒绝、损坏标记失败记录、普通安装立即拒绝真实运行中的测试 Host、更新等待旧 Host 自然退出并完成后重启、60 秒超时返回失败且不重启。测试专用安装随后卸载，专用键和目录均已独立确认消失；真实产品卸载键与完整 taskmgr IFEO 指纹前后一致。
- PowerShell Parser 检查、actionlint 1.7.12 和 `git diff --check` 通过。只读 PE 元数据读取器在带 `+fixture` 的明确测试程序集上保留完整 ReleaseTag/SourceCommit；错误基础版本、请求身份与实际 payload 不一致的两次打包请求均在写产物前拒绝。自定义 payload 未带 SkipBuild、输出目录位于 payload 内部的两项路径保护也实际拒绝。
- CI 预先设置已验证 `TASKMANAGER_RELEASE_TAG` / `TASKMANAGER_SOURCE_COMMIT`，实际 build 校验程序集元数据，NSIS 将身份写入版本资源、安装标记和卸载注册表，安装验收从实际 `/api/updates` 再读回。没有运行 CI 或把 YAML/语法通过当作实际安装身份通过。

## 边界与发现

隔离机制测试使用明确的小型测试程序集和编译期专用产品身份，只证明 NSIS 交接与真实操作系统文件锁行为。它不证明官方 GitHub 下载、真实更高版本替换、C# `now` / `nextStart` 到 NSIS 的整条链路，也不替代正式 Electron 重启验收。

现有 `output/nsis` 生成清单来自旧产物。用该清单编译现有完整 payload 成功，但独立 7-Zip 提取核验正确发现其缺少后来加入的 `restore-windows-task-manager.cmd`，所以该旧清单编译物**未通过完整交付验证**。最终新源码安装包必须从新完整 payload 重新生成清单并验证。新增可选独立 payload/output 参数便于完成该检查而不覆盖旧同版本 release。

更新结果只在固定产品记录可确认后写入。非法参数、缺失/不属于本产品的记录，以及产品身份核验前的平台失败保持记录不动；这种启动前失败由后端的“未确认成功”兜底报告，不能虚构具体安装结果。外部强杀安装器也不能保证回调落盘；后端的一次自动尝试保护负责避免循环。

当前 workflow 允许合法 SemVer `+metadata`。PE 身份读取和打包校验已保留它；后端 `TryTag` 的 NuGet 规范化比对需由主代理同步复核，不能把本阶段通过当作该后端边界已修复。

## 可复现命令

使用实际提升的 PowerShell 7 运行专用机制测试：

```powershell
pwsh -NoProfile -File scripts/test-update-installer.ps1
```

脚本在 `output/update-installer-validation/harness-<guid>` 中生成明确测试 fixture，强制专用编译期注册表键，拒绝既有目录与链接，记录结果到该目录的 `results.json`。不会设定 GitHub runner 环境变量，也不会调用受 hosted-only 门槛保护的 `test-installer-ci.ps1`。

对独立完整新产物编译和静态提取时可用：

```powershell
pwsh -NoProfile -File scripts/build-installer.ps1 -SkipBuild -PayloadDirectory E:\taskManager\output\updates\TaskManager-win-x64 -OutputDirectory E:\taskManager\output\updates\releases -MakeNsisPath C:\nsis\makensis.exe
pwsh -NoProfile -File scripts/verify-installer.ps1 -InstallerPath E:\taskManager\output\updates\releases\BlueDomainTaskManager-v1.1.1-win-x64-setup.exe -PayloadDirectory E:\taskManager\output\updates\TaskManager-win-x64
```

以上第二组命令展示独立路径的复现方式，目录需先由当前源码发布；本阶段没有声称该新完整产物已构建。静态核验器只提取和读取，不运行安装/卸载。
