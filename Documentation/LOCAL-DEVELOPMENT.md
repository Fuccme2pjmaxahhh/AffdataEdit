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

本次补充验证：`live-drag-text-results.xml` 11 项检查通过（片段分割、拖动最近网格线、播放中编辑保留其他音符判定及撤销、Q 状态切换）。未做完整应用鼠标交互验收。

实现：`Assets/Scripts/Compose/Input/AdeTextSelection.cs`，由 `AdeValueEditor`、`AdeSlideEditor` 和设置页 Register 配置；设置内含未激活行的输入框也会接入。

- 失焦后的第一次普通单击选最近的小片段，以小数点、逗号、分号及空白分段，符号跟随数字：`-0.123,0.456` 分为 `-0`、`123`、`0`、`456`。保持焦点后的普通单击放光标；取消单数字特例。设置页沿用同一套配置逻辑。
- 双击把整个浮点数作为一个词，包括符号和小数点。`0.500,2.000` 的两个数是两个词，点到 `2` 或 `000` 都选 `2.000`。
- 三击及连续更多次点击统一全选整个框。废弃“小词→大句→全选”的中间层。
- 空白按最近数值处理。当前多击判定为 0.3 秒内且指针距离不超过 pixelDragThreshold，或解析到同一个 caret；不是仅凭按下次数计数。
- 第二次按下后如果开始拖动，越过拖动阈值就切回字符拖选，不持续锁定整词。拖动、键盘操作、失焦重置多击状态。
- Shift 扩展选择，Ctrl+A 等正常文本操作仍交给 TMP。
- 设置中的数值／网格输入也在原 onEndEdit 回调前归一化标点；普通文本和密码字段只统一选择交互。编辑提交前将 `；`→`;`、`，`→`,`、`。`／`．`→`.`；输入期间保留 IME 标点，提交时再解析。当前实现不是把所有 Unicode 全角字符都转半角。

## 热键预设与编辑命令

2026-11-08 补充：点击热键绑定按钮时，用“谱面已保存”同款顶部提示说明“请按新热键；按 Esc 退出绑定并设为未绑定”，提示 Canvas 排序提升以在设置对话框上方可见。Esc 结束交互绑定并将对应功能保存为空绑定（进入 Custom）；切换绑定项／关闭设置等程序取消保留旧绑定。鼠标操作说明使用独立多行正文，不再识别为固定34高标题；创建时命名所在行而非 TMP 子文字，按正文自然高度换行。5项相关检查通过（`hotkey-help-escape-results.xml`），warn-as-error 编译通过；完整应用外观未手动验收。

自由相机中键转向按用户要求减为原速度0.5倍：mouse delta 系数0.8→0.4；滚轮调速、WASD 移动与手柄转向不变。

预设：`Legacy`、`New`、`Custom`。初次默认 Legacy；以前改过键的用户迁移到 Custom。在任一预设基础上手动改键就更新唯一一套 Custom；切换 Legacy/New 保留 Custom。Space 按普通热键处理，废弃“忽略 Ctrl/Alt”的早期想法。

New 对截图中启用的绑定优先，停用截图禁用项及冲突旧绑定；保留未涉及且无冲突的 AE 绑定。不要因用户提供截图就假定所有功能已经实现。

代码分工：`AdeHotkeyPresets.cs` 保存映射与迁移，`AdeInputManager.cs` 统一输入判断，`AdeEditorHotkeyCommands.cs` 处理新编辑命令；不要给每个预设重复写执行链。InputAction 源文件为 `Assets/Input/ArcadeInput.inputactions`，生成包装器 `AdeInputControl.cs` 必须保持一致。设置 key `AffdataEdit.HotkeyPresets`，节名 `hotkeyPresets`。

New 主要新增／调整绑定：A 点立得，G 网格，1 不创建／2 Tap／3 Hold／4 Arc／5 ArcTap，Ctrl+T 对齐，Ctrl+N Arc／Slide 反转，Tab 选中代码预览，Ctrl+F 空间选择，F1 音轨状态／F2 蓝红／F3 成对缓动／F4 缓动轮换，W 直线 S，Alt+W SISI。完整映射以 `BuildNew` 为准。New 中 Enter 播放切换、垂直镜像及冲突的旧创建绑定停用；Q、普通 Space、Ctrl+R 等无冲突绑定保留。

编辑与创建 Arc 的属性热键合并为同一套：有选中的 Arc 就修改选中 Arc，否则修改创建参数。选中编辑走现有 Command/Undo；不能把带 ArcTap 的黑线随意变实线。

Arc 反转：时间不动，交换头尾 XY；`Si↔So`、`SiSi↔SoSo`、`SiSo↔SoSi`，S/B 不变。它不是时间倒序。

代码预览：可复制 popup 显示全部选中物件 AFF，含 timinggroup 包装；只选 ArcTap 时保留必要父 Arc 包装。复制和 Tab 关闭自己的 popup 已接入。

尚未实现：值复制面板／数值复制、事件渲染器切换、中心点原点、Arc 分支、Arc 节点编辑。频谱、打开工程、单 ArcTap、直接指定 Arc 颜色尚未全部接入截图热键；不等于应用本身完全没有这些功能。按用户后续要求已移除“截图中尚未支持的热键／查看清单”提示行。没有新的明确要求时不自动补做。

### Ctrl+R / Ctrl+F

- Ctrl+R 进入时间段选择；临时启用点立得，使用 Arc 创建时的定位面；左键两次确定起止时间，结束／取消恢复原模式，定位网格遵循现有开关。
- Ctrl+F 顺序为：**起始时间 → 定位面矩形两个对角 → 结束时间**。按时间段与 XY 矩形形成的空间范围，**有交集就选中**。不要求整个长物件都在框内。
- 两者共享现有 Operation、Cursor 与 finally 恢复路径。Ctrl+F 轨迹交集按实际曲线参数判定，考虑 Slide 宽度，不只比较包围盒。
- 入口：`AdeSelectNoteOperation.cs`、`AdeCursorManager.SelectRectangle`、`AdeSelectionVolume.cs`、`AdeSelectionManager.SelectIntersectingRange`。

### 在粘贴时修正时间（2026-11-08）

- 设置→常规新增“在粘贴时修正时间”，`ArcadePreference.FixNotesTimeOnPaste` 默认 true，修改后保存；旧设置缺少字段时也默认开启。
- 只读核对 AA 1.13.1 的 `AdeCopyOrCut`、`ArcEditorUtility.FixNotesTime`、`AdeGridManager.TryAttachTiming`，参考在 `E:\AffdataDev\Checks\AAPaste`。确认复制／剪切粘贴时，对全部节拍线按时间顺序查找严格相差 <2ms 的线，使用未取整节拍时间判断阈值，写入取整时间；不受网格显示开关影响，不改变鼠标预览过程。
- 修正 Tap、ArcTap、长物件头尾及嵌套 ArcTap；Arc 包含修正后的 ArcTap，单 ArcTap 短宿主保留时长。短 Hold／Slide 若吸附后不足合法时长则保留原头尾；超出父 Arc 的独立 ArcTap 继续走已有跳过规则。复用原粘贴命令的撤销／取消。
- 同次将 Slide F4 改为包含 (S,S) 的五组合循环。Unity 3 项专项检查通过，编译 warn-as-error 通过；结果 `E:\AffdataDev\Checks\paste-timing-results.xml`。未打包，未做完整应用手动交互验收。

### Q 与 Esc

- 播放／暂停与模式切换独立（2026-11-08用户纠正，取代旧“两步暂停”）：Q／PlayWhenHolding 调用 TogglePlayback，在当前编辑／播放模式内暂停与继续；连续按 Q 不退出播放模式。Enter／PlayOrPause 和原模式切换按钮走 OnPauseClicked，切换编辑／播放模式；内部强制退出仍用 Compose.Pause。Q 暂停只停止音频与谱面时间，不重置判定，不冻结动画：已生成 PURE 继续按真实帧时间淡出，禁止暂停期间生成新的 PURE；倾斜目标回到零并正常跟随。PlaybackComparisonPauseTests 检查 Q 暂停→Q 继续→Q 暂停→模式按钮退出、时间和判定保留。

右键菜单展开、鼠标按住时 Q 也应正常播放／暂停。菜单屏蔽规则对 Q/Enter 有特例，但文本框真正输入、模态框和重绑热键仍按现有规则处理。中键拖动与 Q 播放／暂停独立，Q 不结束正在按住的拖动；自由相机不吞掉这些播放键。入口：`AdeInputManager`、`ArcadeComposeManager`、`AdeFreeCamera`。

Esc 是固定“取消当前操作”：在黑线上点立得创建 ArcTap，即使没有 ongoing task，也应退出当前创建模式到 Idle、重置光标。`AdeOperationManager.Update` 已覆盖该分支；InputManager 的固定 Cancel 有 Escape 物理键兜底，避免可重绑的关闭拖动快捷键先消费 Escape。不要粗暴绕过文本／模态守卫。

## Slide 数据与连接

### 创建预览、粘贴选择与 Legacy 菜单（2026-11-08）

- 新选中物件自动切换当前时间组；ArcTap取宿主Arc组，重复选中已有物件／取消选择不切组，多选以最后新增选择为准。时间组面板打开时跟随滚到当前组，SC／Camera行显式选中同样切组。用户确认只将时间组选择改为组号输入，曲线／颜色／高度下拉不变；ValueEditor共用组输入覆盖Tap/Hold/Arc/Slide/ArcTap，0默认组，跨组多选显示“-”，只接受现存非负组号，ArcTap按宿主去重编辑并支持原撤销。SC／Camera编辑器也用组号输入，底部当前组／事件范围等非物件编辑选择器保留原控件。 10项组号校验／宿主映射检查通过（timing-group-input-results.xml），源码warn-as-error编译通过；Windows_2026-11-09_02-12-52.log构建51.1秒成功，无C#／Shader警告错误，发布到固定custom目录。尚未进行全部物件类型、多选和SC／Camera的实际面板交互验收。

- 用户纠正F1～F4优先级：存在选中Arc／Slide时优先普通编辑，不修改创建参数；选中Slide但不支持的Arc颜色／虚实动作不回退到创建。无选中Arc／Slide时仍可修改Arc创建参数／预览；活动创建操作与编辑命令保持互斥，避免向Prepare中的命令再Add。创建Arc、Slide、单ArcTap的坐标选择启用fixedWorldPlane，进入该坐标阶段时捕获Z，网格／碰撞面与坐标采样使用同一深度，确认或取消finally解除锁定。SelectCoordinate默认仍跟随时间，编辑、粘贴、脚本、矩形选区不启用冻结；时间选择与中键拖动不变。 13项针对性检查通过（creation-priority-plane-results.xml），源码warn-as-error编译通过；未把这些检查等同于全部播放速度、SV、自由相机与鼠标输入组合的实机验收。

- 用户最新要求不使用更多子菜单：移除NormalSelected的更多父项，上下翻转／水平剪切／转Slide按可用性直接在原更多位置展开（点立得之后、镜像之前），其他AA共同操作相对顺序不变。设置恢复平铺操作标题；已有显式排序中selection-more占位按子项顺序展开，旧隐藏more项忽略，保留各操作显隐。奇数不补偶数槽继续生效。

- 用户按新截图再次纠正：非点立得选中主菜单真正采用AA八入口结构，顺时针从左侧：网格、点立得、更多、镜像、剪切、复制、时间对齐、删除。原上下翻转／水平剪切／转Slide按类型收进更多，设置以“更多／”标注子项并保留独立显隐；没有可用子项时隐藏更多。取消奇数补偶数槽（用户最新要求），镜像优化也按实际项数配对，不留空槽。此前只重排平铺操作的方案不再作为完成标准。

- 所有物件的NormalSelected默认菜单按AA共同操作顺序：网格、点立得、删除、时间对齐、水平镜像、复制、剪切；AE上下翻转／水平剪切／转Slide随后，按原对象类型可用性过滤，不删任何已有功能。已只读观察正在运行AA 1.13.1的Hold选中菜单确认共同顺序；运行菜单及设置目录共用新默认，用户显式排序仍优先，恢复默认可采用新顺序。用户撤回取消偶数槽的要求：继续保留奇数补偶数槽及现有圆环几何，点立得状态不变。

- 创建Arc时恢复New F1～F4：只有Arc创建状态的四个属性动作可穿过进行中操作守卫，优先修改创建参数及当前预览Arc，不误改旧选中物件。开始坐标／结束时间／结束坐标三阶段均即时重建预览与关系／判定时间，不全局ResetJudge；finally清理预览引用。其他操作、模态框、文本输入及右键菜单原守卫保留。四项ArcCreationHotkeyTests通过，覆盖四动作创建参数及Hold／反转拒绝分支；未进行完整三阶段物理按键实机验收。

- 用户纠正先前共用要求：NormalSelected保持当前普通物件菜单；CreateSelected重新独立，AA只读Items实现为创建目录加删除（AdeClickToCreate.cs），AE额外保留选中Arc/Hold时转Slide。设置恢复四种状态，排序／隐藏各自保存，原共用配置继续属于NormalSelected；Legacy仍无直接创建Slide，NEW保留原创建目录；操作进行中的取消菜单优先规则不变。

- 用户后续微调：Future选中亮度由0.46小幅回落到0.42，其余难度及非选中态不变。

- 用户确认其他难度已合适，仅提升Future／Eternal选中亮度目标至0.46／0.53，Past／Present／Beyond选中及全部非选中态保持当前确认值。继续不混白，保留色相饱和度与峰值限制。

- 难度亮度再按用户反馈分开：Beyond未选中单独降至0.18（其他未选中0.29），Past／Present／Eternal选中分别0.53／0.78／0.39，Future／Beyond选中维持0.32。保留RGB同比缩放、不混白及选中透明度，场景数组顺序已核对PST/PRS/FTR/BYD/ETR。

- 用户否定难度标签提亮后泛白：DifficultyPalette 取消混白，改为RGB同比例缩放以保持色相与饱和度，最大通道限制0.95；普通亮度目标降为0.29、当前难度0.32（原FTR高亮亮度约0.288，仅略提亮）。保留未选中0.65／选中1的透明度对比。此前0.58／0.82混白方案已废弃。

- 脚本字号后续统一以上方“重新加载”模板字号为准：操作按钮、分类、工具标题、参数描述及控制文本一致，保留面板标题／计数层级。无“当前时间”按钮的输入／下拉行预留与时间行相同的右侧按钮及间距，使控件左边缘一致。HUD难度色块使用 DifficultyPalette 保留色相方向并归一化亮度（普通0.58、选中0.82），选中全不透明、未选中0.65；初始化、清空及切换难度共用刷新，材质销毁时释放。

- 指示线后续：固定为两侧标题较大文字宽度的1.25倍、厚3单位（原1.5的两倍），居中挂在高亮块内，二者共享同一0.26秒轻微OutBack回弹进度，始终等距平移。Feedback悬停文案修正“应用反馈”。

- 后续脚本UI：分类名称改“外置工具”；切换控件取消模板内置布局／4单位内缩，选中块、底板、文字与上方操作按钮同高40，左右共享8单位分隔基准。纯色矩形1.5单位直线替换圆角纹理下划线。高亮块0.18秒OutCubic滑动，参数区0.2秒高度／透明度展开及收起，悬停提示仍延迟0.5秒后0.14秒淡入；均忽略播放时间缩放，重入从当前值继续，切换分类／销毁取消相关动画。源码warn-as-error编译通过，六项三种宽度的左右按钮／高亮边界与中心对齐回归通过（script-selector-results.xml）；未打包，动画尚未在实际应用中目视验收。

- 上述脚本UI最终包 Windows_2026-11-08_23-25-04.log 成功，40.4秒，无C#／Shader警告错误；源码warn-as-error编译通过。已实机窗口化核对加宽25%的面板、固定双按钮、分段选择圆角和下划线、普通工具参数字体及十位数字宽输入框；分类切换／参数展开未再改变顶部或列表宽度。未执行Lua工具修改谱面，未进行所有主题和分辨率组合的完整回归。

- 外置脚本面板后续：宽度增加25%；操作按钮和分类分段控件固定在列表上方，不在分类切换时重建；分段用选中底色／下划线，底板和选中块使用Sliced圆角。ScrollRect固定滚动条占位、内容不按子项偏好反向撑宽，避免窗口化切换／展开参数造成宽度跳动。工具行在激活前应用主题色，ColorTint零过渡且立即刷新，避免白色淡入。参数描述18号，控制文字20号，输入／下拉宽度按其字体十个0加24内边距计算；顶部控件的flexibleHeight为0，指示线ignoreLayout。侧栏悬停延迟改0.5秒。已实机在窗口模式检查分类切换与参数展开宽度及字体；没有运行Lua修改谱面。

- 本次环形镜像／避让专项1/1通过；最终包 Windows_2026-11-08_23-09-41.log 成功，47.8秒，未检出C#／Shader警告错误。实机打开脚本卡确认53个工具计数位于标题右侧、操作按钮均分全行、快速／普通切换及单行工具名正常；确认侧栏外置脚本延迟提示。未实机运行Lua工具或完整复核所有环形菜单、全部侧栏提示和主题组合。

- 环形角度优化改为镜像成对移动，避免逐按钮搜索造成上下不匀称。Arc缓动页固定左虚实／右颜色，S、Si、So、B上半圈，SiSi、SiSo、SoSi、SoSo下半圈；保留8单位避让。外置脚本用快速／普通双按钮切换，AA Selection/Interactive归快速、Parameters与全部A1归普通；操作双按钮均分全行，工具行只显示单行名称，加载数量放标题右侧。侧栏图标停留0.5秒显示简短提示，移开／按下／关闭即隐藏。

- 后续用户要求：Legacy 点立得直接创建 Slide 的入口从运行菜单和设置目录移除，NEW 保留；选中物件的转 Slide 不变。环形角度在原等分附近做局部紧凑搜索，首项固定、其他项偏移不超过12°／基础角距28%，保留顺序、奇数空槽及8单位按钮矩形间距。Alt+N 增加 Slide：时间不动，交换开始／结束中心和宽度，左右侧分别 Si↔So，S/B不变，原批量撤销命令及 Arc 行为保留。

- 点立得开启时，只要有选中物件，菜单与普通选中菜单相同（共用 NormalSelected 的排序／隐藏配置），不再以创建菜单覆盖；设置中合并为“已选中（共用）”目录。所有环形页基础半径缩小15%，按实际按钮矩形重新扩展，至少保留8单位间距；弧形子菜单同样适用，竖向菜单不受影响。

- Legacy 设置列表按顺时针表示，运行菜单同方向；超过4项从左侧起，4项及以下从右侧起。默认创建目录依据 AA 序列反向为顺时针，并在 Tap 后追加 Slide：不创建、网格、单ArcTap、ArcTap、退出、Arc、Hold、Tap、Slide。显式自定义顺序仍优先，恢复默认可使用新目录；New 不变。
- 用户后续纠正：AA 仅参考菜单排序，不参考其按钮位置。Legacy 恢复原圆环布局、实际按钮边界避让与小幅间距均衡；顺时针排序保留，奇数项补齐到偶数槽（留一个空位）。取消 AA 的非线性角度与椭圆半径；New 菜单不变。
- 设置→常规新增“允许编辑地键宽度”，默认关闭并保存；开启后纯 Tap 选择显示宽度行，1为普通大小，0.5为一半，2为两倍，接受正有限数，支持多选、原命令撤销及既有AFF读写／CPU和驻留渲染宽度路径。关闭只隐藏编辑入口，不改谱面数据。参考 AA 的 Width 倍率及 AllowUnofficialParams 显示门控，未扩展其他非官方参数。
- 本次8项专项检查通过（`tap-width-aa-menu-results.xml`），C# warn-as-error 编译通过；未进行完整应用菜单截图或手动交互验收。

- Q 与中键拖动可同时进行：开始拖动不暂停播放，按 Q 不提前 Finish；Alt 整体拖动的定位面随对应时间的 Z 更新。拖动专用命令在播放时保留全谱判定，避免每帧编辑重置音效／粒子；只有移动到未来的目标解除自身 Judged。
- 时间拖动确定头尾后立刻按鼠标地面投影吸附最近可见节拍线，忽略普通5单位吸附距离，不等待2像素移动阈值，随后显示新端点网格面；网格关闭时使用原始时间，已有 ArcTap 约束和长物件最小时长约束保留。

- 最新修复：Slide F4 最新轮换仅 SS／SiSi／SoSo／BB 四种同侧缓动组合，其他组合无效。打击三角格按实际 mesh 顶点→1.7缩放→纹理 UV 逆算覆盖，修正随位置增大的偏离；进入新区域补0.2秒噪声历史，避免快速窄 Slide 等待随机点亮。
- 播放模式放开右键入口、后续操作调度、位置／时间选择、点立得选择分支及相关编辑热键的编辑模式限制；保留文本输入、模态对话框、菜单遮罩与操作互斥守卫。4 项相关检查通过（`slide-cycle-effect-results.xml`），完整播放模式右键交互尚未手动验收；本次未打包。
- 慢放只读对照见 `Documentation/AA-SLOW-AUDIO.md`，确认 AA 用 FMOD + NativeAudioHelper/NStretch，AE 用 BASS_FX Tempo 默认路径；本次未改音频后端，不将源码差异等同于已完成听感 A/B 根因验证。

- 整体移动比例修正：ArcXToWorld 和 Slide.WorldX 实际均为 `4.25 - 8.5*x`；Slide 的偏移直接加 delta.x，删除误加的 ×2。回归检查改为断言 Arc／Slide 世界位移一致，2 项移动检查通过（`slide-move-scale-results.xml`），warn-as-error 编译通过；未打包。

- 后续补齐：整体移动的定位面固定在最近可见端点的时间／Z，候选包含 Arc 头尾、Slide 四角和 single ArcTap 宿主头尾；排除 GroupHide、相机背后、屏幕外和渲染距离外端点。Slide 保留高度／宽度／曲线；纯 Y 移动不会让只有 Slide 的选择产生空撤销。
- 热键设置增加固定鼠标组合说明，列出中键模式、Alt+中键整体位置移动、Ctrl+左键多选／整链、Shift+左键时间范围选择、松开／Esc／Q 的提交取消规则及自由相机组合。
- Legacy 的两个 ArcTap 入口现进入共用 MenuChoice 配置目录，可分别排序／隐藏；默认单Arctap在前，旧排序缺少 single 条目时插在 Arctap 前。New 菜单保留原目录。3 项相关检查通过（`group-slide-results.xml`），warn-as-error 编译通过；未打包，未做完整应用手动交互验收。

- 创建 Arc 选择尾时间时不再每帧 ResetJudge；仅更新创建中的 Arc 的 Judged 状态，保留 AudioPlayed，避免 Q 播放时整谱重响。Slide 几何 Rebuild 调用 RebuildGroups(false)，保留音效／网格反馈时钟；增删、载入等仍默认重置。长物件粒子在 ongoing 编辑操作中首次发射启用预热，保持预览期间的粒子表现。
- Slide 左右曲线下拉框每次 RefreshShownValue，清掉同值 SetValueWithoutNotify 时残留的“混合”标题。
- 粘贴结果恢复选择时，single ArcTap 只选音符、不同时选隐藏宿主；普通 Arc 只选父 Arc、不同时选其全部 ArcTap，避免编辑面板只剩通用时间字段。
- F6／位置编辑下 Alt+中键按 AA `AdeValueEditor.MoveSelectedArcsPositionTask` 操作：按下时以全部选中物件中最近的可见端点为原点、Arc 同 XY 偏移、Slide 仅跟随 X 偏移，保持时间／曲线／相对位置，松开提交、Esc 取消、Q 仅切换播放且保留拖动。独立选中的 single ArcTap 自动映射到宿主，去重移动一次；支持 Arc／Slide／single ArcTap 混选，其他类型不参与该操作。AA 只读参考 `E:\AffdataDev\Checks\AAMove`。
- Legacy 环形菜单将 ArcTap 父菜单改为上一级“单Arctap”“Arctap”两项；Arc 的“更多类型”展开到 S/Si/So/B 同级，八种缓动按钮统一为当前字体下 SoSo 文本宽度加原有 24 单位内边距，取消早先 S/Si/So/B 单独加宽。仅环形 ShowAt 重排；New 竖向菜单结构保留。
- 验证：6 项相关 EditMode 检查通过（`creation-menu-results.xml`），warn-as-error 编译通过；未打包，音效／粒子及菜单外观未做完整应用实机验收。

当前 AFF 输出格式：`slide(startTime,endTime,startCenter,startWidth,endCenter,endWidth,leftCurve,rightCurve,height);`。以 `AffParser.Slide`、`RawAffSlide`、`ArcSlide.IsValid` 为准。

- 至少持续 2ms；横向中心不局限 0..1，宽度必须正且有限，曲线中间不能交叉成非正宽度。
- 左右曲线 0..3，高度枚举 0..2。`ArcSlide.MinX=-0.5/MaxX=1.5` 是默认预览范围，不是所有合法数据的硬限制。
- 当前 WorldHeight 使用固定三种高度；不要因最初提过 enwidencamera Y 跟随就声称已经实现连续跟随。
- 连成同物件：按时间稳定排序，同 timinggroup、同高度，从满足时间与重叠条件的未连接尾端中选择重叠最大的前段；并行 Slide 不再打断另一侧链；起点时间与前段终点误差绝对值 ≤2ms，横向接壤区间重叠 **严格 >0.01**，重叠宽度先保留六位小数避免边界浮点误差。
- 使用实际重叠端点消缝，包括超出 0..1 的 X，修复只消一半的接缝。逻辑链和视觉消缝都受重叠条件约束。
- 某些 group head 的线由 shader 特意不画，不能看到少一条线就直接放宽连接判定。
- 入口：`Assets/Scripts/Gameplay/Managers/ArcSlideManager.cs` 的 `UpdateConnections`，`Gameplay/Chart/ArcSlide.cs`，`Gameplay/Components/ArcSlideVisual.cs`，测试 `ArcSlideTests`。

### Slide 位置与曲线编辑（2026-11-08）

- 后续修正：拖动坐标信息使用实际拖动点，Slide 的 X 使用 Slide 坐标系；创建／转换默认宽度统一 0.5。Hold 也显示“转Slide”，保留时间、世界 X 和时间组并置于地面；Arc 按曲线中点 Y 距地面(-2/9)、天空(1)、高空(1.61)最近者选择高度。
- 最新验证：32 项拖动、合拢反复拉开、可见性、转换和连接专项检查通过（`slide-fixes-targeted-results.xml`），C# warn-as-error 编译通过。全 ArcSlideTests 运行 65/72 通过，7 个既有解析用例失败：3 个旧非法输入预期与现有合法格式不符，4 个保存重读用例有换行截取问题；本次未改解析器或这些旧用例。未打包、未做完整应用手动交互验收。

- 位置编辑（New F6）中键拖动：四个端点同时按屏幕距离选择，排除相机背后、屏幕外和渲染距离外的端点；只移动该边界，另一侧保持原位。宽度变化也参与提交／撤销判定，光标显示被拖动的边界。
- 物件编辑的开始／结束范围右侧复用 Arc 的移动按钮，移动该时间端的中心，保留宽度；左键确认，Esc 取消，恢复原时间组并可撤销。
- 2D 预览四角允许左右端点重合，包括不在 X 网格上的端点；重合存为“该端点坐标,0.020”，后续每次拖动均将 0.020 宽度视为两侧重合于中心，按拖动方向拉开，另一侧保持原中心，不累积半宽偏移。仍不允许左右边界交叉及曲线中间非正宽度。
- New F3 对 Slide 左右曲线分别执行 S↔B、Si↔So。F4 为 (S,S)→(Si,Si)→(So,So)→(B,B)→(S,S) 四组合循环，其余组合无效；接入原属性命令，支持多选和撤销。曲线切换导致非法形状时沿用合法性约束，不提交该物件。
- 已完成 Unity 编译（无 C# warning/error）及 10 项专项 EditMode 检查：四角合拢／拉开、头尾左右四种位置拖动、屏幕距离判定和曲线切换；结果 `E:\AffdataDev\Checks\slide-editing-results.xml`。本次仅修改源码，未打包，未进行完整应用手动交互／主题截图验收。

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

- 圆形右键菜单以等角度为基础，奇数项补成偶数槽位；按相邻按钮宽高在中心连线上的投影做30%的间距修正，每项角度偏移最多3°（且不超过基础角距10%），让边缘留白稍微均匀。先 Show 获取实际尺寸，微调后统一计算半径，保证按钮矩形之间至少12个Canvas单位间距。保留顺序和首项朝向，竖向菜单不受影响。2026-11-08 更新后源码编译无诊断；按用户9项截图估算的尺寸计算，边缘间距标准差10.14→8.00。未打包或在程序中目视验证此次微调。

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

## 2026-11-09 HUD 输入焦点与反转默认键

- New 预设 Arc／Slide 反转默认键改为 Ctrl+N，保留用户自定义绑定。
- 名称、作者、难度输入框禁用 Unity 自动方向键导航，仍允许鼠标点击编辑，避免浏览谱面时方向键把焦点转进 HUD 而导致 Q 写入元数据。
- 谱面内容接收中键／右键按下时也清除当前文本选择；InputSystemUI 默认只对左键背景点击执行取消选择，因此先前会残留输入焦点。Q 不强行覆盖主动文本编辑。
- 验证：HUD 自动导航实际可达性（修复前可进入作者框、修复后不可进入）及中键／右键取消文本选择、New Ctrl+N／预设冲突共8项专项测试通过。Windows_2026-11-09_11-04-13.log 构建成功，111.2秒，未检出C#／Shader编译警告错误，安装程序集SHA256一致。未进行物理Q实机输入验收。

## 2026-11-09 单 Arctap 混选拖动与点立得选中排序

- 普通中键：单 Arctap（直接选子 Tap 或选其1ms宿主）整体拖时间／位置，混选 Arc、Trace、Slide 时同一偏移批量移动；普通单个非宿主Arc／Slide保持原端点编辑。Alt中键位置移动共享目标解析，子Tap与宿主同时选中只移动一次。
- 时间整体移动给宿主及内部实际Arctap各建一次撤销命令，保持短宿主时长及子Tap相对时间，保留Clone未带Arc引用的关联。普通Arc内单独选中的Tap仍受原宿主时间边界限制。
- 最近可视端点定位使用该端点所属时间组，不再错误使用第一个目标组。位置移动时Slide仍仅X，Arc／Trace／单Tap宿主可XY。
- CreateSelected圆形默认顺序改为从左顺时针：不创建、删除、网格、单Arctap、Arctap、退出、Arc、Hold、Tap、转Slide（按类型过滤）；AA1.13.1同一创建目录加删除适用于Arc／Trace／Hold／Tap／Arctap，AE额外保留转Slide。设置同步；普通选中和未选中的创建菜单不改，奇数不补槽，保留自定义排序。
- 验证：single-tap-group-menu-results.xml 12/12专项通过，覆盖宿主／子Tap选择组合去重、整体时间偏移、位置不累积、Slide仅X、原单Slide四角拖动、播放撤销保留、环形按钮不重叠及新排序。Windows_2026-11-09_11-50-18.log构建52秒成功，无C#／Shader编译警告错误，已发布C:\AffdataEdit_custom；未做AA／AE逐类菜单实机同屏截图或完整鼠标拖动验收。

## 2026-11-09 单 Arctap 水平镜像

- Ctrl+M及右键水平镜像共享CreateMirrorCommands，复用中键拖动的宿主提升／去重：直接选单Arctap即镜像其1ms宿主，子Tap与宿主同时选中不重复执行。与Arc／Trace／Slide／Tap／Hold混选保留原镜像规则（含Arc颜色与Slide左右曲线互换）；普通Arc上的子Tap仍不单独水平镜像。所有命令在同一批撤销里，创建命令不修改原对象。
- 验证：single-tap-mirror-results.xml 3/3专项通过（子Tap／宿主／两者混选生成相同5目标批次，保留Slide曲线交换及Tap／Hold轨道镜像、原对象不提前修改）；Windows_2026-11-09_12-06-36.log构建59.2秒成功，无C#／Shader编译警告错误，已发布C:\AffdataEdit_custom。未做完整实机按键验收。

## 2026-11-09 竖向创建菜单同行与Hold图标

- Tap／Hold／Arc共用一行，Arc右侧48宽子菜单箭头保留在该行；蓝／红／其他共用一行，其他颜色标题简化为“其他”，保留子菜单。ArcTap／单ArcTap及不创建／退出原双列行不变。
- 竖向菜单布局支持连续编号多列：普通按钮均分剩余宽度、子菜单按钮保留固定宽度，宽度按各列内容需求扩展，分隔线与圆角仅作用于实际行及面板外缘。
- Hold图标保留竖向长方形外框，内部两条横线改为单条上下留空的中心竖线。
- 构建Windows_2026-11-09_18-56-33.log成功52.9秒，无C#／Shader编译警告错误，安装程序集SHA256一致，已发布C:\AffdataEdit_custom。本次未新增测试，未做完整实机深浅色菜单截图验收。
- 用户后续纠正：颜色行改为蓝／红／48宽纯箭头，去掉“其他”文字；Tap／Hold同行，Arc与自身子菜单箭头独占下一行。源码warn-as-error编译通过，无诊断；该后续调整尚未打包。
- 竖向Arc缓动区域顺序进一步改为S、B、Si、So；B位于Si／So之前。源码warn-as-error编译无诊断，尚未打包。
- Slide／转Slide图标移除中间横线，仅保留平行四边形外框；与前述菜单调整一起待打包。
- 单Arctap使用独立create-single-arctap图标：菱形中央实心圆点，普通Arctap保留空心菱形；竖向单Arctap项改用正确图标ID。尚未打包。
- 上述后续菜单布局、S/B/Si/So排序、Slide空心外框及单Arctap中心点已打包：Windows_2026-11-09_19-04-00.log构建成功47.5秒，无C#／Shader编译警告错误，安装程序集SHA256一致。发布C:\AffdataEdit_custom，替代此前尚未打包状态。

## 2026-11-09 菜单可用区域及物件编辑首次全选

- 竖向主菜单及级联子菜单按四周可见BarView实际世界边界转换到EditorCanvas坐标限位，底边不进入底栏，其他边同样不覆盖外围栏；隐藏栏不占区域。菜单超过区域时压缩行高／宽度以保持面板位于区域内。
- AdeTextSelection.Configure用于物件编辑时，首次普通单击从最近数字片段改为整框全选；后续单击定位光标，原双击／三击／Shift与拖选保留。ConfigureSettings显式保留原设置框首次片段规则，避免扩大修改范围。
- 验证：menu-bounds-select-all-results.xml 3/3通过（1280x720、800x600四边工具栏排除／隐藏底栏恢复、实际TMP首次全选及后续光标定位）。Windows_2026-11-09_19-13-44.log构建59秒成功，无C#／Shader编译警告错误，安装程序集SHA256一致，已发布C:\AffdataEdit_custom。未做全部实际菜单深浅色截图验收。

## 2026-11-09 Slide整链判定时钟（取代逐物件计数）

- 判定从逻辑链头时间开始连续计算，采用链头BPM及谱面密度。分段只分摊整链的判定点，不在接缝重新计时或补尾判；整链只由链尾补最后余量判定。总判定数、Combo、PURE共用该规则。
- UpdateConnections同步重建每段的前序时间边界及链终点，不改变原连接条件。±2ms接缝容差在计数上连续覆盖：微小间隙不延迟原判定，重叠不重复计数；断链和重连后重新计算。
- 示例：BPM120默认密度，600ms单段及300+300ms同链都为3判；互不连接的两条300ms仍各自2判。单段Slide现有BPM阈值、密度、noinput和尾判>=2ms规则保留。
- 验证：源码warn-as-error编译无诊断；slide-chain-judgement-results.xml 17/17专项通过，覆盖多次拆段及回退、密度、零／负BPM、接缝间隙／重叠、断链重连和原单段尾判。未打包、未做完整实机播放验收。
- 整链判定修复已打包：Windows_2026-11-09_19-31-59.log构建成功56.8秒，无C#／Shader编译警告错误，安装程序集SHA256一致，已发布C:\AffdataEdit_custom，替代前述未打包状态。

## 2026-11-09 设置新增编辑分类

- 设置导航第三项新增“编辑”，顺序为常规、外观、编辑、音频、网格、右键、热键、账号、存储、关于。
- 常规中的CancelLimit（撤销缓存）、在粘贴时修正时间、允许编辑地键宽度、谱面保存排序、中键拖动模式、显示判定详情、显示频谱、频谱亮度移入编辑分类；分辨率／全屏／帧率留在常规。只改变原控件借用时的分组，复用原回调和保存字段，不重置用户设置；新增分类使用已有指针图标，避免查找不存在的Editing侧栏图标。
- 验证导航枚举和标题均为10项，编辑为第三项；Windows_2026-11-09_22-59-21.log构建成功48.6秒，无C#／Shader编译警告错误，安装程序集SHA256一致，已发布C:\AffdataEdit_custom。仅UI分类调整，未新增测试，未做完整深浅色实机截图验收。

## 2026-11-09／10 侧栏图标、Timing与皮肤切换统一

- 设置右键图标采用同套24单位、圆角2单位线条SVG栅格资产，右上按键实心；右侧Timing入口使用普通时钟。FileWatch保留文件夹轮廓，在右下角加同步双箭头，替换临时程序网格图形；悬停名为“谱面文件实时同步”。打开入口提示“打开谱面文件夹”；右侧Timing提示“Timing列表”，底栏及widget页签／当前组标题改为Timing。原场景对象名称保留，避免破坏脚本路径。SVG源和映射在Documentation/UI/Icons。
- Timing／SC／Camera、Timing／属性、皮肤预设／自定义、快速工具／外置工具均使用AdeAnimatedTabs：统一暗色圆角边框底槽、2单位内缩滑块、居中文字、0.26秒轻微OutBack移动。恢复3单位粗直线下划线，各标签长度单独测量乘1.25并限制在滑块内，长度和位置共用同一动画进度。底槽按实际按钮边界计算，适应窗口尺寸；布局组件忽略装饰层。工具页删除单独的重复动画实现。
- 皮肤侧卡宽度上限760且不超过Canvas宽减100，内置／外置／自定义页背景统一三列。自定义搜索位于“重载皮肤／背景文件夹／重载背景”操作行下；与预设搜索共用44高输入框、20号正常字体、12单位水平内边距，主题边框和提示色一致。搜索复用原匹配，内外背景均过滤，普通匹配不区分大小写。
- 快速工具／外置工具切换栏和上方操作行统一44高，双行容器96高；工具标题、参数描述、输入框对齐保持之前修正。
- 两个俯视模式切正交投影时，原1000距离的ScreenSpaceCamera背景平面可能保留透视尺寸，产生巨幅拉伸／单色效果。ArcBackgroundEdgeFill在投影更新之后按相机实际逆投影拟合原背景Canvas为世界平面，保留背景自身层级、暗化及补边，不增加相机、RT或全屏合成遮罩。随相机位置、旋转、投影和窗口宽高更新；切回普通视角也重新拟合。新针对性测试在1920×1080、1200×900连续切换普通→俯视一→俯视二→普通，四角屏幕位置与1000距离均正确，2/2通过（Checks/camera-background-switch.xml）。
- 自由相机鼠标中键旋转系数从0.4再次减半至0.2；移动、手柄和滚轮速度不改。
- 按用户要求不做实机界面审查，保留必要编译与上述相机针对性验证。最终打包状态见本段后续记录。
- 已打包发布C:\AffdataEdit_custom：Windows_2026-11-10_00-05-26.log构建成功，C#／Shader编译警告错误0，安装Arcade.dll与构建产物SHA256一致。相机专项2/2通过；未做实机界面审查。

## 2026-11-10 文件同步与右键图标修正

- folder-sync：同步角标由0.42放大到0.5，线宽折算为2单位与全套一致；文件夹底边／右边缩短让位，整体落在2–22单位安全区内，PNG有效范围底部224与Save一致，不再偏低。
- mouse-right：机身加宽为14×18单位（原12×18），右上按键保持实心；去掉中间竖分隔线，左键空白区直接以实心区左缘为界，使分隔线落在机身正中（原竖线与实心区合并后视觉分界偏左约1单位）；PNG由128×128改为256×256与其他图标一致。MouseRight.png.meta中误抄自Save的精灵名Save_0及ID改为MouseRight_0与新ID（仅脚本Resources.Load引用，不影响场景）。
- 仅资源修改，无代码改动；尚未打包，未做实机界面审查。
- 侧栏／设置图标锯齿：256图标贴图缩到约24–36像素显示时未开Mipmap，双线性只采4个texel，导致账号等细线图标左右线宽不一、圆形发虚起伏。为README映射的工具栏／设置图标及MouseRight统一开启Mipmap并改为三线性过滤；仅改.meta导入设置，PNG与引用不变。尚未打包。
- 账号图标改用本地user-bust：头部圆半径3.75，下方改为闭合半椭圆胸像（宽15单位、高5.5单位），与头部留约1.75单位间隙；替换Tabler user原本窄而开口的“∩”形肩部，视觉重量与齿轮／键盘一致。Account.png保持256×256，GUID与引用不变。尚未打包。
- 设置“编辑”分类图标由借用的create-toggle指针程序图形改为Tabler pencil铅笔栅格图（Resources/AffdataEdit/Icons/Edit.png，256×256，开Mipmap＋三线性）；AdeSettingsDialog.AddIcon中与右键分支合并为同一Image加载路径。ContextMenuIcon本身未改，标记菜单中的create-toggle不受影响。尚未打包。
- AdeAnimatedTabs统一修正：滑块内缩由每边2单位改为3单位（原2单位被底槽1像素边框吃掉，实际只剩约1像素，贴边显得局促）；下划线不再固定贴在滑块底边上方2单位，改为按标签字体下伸线（TMP lineInfo.descender）定位并下移1单位，最低保持滑块底边上方2单位，因此不同按钮高度／字号下与文字间距一致。按钮过矮（如Timing／SC／Camera）时，所有标签通过TMP底部margin统一上移（最多12单位），保证下伸线下方仍容得下1单位间隙＋3单位下划线＋2单位底距，含g的标签不再与下划线重叠；Dispose时恢复原margin。下划线3单位粗、长度乘1.25及动画不变。尚未打包。
- 分段切换统一尺寸：AdeAnimatedTabs构造时统一标签20号字（关自动缩放）、按钮LayoutElement高44、横向间距8。原Timing／SC／Camera与Timing／属性两组场景值为22号字、按钮高40、间距4，与皮肤（20／44／8）和外置脚本（44高）不一致，字大且按钮矮是下划线压字的主因。仅改共用组件，未改场景文件。尚未打包。
- 分段切换与上方按钮协调：共用按钮高改为48（滑块内缩后42，接近上方44高操作按钮，原44高时滑块仅38显得偏小）；外置脚本分类行44→48、双行容器96→100。圆角改为：滑块保持控件标准半径8（与上方按钮一致），底槽改用新增AdeUiTheme.SegmentTrackSurface（半径8＋内缩3＝11，边框仍1像素），保持同心；此前把滑块缩到半径5显得过方、与其他按钮不协调。皮肤与Timing两组的父行本来就是48高，无需改场景。尚未打包。
- 皮肤侧卡“重载皮肤／背景文件夹／重载背景”三按钮原为场景旧样式（常态透明、悬停0.86浅灰底＋白字，悬停时文字几乎看不见），改用与其他面板按钮相同的带边框控件底（新增AdeUiTheme.UseControlSurface，复用StyleControl的ThemeControl/UISprite与状态色），标签用主题文字色；在UpdateTabs中随主题刷新。尚未打包。
- OBS图标（Textures/Icon/OBS.png）同样补开Mipmap＋三线性：256贴图缩到约40像素时无Mipmap导致三条分隔缝断裂、边缘锯齿。仅改.meta，OBS品牌图形本身不变。尚未打包。
