# 构建与增量更新

主项目仓库：<https://github.com/anqtal/AffdataEdit>

独立更新器：<https://github.com/anqtal/AffdataEdit-Updater>

构建 Windows x64 和 macOS（仅 Apple 芯片）。每次 push 到 master 自动构建；本地未 push 的 commit 不会触发。
只通过 R2 发布，不再发布 GitHub Release 或 artifact，安装和更新都通过独立更新器，
只提供最新版本。各平台构建成功后上传 R2，并获取独立更新器的最新 Release，校验 SHA-256
后单独发布到 R2 的 `updater/<平台>/`。构建失败保留上一版。

显示版本使用 UTC 构建日期，格式为 `yyyy.MM.dd`（例如 `2026.11.12`），由 Windows 和 macOS 的构建方法自动填写。更新清单继续使用完整 commit SHA 作为构建身份，同一天的多次发布也能正常检测和更新。

## GitHub 配置

主仓库 Actions Variables：

- `R2_ACCOUNT_ID`：Cloudflare Account ID。
- `R2_BUCKET`：`affdataedit-updates`。
- `R2_PUBLIC_URL`：bucket 公开 HTTPS 根地址，不带 `/windows` 或 `/macos`。

主仓库 Actions Secrets（不要提交凭据到仓库）：

- `R2_ACCESS_KEY_ID`、`R2_SECRET_ACCESS_KEY`：仅此 bucket 的对象读写权限。
- `UNITY_EMAIL`、`UNITY_PASSWORD`。
- `UNITY_LICENSE` 或 `UNITY_SERIAL`：与 GameCI 激活流程兼容的许可证。

Unity 版本自动读取 `ProjectSettings/ProjectVersion.txt`。Windows 和 macOS 目标都使用
Linux runner 交叉构建，需要 GameCI 对应版本的编辑器镜像。
`WindowsCiBuild.Build` 只构建主场景，并拒绝带构建 warning/error 的产物。
Windows 文件选择器先在 Windows runner 用 MSVC 编译 `Native/Windows/FileBrowser.cpp`，
再通过 CI artifact 放入 `Assets/SFB/Plugins/Windows`。它直接调用系统 IFileDialog，
不依赖 Unity 不支持的 Windows Forms。CI 允许这一个生成的未跟踪 DLL 参与构建。

## R2 发布约定

`<前缀>` 为 `windows`、`macos`、`updater/windows`、`updater/macos`：

- `<前缀>/objects/<SHA256>`：不可变文件对象，重复内容跳过上传。
- `<前缀>/releases/<commit>.json`：各版本完整文件清单。
- `<前缀>/latest.json`：客户端下载的最新清单，最后上传，`Cache-Control: no-store`。

macOS 清单路径以 `AffdataEdit.app/` 开头（`updater-config.json` 和 `build-version.txt`
在其旁边），有执行权限的文件标记 `"executable": true`。Burst 的 `*_DoNotShip`
调试信息不发布。

使用自定义域名时，不要让 CDN Cache Rule 覆盖 latest.json 的 no-store。
目前可使用 bucket 的 r2.dev 公开地址，它有限流，正式分发建议绑定自定义域名。
旧 R2 对象不删除，避免正在下载旧清单的客户端失败。

## 客户端

第一次从 <https://github.com/anqtal/AffdataEdit-Updater/releases> 下载更新器并运行，
它下载完整程序并把自己复制到安装目录：Windows 为更新器所在目录，macOS 为
`/Applications/AffdataEdit/`（未签名，首次需在“系统设置 → 隐私与安全性”中允许）。

AffdataEdit 启动时检查最新版本，有新版本时点“立即更新”打开更新器并退出。更新器按
文件大小和 SHA-256 只下载新增或修改的文件，按 Enter 应用更新并启动程序。更新器只更新
AffdataEdit；AffdataEdit 在后台更新更新器，双方都不替换正在运行的自己。

Windows 不删除本地额外文件；macOS 整体替换 `AffdataEdit.app`，删除新版已没有的文件。
这是整个文件级的增量更新，大型 Unity 资源文件变化时仍需下载完整文件。
