# macOS 构建

当前 macOS 目标为 **Apple Silicon ARM64**。`AffStretch` 和部分 BASS 原生插件只提供 ARM64 切片，不能仅将 Unity 架构改成 Universal 就宣称支持 Intel Mac。

在装有项目对应 Unity 版本和 Mac Build Support 模块的环境中执行：

```sh
Unity -batchmode -nographics -quit -projectPath "$PWD" -executeMethod MacCiBuild.Build -logFile macos-build.log
```

产物为 `Build/macOS/AffdataEdit.app`。构建使用 LZ4HC 无损压缩和 StrictMode，有错误或警告即失败。`release.yml` 的 macOS 任务调用同一入口并独立发布；该工作流由 master 推送或手动触发，普通 PR 不会自动执行发布。

Windows 和 macOS 均保留四套原有预设、基础菱形及隐藏选项，额外菱形图片和配置已从产品源码移除，不再分发扩展包。其他轨道、判定线、两侧装饰、Combo 颜色及用户自定义背景保持可用。资源由 `Application.streamingAssetsPath` 定位，兼容 `.app` 内的 StreamingAssets 路径；构建完成后无需再裁剪或修改应用包。

加载页、皮肤选择及 songlist 导出使用共用代码。Windows 标题栏和最大化位置恢复使用平台条件编译，macOS 保留既有 Retina 分辨率处理。修改这些功能后，应分别验证 macOS 的窗口缩放、皮肤切换、预览时间选择和音频原生插件加载。

只在 Windows 上完成的编译、路径检查不能替代 macOS 原生构建、签名检查或实机运行。PR 中应分别注明已完成的 Windows 验证、macOS 条件编译检查和实际 macOS 验证结果。
