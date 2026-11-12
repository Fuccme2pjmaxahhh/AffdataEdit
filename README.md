# AffdataEdit

面向 AffdataPlay 与 AFF 谱面的桌面编辑器，提供谱面编辑、实时预览和 AffdataNet 云端协作。

A desktop editor for AFF charts and AffdataPlay, with chart editing, live preview, and AffdataNet cloud integration.

[下载更新器 / Download updater](https://github.com/anqtal/AffdataEdit-Updater/releases/latest) · [使用文档 / Documentation](#documentation) · [源码开发 / Development](#development) · [语言支持规划 / Localization plan](Documentation/Localization.md)

## 功能 / Features

- **谱面编辑与预览**：编辑 Tap、Hold、Arc、ArcTap 和 Slide，支持时间组、网格吸附、批量操作、撤销与重做，以及 AUTO 预览。

  **Chart editing and preview:** Tap, Hold, Arc, ArcTap, and Slide editing with timing groups, grid snapping, batch operations, undo/redo, and AUTO preview.

- **AFF 扩展**：支持 Slide、Arc 平滑度、Designant、小数轨道和自定义 Trace 颜色。扩展语法需要播放器提供相应支持。

  **AFF extensions:** Slide regions, Arc smoothness, Designant, fractional lanes, and custom Trace colors. These extensions require a compatible player.

- **AffdataNet 云端谱面**：登录后打开云端谱面，将修改保存回云端，并可开启实时保存。

  **AffdataNet cloud charts:** Sign in to open cloud charts, upload edits, and optionally enable automatic cloud saving.

- **音频与录制**：通过 BASS / BASS_FX 提供保持音高的播放速度调整，支持音频频谱显示和 OBS WebSocket 录制联动。

  **Audio and recording:** Pitch-preserving playback speed through BASS / BASS_FX, an audio spectrum display, and recording integration with OBS WebSocket.

- **界面与皮肤**：提供明暗主题、设置侧栏、内置皮肤和外部皮肤覆盖。

  **Interface and skins:** Light/dark themes, a settings drawer, a built-in skin, and external skin overrides.

## 安装与更新 / Installation and updates

当前发布流程面向 Windows x64 和 macOS；Linux 尚无对应的自动构建与发布流程。

The current release workflow targets Windows x64 and macOS. Linux does not currently have an automated build and release workflow.

1. 从 [AffdataEdit-Updater 的最新 Release](https://github.com/anqtal/AffdataEdit-Updater/releases/latest) 下载对应平台的更新器：Windows 使用 `AffdataEdit-Updater.exe`，macOS 使用 `AffdataEdit-Updater-macOS.zip`。

   Download the updater for your platform from the [latest AffdataEdit-Updater release](https://github.com/anqtal/AffdataEdit-Updater/releases/latest): `AffdataEdit-Updater.exe` for Windows or `AffdataEdit-Updater-macOS.zip` for macOS.

2. 运行更新器，按提示安装并启动编辑器。Windows 默认安装到更新器所在目录；macOS 解压后运行更新器，默认安装到 `/Applications/AffdataEdit/`。macOS 首次打开未签名更新器时，可能需要在“系统设置 → 隐私与安全性”中允许打开。

   Run the updater and follow its prompts to install and launch the editor. Windows installs beside the updater; on macOS, extract and run it to install into `/Applications/AffdataEdit/` by default. On first launch, macOS may require allowing the unsigned updater in System Settings → Privacy & Security.

3. 后续启动时，编辑器会检查更新；提示新版本后选择“立即更新”。更新器按文件下载变化内容，目前只提供最新版本。

   The editor checks for updates at startup. Choose “立即更新” (Update now) when prompted. The updater downloads changed files; only the latest version is offered.

编辑器构建通过更新服务分发，更新器本身通过上述 GitHub Release 获取。详见[构建与更新说明](Documentation/Updates.md)。

Editor builds are distributed through the update service; the updater itself is available through the GitHub release above. See [build and update documentation](Documentation/Updates.md).

## 开始编辑 / Getting started

### 本地谱面 / Local charts

在“打开谱面”中选择“本地谱面”，选择乐曲文件夹。一个常见的目录结构如下；封面与各难度谱面按需提供，当前难度缺少 AFF 文件时会创建空白谱面。

Choose “本地谱面” (Local chart) in the open dialog and select a song folder. A typical layout is shown below. Cover art and difficulty files are optional; a missing AFF file for the selected difficulty is initialized as a blank chart.

```text
MySong/
├── base.ogg
├── base.jpg
├── 0.aff
├── 1.aff
├── 2.aff
├── 3.aff
└── 4.aff
```

音频可使用 `base.ogg`、`base.mp3`、`base.wav` 或 `base.opus`；也可用对应难度编号命名的音频覆盖，例如 `2.ogg`。`0` 至 `4` 依次对应 Past、Present、Future、Beyond 和 Eternal。

Audio can be `base.ogg`, `base.mp3`, `base.wav`, or `base.opus`. A difficulty-specific file such as `2.ogg` overrides the shared audio. Difficulty numbers `0` through `4` correspond to Past, Present, Future, Beyond, and Eternal.

保存会更新当前难度的 AFF，并将原文件备份到 `Arcade/Backup/`；编辑期间的自动保存位于 `Arcade/Autosave/`。工程元信息存储在 `Arcade/Project.arcade`。

Saving updates the current difficulty’s AFF and backs up the previous file to `Arcade/Backup/`. Autosaves made during editing are stored in `Arcade/Autosave/`; project metadata is stored in `Arcade/Project.arcade`.

### 云端谱面 / Cloud charts

登录 AffdataNet 后，在“打开谱面”中选择“云端谱面”。云端保存按钮将当前难度上传；实时保存需要单独开启。已冻结的 Ranked 难度不允许上传修改。

Sign in to AffdataNet and choose “云端谱面” (Cloud chart). The cloud save button uploads the current difficulty; automatic cloud saving is enabled separately. Frozen Ranked difficulties cannot be overwritten.

## 语言支持 / Localization

**规划中，尚未实现。** 当前界面主要使用简体中文，尚无统一的 i18n 系统或语言切换入口。计划默认内置以下三种语言：

**Planned, not yet implemented.** The interface currently uses mainly Simplified Chinese, with no shared i18n system or language selector. The planned built-in languages are:

| 语言 / Language | 标识 / Locale |
| --- | --- |
| 简体中文 / Simplified Chinese | `zh-Hans` |
| 繁體中文 / Traditional Chinese | `zh-Hant` |
| 英语 / English | `en` |

建议基于 Unity 官方 Localization 包逐步接入现有 uGUI 界面。方案比较、语言选择、字体和迁移步骤见 [i18n 接入规划](Documentation/Localization.md)。README 的双语说明不代表应用已经支持语言切换。

The proposed approach is to integrate Unity’s official Localization package into the existing uGUI interface. See the [i18n integration plan](Documentation/Localization.md) for alternatives, locale selection, fonts, and migration steps. This bilingual README does not imply that language switching is already available in the app.

<a id="development"></a>

## 源码开发 / Development

### 环境与依赖 / Environment and dependencies

使用 Unity 6.6，具体编辑器版本以 [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt) 为准，并安装所需平台的 Build Support。UPM 依赖由 [manifest.json](Packages/manifest.json) 管理，包括 URP、Input System、UniTask 和 LitMotion。

Use Unity 6.6, matching the exact editor version in [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt), with Build Support for your target platform. [manifest.json](Packages/manifest.json) manages the UPM dependencies, including URP, Input System, UniTask, and LitMotion.

**构建还需要 `Affdata.Core`。** 它是 AffdataEdit 与 AffdataPlay 共用的 UPM 库，当前通过 `file:../../Affdata.Core` 引用，并位于单独的私有仓库中。需要先取得访问权限；仅克隆本仓库无法完成依赖解析。

**Building also requires `Affdata.Core`.** This shared UPM library for AffdataEdit and AffdataPlay is referenced through `file:../../Affdata.Core` and currently lives in a separate private repository. Access is required; cloning this repository alone is insufficient to resolve the dependencies.

两个仓库应放在同一父目录下：

Place both repositories under the same parent directory:

```text
workspace/
├── AffdataEdit/
│   ├── Assets/
│   ├── Packages/
│   └── ProjectSettings/
└── Affdata.Core/
    ├── package.json
    └── Runtime/
```

取得权限后，可在父目录执行：

Once access is granted, run these commands from the parent directory:

```sh
git clone https://github.com/anqtal/AffdataEdit.git
git clone https://github.com/anqtal/Affdata.Core.git
```

用 Unity Hub 打开 `AffdataEdit`，等待依赖解析和脚本编译完成，然后打开 `Assets/_Scenes/ArcEditor.unity` 并进入 Play Mode。

Open `AffdataEdit` through Unity Hub, wait for package resolution and script compilation, then open `Assets/_Scenes/ArcEditor.unity` and enter Play Mode.

### 构建 / Building

可在 Unity Build Profiles 中选择目标平台并包含主场景 `ArcEditor`，或通过以下编辑器构建方法运行：

Select a target platform in Unity Build Profiles and include the `ArcEditor` scene, or use these editor build methods:

| 平台 / Platform | 构建方法 / Build method | 输出 / Output |
| --- | --- | --- |
| Windows x64 | `WindowsCiBuild.Build` | `Build/Windows/AffdataEdit.exe` |
| macOS | `MacCiBuild.Build` | `Build/macOS/AffdataEdit.app` |

[发布工作流](.github/workflows/release.yml)配置了推送到 `master` 和手动触发的 Windows/macOS 构建。CI 同样需要准备 `Affdata.Core`；当前工作流只检出主仓库，尚未自动获取这项私有依赖。发布配置见[更新文档](Documentation/Updates.md)。

The [release workflow](.github/workflows/release.yml) defines Windows/macOS builds on pushes to `master` and manual dispatch. CI also needs `Affdata.Core`; the current workflow checks out only the main repository and does not yet fetch this private dependency. See the [update documentation](Documentation/Updates.md) for publishing configuration.

<a id="documentation"></a>

## 文档 / Documentation

| 文档 / Document | 内容 / Contents |
| --- | --- |
| [AFF 扩展编辑 / AFF extensions](Documentation/AffExtensions.md) | 扩展事件、属性与编辑操作。<br>Extended events, properties, and editing operations. |
| [Slide / Slides](Documentation/SkyAreaEditing.md) | Slide 语法、高度、编辑和预览行为。<br>Slide syntax, heights, editing, and preview behavior. |
| [音频后端 / Audio backend](Documentation/BassAudio.md) | BASS 播放、速度调整和资源生命周期。<br>BASS playback, tempo control, and resource lifetime. |
| [内置皮肤 / Built-in skin](Documentation/ArcadeAlphaSkin.md) | Arcade Alpha 资源来源及适配记录。<br>Arcade Alpha asset provenance and adaptation notes. |
| [构建与更新 / Builds and updates](Documentation/Updates.md) | 更新器、文件清单与发布配置。<br>Updater behavior, file manifests, and publishing configuration. |
| [i18n 接入规划 / i18n integration plan](Documentation/Localization.md) | 三语言目标、方案比较和逐步迁移计划。<br>Three-language targets, approach comparison, and incremental migration. |
| [macOS 文件选择器 / macOS file browser](Native/StandaloneFileBrowser/README.md) | 原生插件的来源与重新构建方法。<br>Native plugin provenance and rebuild instructions. |

## 贡献与反馈 / Contributing and feedback

欢迎通过 [Issues](https://github.com/anqtal/AffdataEdit/issues) 报告问题。请提供操作系统、编辑器版本、复现步骤，以及必要的日志或最小示例谱面。提交代码时保持改动集中，并执行与改动相关的验证；避免在同一次修改中顺带升级 Unity 或依赖。

Report problems through [Issues](https://github.com/anqtal/AffdataEdit/issues), including your OS, editor version, reproduction steps, and relevant logs or a minimal chart. Keep contributions focused and verify the affected behavior; avoid unrelated Unity or dependency upgrades in the same change.

## 项目来源与致谢 / Origins and acknowledgements

AffdataEdit 基于 [Arcade-plus](https://github.com/yojohanshinwataikei/Arcade-plus)，其前身为 Schwarzer 的 Arcade。功能行为和编辑体验主要参考 Arcade Alpha，部分渲染与音频实现参考 AffdataPlay；两者的共享算法和网格定义逐步收敛到 `Affdata.Core`。Slide 的相关来源记录于对应文档。

AffdataEdit is based on [Arcade-plus](https://github.com/yojohanshinwataikei/Arcade-plus), which derives from Schwarzer’s Arcade. Arcade Alpha is the primary reference for behavior and editing interactions; AffdataPlay informs parts of the rendering and audio implementation. Shared algorithms and mesh definitions are being consolidated in `Affdata.Core`. Slide provenance is documented separately.

感谢 Schwarzer、yojohanshinwataikei、Arcade-plus 与 Arcade Alpha 的贡献者，以及 [@LAM0578](https://github.com/LAM0578) 和 [@linwenxuan04](https://github.com/Linwenxuan04) 提供的技术支持。

Thanks to Schwarzer, yojohanshinwataikei, the Arcade-plus and Arcade Alpha contributors, and [@LAM0578](https://github.com/LAM0578) and [@linwenxuan04](https://github.com/Linwenxuan04) for their technical support.

## 许可与第三方资源 / Licensing and third-party assets

项目代码主要采用 [MIT 许可证](LICENSE)。第三方代码、字体、音频和图像保留各自的许可；项目的 MIT 许可证不重新授权这些资源。仓库另附 [CC BY 4.0 纹理许可证](LICENSE.texture)，具体资源仍需结合来源记录判断。

Most project code is covered by the [MIT license](LICENSE). Third-party code, fonts, audio, and images retain their own licenses; the project’s MIT license does not relicense them. The repository also includes a [CC BY 4.0 texture license](LICENSE.texture); consult the provenance of individual assets.

| 来源 / Source | 说明 / Notes |
| --- | --- |
| Unity / UPM / `UIElementsSchema` | 遵循 Unity 及各包作者的许可。<br>Subject to Unity’s and each package author’s licenses. |
| `Assets/Plugins` | 包含第三方 .NET 库、BASS / BASS_FX / BASSOPUS、FLAC 缓存所需的 BASS 扩展和使用 Signalsmith 库的 AffStretch；保留上游许可，参见[音频文档](Documentation/BassAudio.md)和[随程序分发的许可声明](Assets/StreamingAssets/ThirdPartyNotices.txt)。<br>Contains third-party .NET libraries, BASS / BASS_FX / BASSOPUS, BASS add-ons for FLAC caching, and AffStretch using Signalsmith libraries; upstream licenses apply. See the [audio documentation](Documentation/BassAudio.md) and [distributed notices](Assets/StreamingAssets/ThirdPartyNotices.txt). |
| [Unity Standalone File Browser](https://github.com/gkngkc/UnityStandaloneFileBrowser) / [Runtime OBJ Importer](https://assetstore.unity.com/packages/tools/modeling/runtime-obj-importer-49547) | 分别保留 gkngkc 及其他贡献者、Dummiesman 的许可。<br>Retain the licenses of gkngkc and contributors, and Dummiesman, respectively. |
| [Tabler Icons](https://tabler.io/icons) | 通用 UI 图标使用 Tabler Outline，遵循 [MIT 许可](Documentation/UI/Icons/LICENSE)；原始 SVG 与对应关系见[图标说明](Documentation/UI/Icons/README.md)。保留 `Assets/Textures/Icon/MDI` 的历史路径以兼容现有引用。<br>General UI icons use Tabler Outline under the MIT license. Original SVGs and mappings are documented alongside the license; the legacy `MDI` directory is retained to preserve existing references. |
| [Noto Sans SC](https://github.com/notofonts/noto-cjk) / 其他字体 / Other fonts | Noto Sans SC 使用 [SIL OFL 1.1](Assets/Resources/AffdataEdit/Fonts/NotoSansSC-OFL.txt)；其他字体遵循各自作者许可。<br>Noto Sans SC uses [SIL OFL 1.1](Assets/Resources/AffdataEdit/Fonts/NotoSansSC-OFL.txt); other fonts retain their authors’ licenses. |
| [OBS](https://obsproject.com/) | OBS 图标保留上游许可。<br>The OBS icon retains its upstream license. |
| Arcade Alpha / Slide 参考资源 / Reference assets | 参见[皮肤来源](Documentation/ArcadeAlphaSkin.md)和 [Slide 来源](Documentation/SkyAreaEditing.md)；导入资源保留原权利人的许可。<br>See [skin provenance](Documentation/ArcadeAlphaSkin.md) and [Slide provenance](Documentation/SkyAreaEditing.md); imported assets retain their original owners’ licenses. |
