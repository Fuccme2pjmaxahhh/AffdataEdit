# Windows 本地打包

双击项目根目录的 `Build-Windows.cmd`，或运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "E:\AffdataDev\AffdataEdit-latest\Tools\Build-Windows.ps1"
```

脚本自动读取项目需要的 Unity 版本，只同步变更的源码，复用 `E:\AffdataDev\BuildWorkspace` 的 Library 和构建缓存。成功后更新 `C:\AffdataEdit_custom`，保留已有设置、谱面和背景文件。

如果程序正在运行，脚本会等待最多三分钟让你保存并关闭，不会强制结束程序。构建失败不会发布旧产物。日志在 `E:\AffdataDev\BuildLogs`。

Unity 编辑器可以打开源码项目，但不能同时打开构建副本 `BuildWorkspace`。首次构建、Unity／依赖／渲染资源有更新时，仍需要较长的导入与编译时间。
