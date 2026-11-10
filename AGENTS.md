# 项目协作要求

- Windows 本地打包统一放在 `C:\AffdataEdit_custom`，更新同一个目录，不另建 `latest` 等后缀目录。
- 现有增量打包入口：`Build-Windows.cmd`／`Tools/Build-Windows.ps1`，便捷入口 `E:\AffdataDev\Build-AffdataEdit.cmd`。复用 `E:\AffdataDev\BuildWorkspace` 缓存，不每次复制整个项目。发布保留用户设置和谱面；详见 `Tools/BUILD-WINDOWS.md`。
- 历史交互规则、默认值和验证状态保留在项目外层 `E:\AffdataDev\Documentation\LOCAL-DEVELOPMENT.md`（各开发助手共用）；涉及相关修改时按需读取，以当前用户要求和源码为准。不在仓库中新增本地交接或临时分析文档。浅色／深色 UI 都要保持文字可读和选中状态明显。

- 专注于用户当前要求的最小交付实现，不做无关重构、额外功能或扩大改动范围。
- 不做多余的测试。仅执行确保本次改动正确且可交付所必需的编译检查或针对性验证；不要默认添加测试、运行完整测试套件或重复验证。
- 新增和修改的代码必须做到 **0 warning、0 error**。交付前完成必要检查，修复本次改动引入的编译及运行时警告、错误，不通过屏蔽警告或吞掉异常来满足要求。
- 若已有警告或错误影响本次交付，应如实说明，不能将未验证的结果宣称为 0 warning、0 error。

## 参考项目

- **首要参考：Arcade Alpha**，路径：`/Volumes/T7/Affdata/References/Arcade-Alpha`。功能行为、编辑交互、渲染及视听资源优先参考该项目。
- Windows 编译版只读参考：`E:\AffdataDev\Arcade-Alpha_v1.12.2_read`（用户已搬移并改名）。读取配置、反编译与资源分析方法见 `affdataedit-dev/references/arcade-alpha-read.md`；它是发布包，不是上述源码项目。
- **次级参考：AffdataPlay**，路径：`/Volumes/T7/Affdata/AffdataPlay`。用于补充参考；与 Arcade Alpha 存在差异时，默认以 Arcade Alpha 为准，用户明确指定的要求优先。
- 参考项目默认只读；改动应落在当前 AffdataEdit 项目中，除非用户明确要求修改参考项目。

- 用户2026-11-08新授权：打包替换时可以直接关闭 `C:\AffdataEdit_custom\AffdataEdit.exe`，不保存、不再询问；仅限该完整路径的定制版，不影响 official、AA 或其他程序。
