# 字体资源

Takao 的 MewUI 桌面界面使用项目内置的 **HarmonyOS Sans SC**。字体源包位于
`assets/fonts/HarmonyOS-Sans.zip`，发布所需的简体中文常规字重已从该包整理为
`assets/fonts/HarmonyOS_Sans_SC_Regular.ttf`，并保留了同目录的
`HarmonyOS-Sans-LICENSE.txt`。

## 运行时

`Takao.Host` 启动桌面模式时，通过 MewUI 的 `FontResources.Register` 注册应用目录下的
`fonts/HarmonyOS_Sans_SC_Regular.ttf`，再把注册得到的字体设置为默认主题字体。因此字体
不依赖目标电脑是否安装 HarmonyOS Sans，也不会修改 Windows 的系统字体目录。窗口按钮和
导航图标仍使用 `Segoe Fluent Icons`，因为这些控件显示的是 Segoe 专用图标字形。

缺少字体文件会在桌面启动阶段明确报错，避免发布包静默回退到系统字体。无窗口的
`--headless` 模式不初始化 MewUI，也不需要读取字体。

## 构建与安装

`src/Takao.Host/Takao.Host.csproj` 将字体声明为发布内容，并固定输出路径为
`fonts/HarmonyOS_Sans_SC_Regular.ttf`。使用 `dotnet publish` 生成安装负载时，必须把发布
目录整体交给安装器，保留该相对路径；安装后的目录结构应包含：

```text
Takao.Host.exe
fonts/
  HarmonyOS_Sans_SC_Regular.ttf
licenses/
  HarmonyOS-Sans-LICENSE.txt
```

安装器不需要把字体注册为全局 Windows 字体，只需随应用文件复制。这样卸载不会触碰其他
应用或用户的字体设置，便携运行和正式安装也保持相同的字体行为。

## 维护

如果从 `HarmonyOS-Sans.zip` 更新字体，优先替换 `HarmonyOS_Sans_SC_Regular.ttf`，确认
`dotnet publish` 输出仍包含 `fonts/` 文件，再更新本页的源包说明和许可证文件。不要把
`__MACOSX` 元数据或 `.DS_Store` 文件放入发布目录。
