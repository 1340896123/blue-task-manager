# 字体资源

Takao 的 MewUI 桌面界面使用项目内置的 **HarmonyOS Sans SC**。字体源文件位于
`assets/fonts/`，其中 `HarmonyOS_Sans_SC_Regular.ttf` 是桌面界面使用的简体中文字体，
其余 HarmonyOS Sans 字重也会随发布包携带，便于后续扩展。源包
`assets/fonts/HarmonyOS-Sans.zip` 和同目录的 `HarmonyOS-Sans-LICENSE.txt` 也会保留。

## 运行时

`Takao.Host` 启动桌面模式时，通过 MewUI 的 `FontResources.Register` 注册应用目录下的
`fonts/HarmonyOS_Sans_SC_Regular.ttf`，再把注册得到的字体设置为默认主题字体。主窗口页容器
也会显式继承这个字体，因此总览、进程、性能、历史记录、启动应用、用户、详细信息、服务、
设置和系统信息页面的正文、表格、按钮、输入框与弹窗都使用项目字体。字体不依赖目标电脑
是否安装 HarmonyOS Sans，也不会修改 Windows 的系统字体目录。窗口按钮和导航图标仍使用
`Segoe Fluent Icons`，因为这些控件显示的是 Segoe 专用图标字形。

缺少字体文件会在桌面启动阶段明确报错，避免发布包静默回退到系统字体。无窗口的
`--headless` 模式不初始化 MewUI，也不需要读取字体。

## 构建与安装

`src/Takao.Host/Takao.Host.csproj` 将 `assets/fonts/*.ttf` 声明为发布内容，并固定输出路径为
`fonts/`。使用 `dotnet publish` 生成安装负载时，必须把发布目录整体交给安装器，保留该相对
路径；安装后的目录结构应包含：

```text
Takao.Host.exe
fonts/
  HarmonyOS_Sans_SC_Regular.ttf
  HarmonyOS_Sans_Regular.ttf
  HarmonyOS_Sans_Bold.ttf
  ...
licenses/
  HarmonyOS-Sans-LICENSE.txt
```

安装器不需要把字体注册为全局 Windows 字体，只需随应用文件复制。这样卸载不会触碰其他
应用或用户的字体设置，便携运行和正式安装也保持相同的字体行为。

## 维护

如果从 `HarmonyOS-Sans.zip` 更新字体，优先替换 `HarmonyOS_Sans_SC_Regular.ttf`，确认
`dotnet publish` 输出仍包含 `fonts/` 文件，再更新本页的源包说明和许可证文件。不要把
`__MACOSX` 元数据或 `.DS_Store` 文件放入发布目录。
