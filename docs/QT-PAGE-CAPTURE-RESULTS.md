# 快速八页截图验收

日期：2026-11-04（Asia/Shanghai）。实现提交 `313ba65`，有效临时偏好及前置检查修复 `f98c227`。复现入口（PowerShell 7）：`./scripts/capture-qt-pages.ps1 -SkipBuild`。

| 最终轮次 / runId | 公开入口墙钟 | Python总耗时 | 原生驱动耗时 | 结果 |
|---|---:|---:|---:|---|
| 公开入口 / `20261104-063824-dfe3b503` | 9.053秒 | 8.016秒 | 5.530秒 | 退出0，24项检查，8张PNG |
| 禁用输入/OCR接口 / `20261104-063843-feca96db` | — | 9.250秒 | 5.133秒 | 退出0，24项检查，8张PNG |

每页WGC捕获及保存耗时如下；完整页面切换/确认耗时另见各run的 `native-results.json`。

| 页面 | 公开入口轮（秒） | guard轮（秒） |
|---|---:|---:|
| 进程 | 0.531 | 0.563 |
| 性能 | 0.469 | 0.437 |
| 应用历史记录 | 0.438 | 0.453 |
| 启动应用 | 0.485 | 0.437 |
| 用户 | 0.469 | 0.453 |
| 详细信息 | 0.500 | 0.453 |
| 服务 | 0.500 | 0.453 |
| 设置 | 0.500 | 0.422 |

两轮均实际运行生产Host→HTTP/JSON.NET/CsWin32，原生驱动验证Host/Qt精确归属、真实快照、有效偏好和1100×720客户区。导航调用真实Qt控件的公开 `click()`；标题、选中态及布局就绪后才请求WGC，PNG与metadata发布完成后才确认下一页，无每页固定4.2秒等待。

独立guard将 `Input`、`click_text`、`ocr_window`、`find_text`、`activate_window`、`load_bridge`、`ocr_frame`、`find_all_text` 全部替换为抛异常；实际调用0次。10项guard检查通过：新runId/新路径、旧PNG/metadata/本轮清单SHA256保持、8张不同PNG、最新根清单指向新轮、每图有metadata、Host退出0。验证代码保存在 `output/qt-page-capture/guard-run.py`，结果为 `guard-results.json`。

运行完成后才独立执行离线OCR和视觉核对：16张PNG标题均正确且各轮八页不同，没有人为 `preferencesRecovered` 错误条；全部WGC原图1102×752，真实客户区1100×720、96DPI/100%，metadata的Qt PID与原生记录一致。主屏2560×1440，未改变分辨率或拉伸图片。证据为 `offline-inspection.json` 及各run的 `headers-contact-sheet.png`。

两个Host均退出0，精确PID 68808/89900及87296/28908已全部退出，无残留；见 `process-cleanup.json`。全程使用独立临时profile，没有更改真实用户配置、IFEO、启动项或服务。

原始结果位于 `output/qt-page-capture/<runId>/` 的 `results.json`、`native-results.json`、`captures.json` 和八组PNG/metadata；根目录 `results.json` / `captures.json` 指向最新轮。公开入口退出码和墙钟见 `public-entry-exit.json`。

旧两轮 `20261104-063322-bf7a2b9e`、`20261104-063451-ecee2540` 仅保留诊断：临时偏好缺少必需字段导致恢复默认提示，修复后已重新完成上述两轮，旧图不计最终证据。性能页的真实PDH `0xC0000BBA` 状态照实保留；本轮只验证组件驱动截图，不重跑后台专项或物理输入E2E。
