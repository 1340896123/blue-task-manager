# 产品图标

2026-10-23：按用户要求统一蓝域任务管理器的产品标识。单一手工矢量母版为 `assets/branding/task-manager.svg`：透明外缘、Windows 蓝色圆角底板、白色显示器与三条资源柱。颜色直接对应 MASTER 的 `--color-accent`（#0067C0）与 `--color-on-accent`（#FFFFFF）；标题栏继续使用既有 `--space-7` 尺寸。导航/操作图标仍使用原有 Lucide，业务采集与进程操作不变。

## 资产与覆盖

| 位置 | 实际使用 |
|---|---|
| 母版 | `assets/branding/task-manager.svg` |
| PNG | `assets/branding/task-manager-256.png`、`task-manager-512.png`，RGBA 透明 |
| Windows ICO | `assets/branding/task-manager.ico`，含 16/24/32/48/64/128/256 像素七帧 |
| React 品牌 | 标题区 `img.brand-mark` 读取 `/icon.svg`；相邻已有产品文字，装饰图片使用空 alt |
| 浏览器标签 | SVG favicon + 标准 `/favicon.ico` 回退；另提供 256/512 PNG |
| .NET EXE | 标准 MSBuild `ApplicationIcon` 嵌入 Host apphost 的 PE 图标资源 |
| Electron 窗口/任务栏 | `ElectronIcon` 复制 ICO 到 `.electron`，官方 `BrowserWindowOptions.Icon` 指定绝对路径 |
| Windows 应用身份 | 沿用原默认派生值 `taskmanager-host`，显式固定 `ElectronPackageId`、builder `appId` 与官方 `app.setAppUserModelId` |

框架和 Electron vendor EXE 未做二进制修改。原有第三方依赖许可文件保留；本次图标变更不为整个项目新增许可授权。

## 重建

已有生成资产进入 Git，普通应用构建不要求 Python。需要修改母版后重新生成时，在根目录运行：

```powershell
npm ci
npx playwright install chromium
python -m pip install Pillow==12.3.0
npm run icons:generate
```

生成脚本先校验母版颜色与 MASTER 一致，再用已锁定 Playwright 的 Chromium 将同一 SVG 渲染为透明 512 PNG，Pillow 派生 256 PNG 与七帧 ICO，同时同步 web public 副本。不会调用图片生成模型、下载图标或引入另一图标库。当前生成环境 Pillow 12.3.0。

正式便携版本按 Web → Host 顺序构建：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -SkipRestore
npm run test:icon
```

产物：`output/TaskManager-win-x64/start.cmd`。图标只通过标准资源与官方 API 接入，运行前提沿用 README。

## 真实验收

本次生产 Web 类型检查/构建、Release Host/测试探针构建、便携复制均通过，0 个 C# 警告/错误。`npm run test:icon` 在真实 Electron.NET 窗口完成 16 个断言：

- ICO 七尺寸与便携 ICO 哈希一致；SVG/ICO/PNG 均由本机主机返回 HTTP 200 和图片类型；React `img.decode()` 成功。
- 独立 CsWin32 `ExtractIconExW` 从最终 Host EXE 提取大/小图标；`WM_GETICON` 从本次测试拥有且重验 PID+创建时间的真实窗口读取大/小图标。两者均读出蓝底、白色符号与透明角，32px/16px 图像与母版相符。
- renderer 继续隔离 Node；真实窗口关闭后本次 Host/Electron 进程和测试端口均释放。没有操作任何真实业务进程。

本地证据：`output/product-icon/results.json`、`product-desktop.png`、`product-native-window.png`、`product-exe-large.png`、`product-exe-small.png`、`product-window-large.png`、`product-window-small.png`。独立原生窗口截图只捕获目标应用，不采集整个桌面。这里验证的是窗口/任务栏提供的原生图标及 EXE 图标资源，未改写用户已经固定在任务栏上的旧快捷方式或 Windows 图标缓存。

接入依据：本机 Electron 45 类型定义中的 [BrowserWindow icon](https://www.electronjs.org/docs/latest/api/browser-window) 与 [app.setAppUserModelId](https://www.electronjs.org/docs/latest/api/app#appsetappusermodelidid-windows)，以及 Electron.NET 0.6.0 的 `ElectronIcon` / `ElectronPackageId` 官方构建属性。

包含任务栏设置的最终业务版本已独立重跑16项。公开归档：[图标结果](evidence/product-icon-results.json)、[实际应用](evidence/product-desktop.png)；最终发布状态见[验收记录](ACCEPTANCE-RESULTS.md)。
