# AA / AE 慢放实现对照（2026-11-08）

只读参考 AA 1.13.1，本次未替换 AE 的音频后端，也未进行同一音源的录音 A/B。

## 源码中确认的差异

- AA `ArcFMODAudioManager.UpdatePlaybackSpeed` 将 FMOD channel frequency 设为音源采样率 × 倍率，同时调用原生 `NStretch.SetPlaybackSpeed`。其 DSP read 回调按音频块调用 `NStretch.Process`，DSP reset 调用 `NStretch.Reset`。
- NStretch 位于 `NativeAudioHelper.dll`，有输入／输出延迟和 offset 接口；AA 用 offset × 倍率 ÷ 采样率计算 TimeOffset。C# 包装器与导出符号未标明底层算法，不能据此断言使用某个具体第三方库。
- AE `BassVoice` 用 `BASS_FX_TempoCreate` 建立变速流，`ArcAudioManager.PlayBackSpeed` 只设置属性 `0x10000`（tempo百分比），没有设置额外的时间拉伸质量／窗口参数。低倍速因此走另一套处理实现。
- AE BASS 输出初始化为 48000Hz，普通输出缓冲100ms、更新周期5ms，Windows另有设备缓冲设置。AA 使用 FMOD DSP 回调和自身输出配置；单看这些数值不能证明哪一端发生欠载。
- AE 播放同步只校正内部谱面时钟，不在每帧对音频做 seek；当前源码不支持“同步每帧重定位撕裂”这一解释。

## 结论与界限

两者并非相同的慢放实现：AA 是 FMOD 频率控制加 NStretch DSP／延迟补偿，AE 是 BASS_FX Tempo 默认处理路径。低倍速的拉伸算法与默认参数是优先排查项；延迟补偿主要影响时序对齐，不能单凭它解释音质改善。未做录音与运行时欠载分析前，不能将用户听到的严重撕裂唯一归因于算法，也不能认定加大缓冲就能解决。

本地提取代码：`E:\AffdataDev\Checks\AASlowAudio`；AA 原生 DLL 只读检查了导出／字符串，没有改参考包。
