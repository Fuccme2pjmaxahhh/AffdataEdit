# AffdataEdit 本地交接：2026-11-07

这是可脱离聊天使用的开发摘要。功能描述分为最终交互约定、当前实现和验证证据；它不是新增任务单，也不授权发 issue、推送代码或删除文件。继续开发时以当前用户要求、AGENTS 和实际源码为准。

## 入口与环境

Windows Arcade Alpha发布包只读参考现位于 `E:\AffdataDev\Arcade-Alpha_v1.12.2_read`（2026-11-08用户从C盘搬移改名）。读取配置、反编译、PURE类入口及辅助脚本见已保存的 `affdataedit-dev/references/arcade-alpha-read.md`。当前AffdataEdit跳字源码为 `Assets/Scripts/Gameplay/Components/ArcHitTypeEffects.cs`。

| 用途 | 路径／状态（记录时） |
| --- | --- |
| 实际源码 | `E:\AffdataDev\AffdataEdit-latest` |
| 聊天工作目录 | `C:\Users\Max Ng\Documents\ChatGPT\Affdata`，不是 Unity 源码 |
| Unity | `E:\AffdataDev\Unity\6000.6.6f1\Editor\Unity.exe`，版本读取 `ProjectSettings/ProjectVersion.txt` |
| 主场景 | `Assets/_Scenes/ArcEditor.unity`，运行 Untitled 空场景只会看到天空 |
| Git | `E:\AffdataDev\Git\cmd\git.exe` |
| Python | `C:\Python313\python.exe` |
| 缓存构建项目 | `E:\AffdataDev\BuildWorkspace` |
| 可用程序 | `C:\AffdataEdit_custom\AffdataEdit.exe` |
| 官方程序 | `C:\AffdataEdit_official`，不在定制发布目标内 |
| 一键打包 | `E:\AffdataDev\Build-AffdataEdit.cmd` |
| 构建日志 | `E:\AffdataDev\BuildLogs` |
| 专项检查产物 | `E:\AffdataDev\Checks` |

上游 `origin`：`https://github.com/anqtal/AffdataEdit.git`；fork remote `myfork`：`https://github.com/Fuccme2pjmaxahhh/AffdataEdit.git`。记录时当前分支 `custom-improvements`，HEAD `61011b0`（Improve editor hotkeys, selection, slide rendering and UI）。构建脚本、AGENTS 等还有本地改动。不要据此假设已推送；以后先查 status、log、remote。

## 用户协作偏好

- 最小范围完成明确修改，保留本地定制，不扩展成无关重构或大测试工程。
- 涉及新交互且不明确时先确认关键含义；已经确认的操作顺序不用重复问。
- 浅色／深色都要读得清楚，选中状态明显；颜色要和当前主题、周边 HUD 契合，不机械套纯黑或纯灰。
- 打包更新同一个 `C:\AffdataEdit_custom`，不产生 latest 后缀文件夹。保留安装目录用户文件。
- 此前某些文件修复明确要求“不备份”；不要把这句话扩展为永远禁用应用自身备份。保存快照同秒可覆盖，是明确且独立的约定。
- 必要编译／专项验证通过后交付，不重复跑无关检查；如实说明未实测部分。

## 打包脚本与构建边界

实现位于 `Tools/Build-Windows.ps1`；项目根 `Build-Windows.cmd` 和 E 盘入口只是包装器。使用说明 `Tools/BUILD-WINDOWS.md`。脚本已真实运行，勿另写一份相似脚本造成分叉。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "E:\AffdataDev\AffdataEdit-latest\Tools\Build-Windows.ps1"
```

脚本可选 `-UnityPath`、`-BuildWorkspace`，默认自动读 Unity 版本。它用互斥锁防止同一脚本并行构建，仅同步 Assets／Packages／ProjectSettings，保留构建副本的 Library。源码缓存的 robocopy `/MIR` 限于验证后的三个子目录；发布安装目录用 `/E`，保留 settings、谱面和背景。成功后核对 Arcade.dll SHA256；构建失败不发布旧产物。

构建入口 `Assets/Editor/WindowsCiBuild.cs` → `WindowsCiBuild.Build`，输出 `Build/Windows/AffdataEdit.exe`。场景固定 ArcEditor，StandaloneWindows64，StrictMode；BuildReport 不成功或有 warning 就失败。清理仅构建输出中的 Unity 不应随包分发的 BackUpThisFolder 目录。

脚本加 Git 到本进程 PATH，把 TEMP/TMP 指到 E 盘；启动参数含 `-batchmode -quit -job-worker-count 2 -desiredWorkerCount 1`。Unity/Hub 自身的少量 C 盘配置不能据此宣称完全移走。

用户2026-11-08新授权：发布前按完整路径自动关闭 C:\AffdataEdit_custom\AffdataEdit.exe，不保存、不再询问；仅限这个定制版。源码编辑器可以开着，但不能同时打开 BuildWorkspace；内存不足时要另行处理。过去源码 Unity 曾占约 13.7GB，导致第二进程 UDS 分配失败，关闭后才完成测试。这不是测试代码失败，也不是对未来进程的强杀授权。

暖缓存实跑约 32.8–48.6 秒，等待用户关程序的时间会额外计入。耗时包括 Unity 启动、编译、场景／资源整理和后处理。不要每次清缓存。判断新包是否更新用 `AffdataEdit_Data/Managed/Arcade.dll`，Unity 启动 EXE 的时间戳可能没变。

## 文本框最终选择约定

实现：`Assets/Scripts/Compose/Input/AdeTextSelection.cs`，由 `AdeValueEditor`、`AdeSlideEditor` 配置。

- 单击放光标，不因聚焦自动全选；普通拖动按字符选择。
- 双击把整个浮点数作为一个词，包括符号和小数点。`0.500,2.000` 的两个数是两个词，点到 `2` 或 `000` 都选 `2.000`。
- 三击及连续更多次点击统一全选整个框。废弃“小词→大句→全选”的中间层。
- 空白按最近数值处理。当前多击判定为 0.3 秒内且指针距离不超过 pixelDragThreshold，或解析到同一个 caret；不是仅凭按下次数计数。
- 第二次按下后如果开始拖动，越过拖动阈值就切回字符拖选，不持续锁定整词。拖动、键盘操作、失焦重置多击状态。
- Shift 扩展选择，Ctrl+A 等正常文本操作仍交给 TMP。
- 编辑提交前将 `；`→`;`、`，`→`,`、`。`／`．`→`.`；输入期间保留 IME 标点，提交时再解析。当前实现不是把所有 Unicode 全角字符都转半角。

## 热键预设与编辑命令

预设：`Legacy`、`New`、`Custom`。初次默认 Legacy；以前改过键的用户迁移到 Custom。在任一预设基础上手动改键就更新唯一一套 Custom；切换 Legacy/New 保留 Custom。Space 按普通热键处理，废弃“忽略 Ctrl/Alt”的早期想法。

New 对截图中启用的绑定优先，停用截图禁用项及冲突旧绑定；保留未涉及且无冲突的 AE 绑定。不要因用户提供截图就假定所有功能已经实现。

代码分工：`AdeHotkeyPresets.cs` 保存映射与迁移，`AdeInputManager.cs` 统一输入判断，`AdeEditorHotkeyCommands.cs` 处理新编辑命令；不要给每个预设重复写执行链。InputAction 源文件为 `Assets/Input/ArcadeInput.inputactions`，生成包装器 `AdeInputControl.cs` 必须保持一致。设置 key `AffdataEdit.HotkeyPresets`，节名 `hotkeyPresets`。

New 主要新增／调整绑定：A 点立得，G 网格，1 不创建／2 Tap／3 Hold／4 Arc／5 ArcTap，Ctrl+T 对齐，Alt+N Arc 反转，Tab 选中代码预览，Ctrl+F 空间选择，F1 音轨状态／F2 蓝红／F3 成对缓动／F4 缓动轮换，W 直线 S，Alt+W SISI。完整映射以 `BuildNew` 为准。New 中 Enter 播放切换、垂直镜像及冲突的旧创建绑定停用；Q、普通 Space、Ctrl+R 等无冲突绑定保留。

编辑与创建 Arc 的属性热键合并为同一套：有选中的 Arc 就修改选中 Arc，否则修改创建参数。选中编辑走现有 Command/Undo；不能把带 ArcTap 的黑线随意变实线。

Arc 反转：时间不动，交换头尾 XY；`Si↔So`、`SiSi↔SoSo`、`SiSo↔SoSi`，S/B 不变。它不是时间倒序。

代码预览：可复制 popup 显示全部选中物件 AFF，含 timinggroup 包装；只选 ArcTap 时保留必要父 Arc 包装。复制和 Tab 关闭自己的 popup 已接入。

尚未实现：值复制面板／数值复制、事件渲染器切换、中心点原点、Arc 分支、Arc 节点编辑。频谱、打开工程、单 ArcTap、直接指定 Arc 颜色尚未全部接入截图热键；不等于应用本身完全没有这些功能。UI 有“查看清单”。没有新的明确要求时不自动补做。

### Ctrl+R / Ctrl+F

- Ctrl+R 进入时间段选择；临时启用点立得，使用 Arc 创建时的定位面；左键两次确定起止时间，结束／取消恢复原模式，定位网格遵循现有开关。
- Ctrl+F 顺序为：**起始时间 → 定位面矩形两个对角 → 结束时间**。按时间段与 XY 矩形形成的空间范围，**有交集就选中**。不要求整个长物件都在框内。
- 两者共享现有 Operation、Cursor 与 finally 恢复路径。Ctrl+F 轨迹交集按实际曲线参数判定，考虑 Slide 宽度，不只比较包围盒。
- 入口：`AdeSelectNoteOperation.cs`、`AdeCursorManager.SelectRectangle`、`AdeSelectionVolume.cs`、`AdeSelectionManager.SelectIntersectingRange`。

### Q 与 Esc

- 播放／暂停与模式切换独立（2026-11-08用户纠正，取代旧“两步暂停”）：Q／PlayWhenHolding 调用 TogglePlayback，在当前编辑／播放模式内暂停与继续；连续按 Q 不退出播放模式。Enter／PlayOrPause 和原模式切换按钮走 OnPauseClicked，切换编辑／播放模式；内部强制退出仍用 Compose.Pause。Q 暂停只停止音频与谱面时间，不重置判定，不冻结动画：已生成 PURE 继续按真实帧时间淡出，禁止暂停期间生成新的 PURE；倾斜目标回到零并正常跟随。PlaybackComparisonPauseTests 检查 Q 暂停→Q 继续→Q 暂停→模式按钮退出、时间和判定保留。

右键菜单展开、鼠标按住时 Q 也应正常播放／暂停。菜单屏蔽规则对 Q/Enter 有特例，但文本框真正输入、模态框和重绑热键仍按现有规则处理。中键拖动先正常结束再播放；自由相机不吞掉这些播放键。入口：`AdeInputManager`、`ArcadeComposeManager`、`AdeFreeCamera`。

Esc 是固定“取消当前操作”：在黑线上点立得创建 ArcTap，即使没有 ongoing task，也应退出当前创建模式到 Idle、重置光标。`AdeOperationManager.Update` 已覆盖该分支；InputManager 的固定 Cancel 有 Escape 物理键兜底，避免可重绑的关闭拖动快捷键先消费 Escape。不要粗暴绕过文本／模态守卫。

## Slide 数据与连接

当前 AFF 输出格式：`slide(startTime,endTime,startCenter,startWidth,endCenter,endWidth,leftCurve,rightCurve,height);`。以 `AffParser.Slide`、`RawAffSlide`、`ArcSlide.IsValid` 为准。

- 至少持续 2ms；横向中心不局限 0..1，宽度必须正且有限，曲线中间不能交叉成非正宽度。
- 左右曲线 0..3，高度枚举 0..2。`ArcSlide.MinX=-0.5/MaxX=1.5` 是默认预览范围，不是所有合法数据的硬限制。
- 当前 WorldHeight 使用固定三种高度；不要因最初提过 enwidencamera Y 跟随就声称已经实现连续跟随。
- 连成同物件：按时间稳定排序，同 timinggroup、同高度，只连接该键下紧邻的前一段；起点时间与前段终点误差绝对值 ≤2ms，横向接壤区间重叠 **严格 >0.01**，重叠宽度先保留六位小数避免边界浮点误差。
- 使用实际重叠端点消缝，包括超出 0..1 的 X，修复只消一半的接缝。逻辑链和视觉消缝都受重叠条件约束。
- 某些 group head 的线由 shader 特意不画，不能看到少一条线就直接放宽连接判定。
- 入口：`Assets/Scripts/Gameplay/Managers/ArcSlideManager.cs` 的 `UpdateConnections`，`Gameplay/Chart/ArcSlide.cs`，`Gameplay/Components/ArcSlideVisual.cs`，测试 `ArcSlideTests`。

## AA播放显示对齐（2026-11-08）

- 后续截图回归发现之前仅全viewport公式检查不足：用户实际对照AA 1.13.1（`C:\Arcade-Alpha_v1.13.1_b2026110800_202611081354`），不是只读1.12.2包。已读取1.13.1核对，编辑视口固定(.05,.05,.9,.9)，设计尺寸取整个输出。AE现在同样保留均匀5%相机边距，DesignResolution读取完整targetTexture或Screen尺寸，不再取工具栏裁切后的camera.pixelWidth/Height，避免编辑模式下FOV和取景范围随工具栏比例变化。新增回归用例后20/20通过，结果 `alpha-viewport-results.xml`；仍不把测试等同于所有内容同帧截图验收。
- 视口修正版已更新 `C:\AffdataEdit_custom`，成功日志 `Windows_2026-11-08_08-51-48.log`，66.1秒（含等待保存退出），安装程序集哈希一致。

- 用户要求：已确认的显示差异对齐AA，**不更换长物件裁切**。AA当前参考包的Slide实现不完整，因此不据其占位代码改AE宽面Slide。
- Slide PURE按各自判定高度投影：地面维持world Y=0，普通天空用Arc y=1（world Y=5.5），高位／enwidened天空用Arc y=1.61（world Y=8.245），均复用长Arc的PURE上方偏移和动画。入口 `ArcHitTypeEffects.ShowSlide`，不再把天空Slide的跳字放到地面。
- 公式入口 `Gameplay/Utility/ArcadeAlphaAppearance.cs`：Tap/ArcTap淡入90→80，Tap长度使用连续浮点且钳制非负，不再套共享Play工厂的厘米截断。Tap CPU/Resident投影/GPU shader保持同一淡入公式；ArcTap基准alpha240/255。
- 实体Arc按AA段尾距离计算远处alpha：常规阈值85，零时长新样式80，10单位内由225/255到75/255；body/head/高度指示器使用AA对应opacity乘数。原有段几何、From/To裁切、Hold/Slide裁切及黑线专用更新路径保留。
- 自动倾斜记录左右极值而非同侧Arc求和；按AA的EnwidenScale补偿、正向9.6/s／其他4.8/s跟随与.0001阈值。camera事件初始角度从AA的LookAt矩阵计算；天空线满扩展高度8.25，连接线随扩展淡化。
- 背景Canvas调整到AA的1280×720宽度匹配，图片1280×960，宽屏Y偏移按设计尺寸计算；仅viewport改变时重设位置，无每帧对象分配。
- 专项测试 `Assets/Tests/EditMode/ArcadeAlphaAppearanceTests.cs` 最终18/18通过（含宽屏背景切回16:9复位），源码编译无诊断；日志未检出C# warning/error或Shader error。结果与日志在 `E:\AffdataDev\Checks\alpha-appearance-results.xml` / `alpha-appearance-tests.log`。这是源码修改与针对性验证，不是AA/AE所有物件同帧截图全量验收；未因此自动发布到安装目录。
- 用户随后要求打包，以上AA对齐和天空Slide PURE修复已发布到 `C:\AffdataEdit_custom`；成功日志 `Windows_2026-11-08_08-23-12.log`，含等待用户保存退出共124.1秒，安装程序集SHA256核对一致。前一次08-18-46构建成功但等待程序退出超时，未发布，不要把该旧包当最新交付。

## 主题、设置、默认值与保存

- 圆形右键菜单以实际项数等角度为基础，不再把奇数项补成偶数槽位；按相邻按钮宽高在中心连线上的投影做30%的间距修正，每项角度偏移最多3°（且不超过基础角距10%），让边缘留白稍微均匀。先 Show 获取实际尺寸，微调后统一计算半径，保证按钮矩形之间至少12个Canvas单位间距。保留顺序和首项朝向，竖向菜单不受影响。2026-11-08 更新后源码编译无诊断；按用户9项截图估算的尺寸计算，边缘间距标准差10.14→8.00。未打包或在程序中目视验证此次微调。

- 打开谱面选择框的本地／云端按钮使用专用 pressed sprite：本地按钮深色压暗55%、浅色压暗25%；云端按下变灰，按文字明暗使用 `#626262` 或 `#ABABAB`，保留其他对话框样式。
- 顶部音频时间／谱面时间选择存入设置 `useChartTiming`（key `AffdataEdit.UseChartTiming`），点击即保存，启动读取；无已保存值时仍默认音频时间。

- “使用全新的右键菜单”在“点击选取”之前。点击选取仅新菜单启用时可操作；不可用时置灰并显示未勾选，存储偏好保留。
- 浅色模式 pause/score HUD 用和主题契合的偏灰深色，白字可读；当前中性参考约 `#45474A`，暖／冷主题随色调适配。相关文件 `AdeUiTheme.Hud.cs`。
- 菜单选中底色只加强右键菜单，未全局改变 Selection：浅色 `Lerp(Current.Selection,Current.Text,.20f)`；圆形菜单悬停阴影 alpha `.24`，普通浅色 `.12`。深色沿用原底色；竖向菜单选中也用菜单专用颜色。
- 顶部通知只占文字附近：最小宽 320，左右留白各 80，两端 48 渐变；填充用当前顶栏 Chrome 同色，文字用当前 Text，不挡整条工具栏也不拦点击。入口 `AdeToast.cs`。
- 谱面加载后 scoreboard “打开”标签直接 SetActive(false)，清理项目再显示；不只改透明度，以免主题切换把它恢复出来。
- 初次默认 scroll speed **5.5**，存储值165（显示速度×30）。不覆盖现有已保存速度。
- 初次分辨率：屏幕工作区考虑缩放和窗口边框后足够才选 **1920×1080**，否则1280×720；已有设置继续使用。入口 `AdeScreenResolution.cs`。
- 手动／自动快照命名如 `2_2026-11-07_21-30-15.aff`，日期时间之间用 underscore；**不用毫秒，同名允许覆盖**。手动 Backup 与 Autosave 目录分开。File.Copy overwrite=true，自动保存 FileMode.Create。
- 先在 MemoryStream 序列化成功后再改原文件。AFF serializer 会关闭传入流，之后取 ToArray，不能再 Position/CopyTo。IO 在 try/using 内，写成功后才提示成功。入口 `AdeProjectManager.cs`。

## UI 布局的坑

热键预设必须是热键设置页所有分组之前的第一行。`AdeSettingsDialog.Hotkeys.cs` 从 `AdeUiKit.Content(content.Dialog)` 找实际 ScrollView 内容，不能直接用 `AdeHotkeyDialogContent.transform`（那是活跃的对话根，会把行漏到主界面）。`BuildHotkeyGroups` 跳过 preset 行，在 Mount 完成后 SetAsFirstSibling。已打开实际 UI 确认。

主题形状可能从 `Resources/AffdataEdit/Theme/Serialized/<preset>` 加载，改生成色前检查是否有序列化资源覆盖。此次 ContextMenuHover 是运行时形状。

## 验证记录与限制

- 2026-11-07 本地／云端按钮按下反馈、时间模式记忆已打包到 `C:\AffdataEdit_custom`；日志 `Windows_2026-11-07_23-36-40.log` 成功，51.7秒。实测新包从音频切到谱面时间，`settings.json` 写入 `useChartTiming: 1`，正常退出后重新启动仍显示谱面时间。按钮按下效果未做深浅色实际按住的截图验证。

- 最终两项 UI 调整（菜单阴影、热键预设置顶）已包含在 C 盘包内。构建日志 `E:\AffdataDev\BuildLogs\Windows_2026-11-07_22-22-00.log` 成功，32.8秒；构建入口对 warning/error 有失败门槛。
- `Player_menu-preset-final.log` 未检出应用异常；实际主界面正常，热键页第一行已目视确认。没有把所有鼠标选择、主题组合、菜单悬停和创建流程重新跑一遍。
- 专项测试12项（preset3 + volume/reverse9）在 `hotkey-selection-results.xml` 中通过；该旧报告还含一个当时失败的 Esc 测试，报告总状态为 Failed，不能冒充全绿报告。
- 修正后的 Esc 测试分别在 shortcutKeysConsumeInput 开／关两模式通过：`escape-cancel-results.xml` 为2/2 Passed。因此合计14个不同专项用例通过，但不是同一次14/14全量运行，也未跑整个项目完整测试套件。
- EditMode 输入测试要临时启用 `RUN_PLAYER_UPDATES_IN_EDIT_MODE` 并恢复状态，不替换整个 InputSystem.settings（会销毁原临时设置）。清理 InputActionAsset 用 DestroyImmediate，别调用生成 wrapper 的 Dispose/Destroy。
- `E:\AffdataDev\Checks\Arcade.rsp` 是旧编译参数快照，不自动包含新增文件；快速 csc 检查必须补上新源文件。通常直接运行现有增量打包更可靠。
- 最初讨论过的外置 Lua、整体本家渲染审查等没有在这份交接中逐项确认完成。以后先读当前实现和新的用户任务，不自动当成仍要补齐的工作。

- 2026-11-08追加 AA 1.13.1实际GPU核查：Arc与Shadow片元着色器直接拒绝 abs(world Z)>90，CPU候选100不是最终可见距离。AE NoteIndirect 对Arc（含头）及Arc阴影加入同样逐像素90裁切，保留原长物件近端裁切实现。D3D11反汇编见 Checks/AlphaJudgementReference/aa113-arc-4.asm.txt 与 aa113-shadow-4.asm.txt。
- 播放HUD：独立扩大暂停／信息组，采用 AA 1.5x 与1920x1080 Expand缩放，编辑工具栏布局保留。暂停图237x99，播放坐标(-117,-20)，对应AA legacy组(170-237)、隐藏HP侧栏-50；信息图向右偏30单位，均随组缩放，不能只把两幅图原地放大。背景新增 ArcBackgroundEdgeFill，仅在主体图像以外采样原图边缘做渐进模糊延伸；不创建大纹理副本／CPU模糊／额外RT，跟随换背景和源图颜色。

- 本次验证：外部C#编译无诊断；AlphaAppearance 20项及播放状态回归1项通过。旧测试销毁EditMode对象时未取消LitMotion，虽断言通过但退出期有MissingReferenceException，已在测试清理显式调用OnDestroy并单独重跑，1/1且日志无异常。用户按物理Escape终止computer-use，已停止所有界面输入；最终包尚未完成实机同帧画面对照，不能宣称像素级一致。AA参考窗口已恢复谱面时间13945，未保存／更改谱面。
- 当前修正已打包到 C:\AffdataEdit_custom；最终日志 Windows_2026-11-08_09-26-46.log，45.6秒，安装程序集SHA256与构建输出一致；构建成功且无编译／Shader警告错误。保留用户原有定制与安装目录数据。
- 2026-11-08黑条回归：原补边挂在Background相机画布内，会被GameplayCamera的5%视口裁掉，无法填工具栏与视口间的缝。新增EditorCanvas第一层RawImage，整窗覆盖但shader只画GameplayCamera.rect之外，按原背景实际屏幕矩形换算并钳制到可见视口边缘采样，带渐进模糊；原相机内补边继续处理图片自身空隙。没有扩大GameplayCamera.rect或改变谱面透视。
- 黑条截图原图2559x1527，左侧102..127列纯黑，与camera.rect.x=.05（约128像素）精确吻合。源码覆盖检查确认这些列进入新增outside-viewport分支，129列及画面中央不进入。构建Windows_2026-11-08_09-33-24.log成功，无C#及Shader编译警告错误；等待关闭定制版发布。此为截图位置与源码范围检查，尚非新包实机截图验收。
- 黑条修正版已发布C:\AffdataEdit_custom，安装程序集哈希核对一致。

- 2026-11-08再次纠正补边实现（取代前述EditorCanvas首层RawImage方案）：全屏补边造成HUD遮挡，已经移除该新增UI图层。现在只为AdeGameplayContentResizer原有RawImage更换合成材质：viewport内部保留原谱面RT采样，外部采样背景边缘渐进模糊。复用原显示层的父级、层级、相机和RT，不新增相机、Canvas或全屏Graphic。销毁时还原原材质。独立相机方案渲染检查失败，未交付。
- 合成回归验证为真实Unity GPU渲染（1280x720与800x600）：谱面区域绿色、补边区域红色、上方工具栏蓝色，像素断言全部通过。Checks/border-composite-final.log，输出border-layer-1280.png与border-layer-800.png；这是受控图层测试，不是实际谱面的全界面截图。临时QA脚本仅放BuildWorkspace，打包同步自动移除，没有写入产品。
- HUD遮挡修正版最终已发布 C:\AffdataEdit_custom；Windows_2026-11-08_09-44-14.log，44.7秒，程序集哈希一致，无C#／Shader编译警告错误。使用FindAnyObjectByType定位既有显示层，避免Unity6.6已弃用FindFirstObjectByType警告；09-43-15那次构建因严格警告检查失败，没有发布。
- 2026-11-08再纠正边缘物件被替换：camera.rect不是RT有效像素范围，按rect强制替换边缘会切掉实际绘制内容。合成shader改为先采样完整谱面RT，所有不透明像素原样保留，半透明内容以over合成到补边之上，仅透明空白露出背景。GameplayCamera清屏alpha设0，缩小视口期间渲染前清空RT，避免前一全屏帧在外圈残留；销毁恢复原清屏颜色。替代旧rect分区替换。边缘GPU回归1280x720与800x600通过，包含rect外不透明蓝色、纯黑、半透明蓝色物件，pixel断言与截图见Checks/border-alpha-check.log及border-alpha-*.png。旧中央纯色测试不足以证明边缘物件保留。
- 边缘内容保留修正版已发布C:\AffdataEdit_custom，Windows_2026-11-08_09-50-28.log，构建成功且无C#／Shader编译警告错误，安装程序集哈希一致。尚未完成用户实际谱面同帧截图复核；GPU测试明确验证边界外物件像素保留。
- 2026-11-08 AA同帧32594ms/4.9实机对照找到真实裁切原因：AA工具栏背景遮满相机5%裁切边；AE的四个BarView有-20 sizeDelta透明内缩，露出已被相机裁切的轨道矩形边界。撤回此前针对RT的补边材质、透明清屏及GL.Clear全部变更，仅保留原背景画布内局部补边；BarView取消20单位内缩，四边工具栏背景至少覆盖5%编辑画布，保留投影与相机rect。旧色块/alpha测试不代表实际物件未被相机裁切，不再作为此问题验收。
- 实机首次复核还发现View本身没有Image，只改View尺寸仍不够；最终给四个现有Bar Canvas各加一个首子Image作为不透明chrome底板，随原Bar一起收起，raycastTarget=false，颜色跟随Current.Chrome，覆盖到5%裁切边。不是在谱面或EditorCanvas上新增覆盖图层。
- 最终Windows_2026-11-08_10-04-03.log构建47.9秒成功，无C#／Shader警告错误，程序集核对一致。实际打开新版空谱面窗口检查：原黑色间隙/矩形轨道边缘已被四边chrome完整盖住，HUD未遮挡。AA已实际切到32594ms/4.9用于对照。尝试通过UI加载谱面时打开按钮未触发对话框，未完成新包同谱面物件同帧复核，不把空谱面窗口检查说成全部验证通过。

- 2026-11-08用户再次否定整块chrome方案：移除FitToolbarBackgroundsToViewport/四边底板/强制Bar尺寸及View清零，恢复场景原UI尺寸与布局。现在GameplayViewRect仅记录AA取景，实际相机全viewport，projection的m00/m11按logical宽高缩放、m02/m12补偿中心，从而保留AA中央物件屏幕位置且把几何继续渲染到窄工具栏背后。背景图按logical宽高保留原主体比例，原局部edge fill只绘制图片外空白。PURE尺寸换算使用logical viewport高度。禁止再靠铺满白色侧栏遮裁切口。
- 用户明确长期授权：后续打包时直接关闭C:\AffdataEdit_custom\AffdataEdit.exe，不保存、不询问；已修改原Tools/Build-Windows.ps1，只按完整ExecutablePath匹配停止该定制版，保留official/AA/Unity等其他进程。
- 最终Windows_2026-11-08_10-16-11.log成功61.9秒，无C#／Shader编译警告错误，自动按授权关闭custom后发布，哈希一致。ProjectionContinuationCheck在1280x720/800x600真实相机渲染证明原AA屏幕坐标误差<.01px且旧viewport外几何能继续显示。实机通过曲封面打开本地文件夹E:\sinkdeepseek（LastWorkingDifficulty=2, LastWorkingTiming=32594），已在小窗口及最大化窗口检查同谱4.9/32594ms：恢复原窄工具栏与间距，四周无整块白底，右Arc/轨道持续延伸到工具栏下方，无原矩形断口。
- 2026-11-08 HUD后续调整：难度框Difficult由(-92,-85)/83宽改为(-116,-85)/68宽，保留仅数字Rating语义并右对齐；Difficulties选择组从-260左移到-284（比AA的-306少挪），两组间8单位空隙，文本右端含panel偏移30后仍有余量。Name/Composer位置分别(-245,-122)、(-245,-152)，宽197，使右边界与难度对齐。播放HUD通过hudPlayerBlend做.25秒OutCubic位移与缩放，保留窗口尺寸适配。Q TogglePlayback不再被switchingMode拦截；模式动画不取消、只暂停播放状态。PlaybackComparisonPauseTests已添加切换锁为true的回归，1/1通过。
- HUD修正已发布C:\AffdataEdit_custom；Windows_2026-11-08_10-33-12.log成功53.6秒，无C#／Shader编译警告错误，自动关闭custom，程序集哈希一致。实际加载E:\sinkdeepseek已保存Rating=10，在深色编辑、小窗口播放、最大化播放HUD里确认10完整可见，五个难度按钮适度左移，名称/曲师相对曲封位置正常。Q切换锁回归1/1通过；CUA合成Q在该InputSystem实例未触发（未据此宣称物理Q实机通过），状态逻辑测试明确验证switchingMode=true时能暂停且不会取消模式切换。
- 实机检查结束已通过顶部时间框恢复32594ms并停留编辑模式；没有修改难度、名称、曲师或谱面物件，也没有主动保存谱面。HUD逻辑测试验证Q切换锁，实机检查验证10在小窗口/最大化播放均不截断。
