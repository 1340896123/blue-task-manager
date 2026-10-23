# 手动构建 Windows 安装包

## 本轮交付合同

GitHub Actions仅由`workflow_dispatch`手动触发Windows x64 Release安装包构建。使用标准Inno Setup和.NET self-contained发布，包含.NET10、ASP.NET Core与完整`.electron`；使用者无需另装Node/npm或.NET。现有framework-dependent便携ZIP和旧Release保持原义。

- Windows2025 hosted runner，固定SDK、Node与官方Actions commit SHA，npm/NuGet使用锁文件。
- 版本取Host项目，不提供只改变安装器标签的版本输入。稳定安装身份、Program Files默认路径及`taskmanager-host`窗口身份。
- 默认不启动应用、不注册任务栏入口。提供开始菜单、卸载入口和可选桌面快捷方式。安装/卸载为计算机范围，普通运行不要求管理员。
- 本次Actions artifacts上传Setup.exe、SHA256SUMS和真实验收报告，保留14天；不自动创建标签或发布Release。
- 删除文件前通过固定Host cleanup CLI复用CsWin32真实状态、精确所有权和版本检查，仅删除本目录自有Debugger，保留其他工具/值。清理失败停止卸载并保留文件；不通配删除用户文件。

## 成熟实现依据

- [Inno Setup官方6.7.1示例](https://github.com/jrsoftware/issrc/tree/is-6_7_1/Examples)：64位、稳定AppId、标准文件/快捷方式/卸载。
- [官方卸载事件源码](https://github.com/jrsoftware/issrc/blob/is-6_7_1/Projects/Src/Setup.Uninstall.pas)：`usUninstall`在用户确认后、`PerformUninstall`前执行，异常会阻止删文件。关键清理不能放到后续吞异常的事件。
- [Windows2025 runner清单](https://github.com/actions/runner-images/blob/c87f6f636fffa43205bc05328efe9958af0f984f/images/windows/Windows2025-Readme.md)：已核实预装InnoSetup6.7.1，工作流应检查工具版本和存在性。
- [成熟.NET工作流](https://github.com/martincostello/dotnet-bumper/blob/main/.github/workflows/build.yml)：Windows、固定SHA、最小权限和artifacts。
- [微软独立发布文档](https://learn.microsoft.com/en-us/dotnet/core/deploying/deploy-with-cli)及本地Electron.NET0.6.0 targets：`ElectronSkipExecCommands`仅跳过npm/npx，publish仍重排bin/app；最终必须恢复Host-first布局，带齐锁定Electron runtime。

## 验收要求

1. actionlint/PowerShell语法检查、真实Release/self-contained构建和ISCC编译。
2. 隔离数据目录、DOTNET_ROOT无效路径下真实启动，确认从程序目录加载coreclr及真实API，不把框架依赖包称为自包含。
3. 在一次性GitHub hosted Windows runner静默安装到带空格目录，核对payload/快捷方式，以安装目录执行真实Electron E2E；重装后仍可运行，关闭专用实例再卸载。
4. IFEO前后只读对照，不为测试改变本机全局入口；清理仅针对测试创建且保存身份的实例。
5. 提交推送后实际手动触发一次，核实运行成功及三个附件；失败按日志修复。

以上是实施合同，实际验证结果在后续记录。
