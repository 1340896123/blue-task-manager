# 1.1最终验收修复阶段

本阶段根据真实桌面与满120点性能验收修复问题；完整同版本回归、最终性能与portable产物以ACCEPTANCE-RESULTS为准，不用本交接替代最终矩阵。

- CompanyName版本资源、当前账户、Run/RunOnce全部统一CsWin32生成接口，保留原慢元数据/文件mtime缓存。版本资源最多4MiB并验证Translation/value实际缓冲边界；注册表只读同32/64视图，单值64KiB、总4096项/4MiB，超大值跳过并解释。独立Windows/托管消费者对照真实系统exe、自有exe、账户/进程Owner与25个真实启动值通过5断言。
- 服务/启动/会话首读与15秒更新移到单个合并后台任务；首读时间null/loading，慢源失败局部保留旧来源、5秒退避，暂停不发布结果，关闭最多等2秒。基本采样仍串行，暂停在采集期间获准时丢弃该在途帧，避免暂停响应之后再增长历史。
- Intl.Collator与Intl.DateTimeFormat模块级复用，排序语义不变；保留虚拟行与不透明ETag/304。初次完整桌面测量暴露采样p95616ms/交互117ms；修复后同脚本满120点、32.8秒测得55.8ms/48.5ms。条件不同，仅作工程问题定位；最后代码后还要重新测并按预算标结果。
- 文件位置改用CsWin32 ShellExecuteEx、固定Windows explorer.exe的标准/select与后端重验路径。在当地两种PIDL调用返回S_OK但无窗口，标准Explorer入口已由原生UI Automation确认实际选中文件；Windows属性仍使用标准Shell properties。不会执行renderer任意命令。
- 置顶在当地官方框架setter/readback不生效，改用官方GetNativeWindowHandle+CsWin32 SetWindowPos/实际扩展样式，状态GET与启动恢复同源。缩放GET使用WebContents实际读回。
- 窗口尺寸固定为正常客户区DIP，GetContentBounds保存、UseContentSize+SetContentSize恢复并读回；绕开官方0.6.0的旧Windows初始14×7补偿，不扣固定边框。原生ClientRect证明960×640客户区跨重启保持不变；同窗口125%时CSS768×512、DPR1.25，外框976×648为当地隐形边框口径。
- TCP状态映射中文并保留raw title；效率与结束树说明改为用户可理解的实际影响。主控件、菜单、详情与慢源初读状态继续只使用MASTER Token。

独立原生探针按确切PID+创建时间/HWND核验，使用生成的CsWin32/UI Automation；PrintWindow只截图目标窗口。选择器真实选择、取消与打开超过9秒、带逗号路径/等号文件名CSV、实际文件位置/属性、置顶、960客户区+125%、隐藏>=5秒轮询、恢复立即补帧、跨重启尺寸/缩放/置顶、关闭应用后UI Run创建的任务仍存活，最新22条通过。仅清理测试自有进程和新建确认为测试的Shell窗口；保留原有Explorer。

Playwright默认CDP会启用焦点模拟。交互结束后先断开CDP并确认真实窗口仍活，再用官方noDefaults:true重连测原生最小化/visibility，避免把模拟焦点误判为产品不节流。隐藏状态仍由Windows最小化与document.hidden共同确认，未替换浏览器属性或业务API。

服务成功启停/重启、超时与部分生效分支仍受本机标准用户SC_MANAGER_CREATE_SERVICE错误5限制；禁止触碰用户业务服务补覆盖率。逐进程网络吞吐仍明确未接入；连接、进程I/O不冒充吞吐或物理磁盘数据。
