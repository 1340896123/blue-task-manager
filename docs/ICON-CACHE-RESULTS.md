# 图标缓存验收记录

日期：2026-11-04（Asia/Shanghai）。规格见 [ICON-CACHE.md](ICON-CACHE.md)。需求提交 `8e55838`，业务实现提交 `395fed9cb7e47151d8a3bfd6a60aaf9743ac8fe3`。

## 结果

| 检查 | 实际结果与证据（相对项目根目录） |
|---|---|
| IC01：Release 构建 | 独立检出 `395fed9` 的 .NET / Qt 构建退出 0；.NET 10.0.403、Qt 6.8.3、MSVC 19.52.37025.0。日志 `output/icon-cache/isolated-build.log`、`isolated-components-build.log`。 |
| IC02、IC03、IC05：缓存策略 | 26 项通过：512 项 LRU 溢出保留热项、最多两个并发、同键合并、静态 QIcon 共享、成功复核、空响应/无效 PNG 退避、失败保留旧图及恢复、来源命名空间和启动项命令/位置变化隔离。`output/icon-cache/isolated-cache-policy-results.json`。 |
| IC04：真实跨页复用 | 使用真实 Host 的 PID+创建身份，通过生产 HTTP PNG 接口加载图标；进程→详细信息→历史→进程的 `DecorationRole` 保持同一个 QIcon `cacheKey()`，缓存与并发均有界。新增检查未向模型插入测试图像。 |
| IC04、IC06：原生组件 | 68 项通过、20 张 WGC 图像，含八页、滚动位置恢复、真实采样间隔/暂停/冻结、实际连接和专用进程结束反馈。`output/icon-cache/components/native-results.json`、`captures.json`。组件方法检查与真实键鼠检查分别记录。 |
| IC06：实际键鼠 | 30 项通过、12 张 1920×1080 WGC 图像，含八页 Tab/Space 导航、搜索、新任务、取消及确认结束、成功反馈、窗口/Host 退出。`output/icon-cache/desktop/results.json`。鼠标/键盘使用 windows-computer-control 的 FYLHelper；主屏初始化为实际 2560×1440，测试窗口为 1920×1080、100%缩放，未改变系统分辨率。 |
| 真实后端回归 | 80 项通过，生产 CsWin32 采样、身份与权限保护、采样设置及测试专用进程启动/结束；Host 业务未改。`output/icon-cache/native-api.log`，执行退出 0。 |
| 桌面通用对比度 | 现有语义 Token 28 对通过；`output/icon-cache/contrast.json`。本次不改变图标颜色、尺寸或行高。 |
| 清理 | 测试 Host 正常退出，端口 10057 已关闭，本次隔离目录下无残留 Host/Qt；专用结束目标由测试验证身份后操作，未控制用户服务/启动配置。`output/icon-cache/verification.json`。 |

缓存为 Qt 会话内的已解码图标缓存，随窗口销毁释放；成功项可见时每60秒复核、失败后15秒重试。源文件修改时间、包身份与实际 Shell 取图继续由现有后端处理。没有新增接口、依赖或私有协议。

## 可复现命令

业务代码在独立检出中构建，避免混入原工作区同时进行的 Fluent 控件改动。以下命令在该检出根目录运行，Qt SDK 使用现有 `E:\taskManager\output\Qt`：

```powershell
& scripts/build.ps1 -QtPrefix 'E:\taskManager\output\Qt'
& scripts/import-msvc.ps1
cmake -S src/Takao.Qt -B output/qt-tests-build -G Ninja '-DCMAKE_PREFIX_PATH=E:\taskManager\output\Qt' -DCMAKE_BUILD_TYPE=Release -DTAKAO_BUILD_INTEGRATION_TESTS=ON
cmake --build output/qt-tests-build --target Takao.Qt.Integration Takao.Qt.IconCache.Tests --parallel 4
$env:PATH = 'E:\taskManager\output\Qt\bin;' + $env:PATH
& output/qt-tests-build/Takao.Qt.IconCache.Tests.exe
& scripts/test-qt-components.ps1 -QtPrefix 'E:\taskManager\output\Qt' -SkipBuild -OutputDirectory 'E:\taskManager\output\icon-cache\components'
```

组件运行前应用本次新增的真实缓存探针，补丁保存在 `output/icon-cache/live-integration.patch`，已回带到 `tests/qt-integration.cpp`。首次探针未恢复临时搜索条件，导致后续启动页断言失败；恢复查询后重跑完整流程，最终68项通过。首次失败保存在 `output/icon-cache/components-probe-first-attempt.json`，不计作最终通过。

后端回归在原工作区管理员测试进程执行 `node tests/native-api.mjs`。实际键鼠使用原工作区已有的待提交测试脚本，在原工作区执行：

```powershell
& scripts/test-qt-desktop.ps1 -ReleaseDirectory 'C:\Users\jieok\.codex\worktrees\icon-cache-validation\taskManager\output\Takao-win-x64' -OutputDirectory 'E:\taskManager\output\icon-cache\desktop'
& 'C:\Users\jieok\.codex\skills\windows-computer-control\.venv\Scripts\python.exe' tests/qt-contrast.py --output output/icon-cache/contrast.json
```

该物理输入脚本原有改动未纳入本任务提交。其 SHA256 为 `B83F90D99A5C9F243D492AAB48A6D8478453D40A5EECD53E587BBAA484E1725F`，启动脚本为 `42B5C569E1E8C80070F3D3C525F746D711689276520DAFF064BCFB2EA3D4C129`。验证后的可运行目录保存在 `output/icon-cache/tested-app`，其中 `Takao.Qt.exe` SHA256 为 `402996CE7ABF981CD112BBB5EF6CED252A97133411A5CC339EC3C3B88200EB82`。验证检出保留：应用归档接口返回 `The task's primary checkout cannot be removed.`，未强制删除；最终代码、证据与构建目录均已保存到原工作区 `E:\taskManager`。另行检出上述业务提交、应用探针补丁并运行命令可复核。

## 范围与限制

本轮未执行真实文件替换、打包应用包更新或网络断开注入；失败/恢复与60秒/15秒边界由单调可控时钟及真实 PNG 的缓存策略测试验证，不能称为这些 Windows 来源变化的 E2E。实际键鼠未启用 `-Extended` 或 `-NativeFileDialogs`；暂停/冻结/间隔等由原生组件检查覆盖。不测其他显示尺寸、不执行安装/升级、系统入口写入或用户服务控制，也不报告未测量的性能提升比例。
