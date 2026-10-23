# 手动构建 Windows 安装包

## 在 GitHub 上运行

打开[手动构建工作流](https://github.com/1340896123/blue-task-manager/actions/workflows/manual-windows-installer.yml)，点击 **Run workflow**，选择`main`并启动。手动运行会先构建并验证安装包，全部通过后创建Git tag与附带NSIS安装包的GitHub Release；普通提交不会自动运行。

`release_tag`留空时，默认生成`v<Host版本>-build.<运行序号>`，例如`v1.1.1-build.5`，并标为预览版。需要正式版时填写`v1.1.1`；也可填写`v1.1.1-preview.1`发布预览版。输入必须是合法的`v`前缀SemVer，基础版本须与Host项目一致，不能仅改变安装包标签。已有tag或Release会直接拒绝，不覆盖。含预发布标识的tag标为prerelease；正式版本标为稳定版。

运行成功后，从运行摘要的Release链接或[Releases页面](https://github.com/1340896123/blue-task-manager/releases)长期下载Setup.exe、SHA256SUMS、acceptance.json和acceptance.md。同时可从该次运行的 **Artifacts** 下载`BlueDomainTaskManager-v<版本>-win-x64-<运行序号>-<重试序号>`，应用截图单独提供；Artifacts保留14天。安装包包含运行时，安装后的应用按当前管理员权限合同运行。

## 本轮交付合同

GitHub Actions仅由`workflow_dispatch`手动触发Windows x64 Release安装包构建。安装器和权限以用户后续明确要求的[管理员与NSIS合同](ADMIN-INSTALLER.md)为准：使用标准NSIS/MUI2和.NET self-contained发布，包含.NET10、ASP.NET Core与完整`.electron`；使用者无需另装Node/npm或.NET。历史Release保持原义；当前便携包与安装包遵循管理员合同。

- Windows2025 hosted runner，固定SDK、Node与官方Actions commit SHA，npm/NuGet使用锁文件。
- 安装器版本取Host项目；可选`release_tag`仅选择匹配该基础版本的新Git标签。稳定安装身份、Program Files默认路径及`taskmanager-host`窗口身份。
- 默认不启动应用、不注册任务栏入口。提供开始菜单、卸载入口和桌面快捷方式。安装/卸载为计算机范围，正式应用必须经系统UAC并验证实际提升令牌。
- 构建和安装E2E全部通过后，独立发布job创建指向本次完整源提交SHA的新tag和Release，附加已验证Setup.exe、SHA256SUMS及真实验收报告。Actions artifacts额外保留14天。
- 删除文件前通过固定Host cleanup CLI复用CsWin32真实状态、精确所有权和版本检查，仅删除本目录自有Debugger，保留其他工具/值。清理失败停止卸载并保留文件；不通配删除用户文件。

## 成熟实现依据

- [NSIS官方3.12示例](https://github.com/nsis-dev/nsis/blob/v312/Examples/Modern%20UI/Basic.nsi)及[electron-builder标准安装模板](https://github.com/electron-userland/electron-builder/blob/v26.0.12/packages/app-builder-lib/templates/nsis/installer.nsi)：成熟的MUI2、计算机范围安装、标准卸载与快捷方式；具体安装器由管理员合同统一维护。
- [Windows2025 runner清单](https://github.com/actions/runner-images/blob/c87f6f636fffa43205bc05328efe9958af0f984f/images/windows/Windows2025-Readme.md)：未列出NSIS，因此工作流只在一次性runner通过Chocolatey安装固定3.12.0并核实编译器版本v3.12；不改动开发机。
- [成熟.NET工作流](https://github.com/martincostello/dotnet-bumper/blob/main/.github/workflows/build.yml)：Windows、固定SHA、最小权限和artifacts。
- [微软独立发布文档](https://learn.microsoft.com/en-us/dotnet/core/deploying/deploy-with-cli)及本地Electron.NET0.6.0 targets：`ElectronSkipExecCommands`仅跳过npm/npx，publish仍重排bin/app；最终必须恢复Host-first布局，带齐锁定Electron runtime。

## 验收要求

1. actionlint/PowerShell语法检查、真实Release/self-contained构建和NSIS编译。
2. 隔离数据目录、DOTNET_ROOT无效路径下真实启动，确认从程序目录加载coreclr及真实API，不把框架依赖包称为自包含。
3. 在一次性GitHub hosted Windows runner静默安装到带空格目录，核对payload/快捷方式，以安装目录执行真实Electron E2E；重装后仍可运行，关闭专用实例再卸载。
4. IFEO前后只读对照，不为测试改变本机全局入口；清理仅针对测试创建且保存身份的实例。
5. 提交推送后实际手动触发一次，核实构建/安装E2E/独立发布校验成功、tag指向该次源SHA、Release四个附件齐全；失败按日志修复。

管理员/安装生命周期适用R系列验收；测试不得通过伪造环境变量绕过本机UAC。CI在开始时检查实际Windows管理员令牌，缺少提升权限应失败而非宣称成功。构建job只有仓库读取权限；`needs: installer`的独立发布job才具备`contents: write`，不检出源码或运行包内脚本，仅下载本次已验证交付集。以上是实施合同，实际验证结果在后续记录。

发布job独立核对Setup的SHA256、单条SHA256SUMS、验收PASS、源提交SHA、安装器名称/版本/哈希及至少42项桌面断言。发布前再次检查目标不存在，再通过标准Git refs API原子创建固定SHA的新tag；冲突即失败。随后创建draft、上传并确认四个附件完整、再次确认tag指向，再公开Release。构建/安装验收失败不创建tag或Release；发布上传失败时保留本次tag和未公开draft供排查，重试须使用新的tag，不会将不完整draft公开。

## CI验收入口与下载内容

工作流直接调用`./scripts/build-installer.ps1 -MakeNsisPath <固定NSIS路径>`。安装验收入口为：

```powershell
./scripts/test-installer-ci.ps1 `
  -InstallerPath output/releases/BlueDomainTaskManager-v1.1.1-win-x64-setup.exe `
  -PayloadDirectory output/TaskManager-win-x64
```

此入口只允许在一次性GitHub-hosted Windows runner且真实提升的64位PowerShell中执行；不要在开发机伪造CI环境。它创建RUNNER_TEMP内独立且带空格的测试目录，依次验证静默安装、完整payload哈希和快捷方式/卸载记录、无全局.NET目录的真实Host/API/coreclr、现有42项Electron桌面验收、同路径重装后真实启动和卸载。测试自建note必须在卸载后保持原哈希，以证明没有通配删除用户文件。全程独立读取HKLM64 IFEO全键和子键，原始临时导出取哈希后立即删除，报告不保存注册表值、写令牌或全机快照。

`output/installer-ci/acceptance.json`和`acceptance.md`记录实际通过项、失败状态和限制；失败时只清理测试保存的PID+创建时间身份，保留失败安装目录供一次性runner诊断。`output/installer-ci/delivery`平铺Setup.exe、只引用这个EXE的`SHA256SUMS`及两份报告；应用截图单独上传。工作流要求完整交付集全部非空，不能把缺少报告的构建当作已验收安装包。
