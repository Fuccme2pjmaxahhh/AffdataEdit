# Windows 本地打包

双击项目根目录的 `Build-Windows.cmd`，或运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "E:\AffdataDev\AffdataEdit-latest\Tools\Build-Windows.ps1"
```

脚本自动读取项目需要的 Unity 版本，只同步变更的源码，复用 `E:\AffdataDev\BuildWorkspace` 的 Library 和构建缓存。成功后更新 `C:\AffdataEdit_custom`，保留已有设置、谱面和背景文件。

Unity 以 `-batchmode -nographics` 运行，跳过构建时不需要的编辑器图形设备初始化，避免载入编辑器 GUI 材质／资源时的原生崩溃。这不改变 Windows 播放器的图形 API 或频谱渲染设置；C#、Shader 与严格构建检查照常执行。

按已有授权，发布时脚本会直接关闭 `C:\AffdataEdit_custom\AffdataEdit.exe`，不保存；只匹配此完整路径，不影响其他程序。构建失败不会发布旧产物。日志在 `E:\AffdataDev\BuildLogs`。

构建失败时，终端会直接显示日志中最先出现的最多8条 C#、Shader 或致命错误，再显示退出码和完整日志路径，便于定位问题。

发布后的 SHA256 校验通过 .NET 完成，不依赖启动终端是否能自动加载 PowerShell 的 `Get-FileHash` 命令。

Unity 编辑器可以打开源码项目，但不能同时打开构建副本 `BuildWorkspace`。首次构建、Unity／依赖／渲染资源有更新时，仍需要较长的导入与编译时间。

Windows 和 macOS 均使用 LZ4HC 无损压缩，保留原有四套完整预设、基础菱形及隐藏选项。额外菱形图片和配置已从产品源码移除，不再生成扩展资源包。其他轨道、判定线、两侧装饰、Combo 颜色及用户自定义背景保持可用。

macOS 架构和构建验证说明见 [macOS 构建](BUILD-MACOS.md)。

更新本地旧版时，脚本将新版不再使用的已知 Unity 生成数据文件移到项目外层 `RetiredPlayerFiles/时间戳` 备份，避免压缩后的 `data.unity3d` 与旧版松散数据重复占用空间。设置、谱面和背景会保留，安装目录仍不用 `/MIR`。
