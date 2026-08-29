# QuickTranslate

在任意可编辑输入框中快速连续按三次空格，将当前输入框中的中文通过 CC Switch 当前 Codex API 翻译为自然英文，并在原位置替换。程序常驻系统托盘或 macOS 菜单栏，不打开独立翻译窗口。

## 功能

- Windows 与 macOS 原生实现
- 三击空格触发，间隔上限为 0.7 秒
- 自动全选当前输入框并原地替换
- 每次请求动态读取 CC Switch 写入的 `~/.codex/config.toml` 与 `auth.json`
- 支持 Codex `responses` 协议与 OpenAI 兼容的 `chat/completions` 协议
- API Key 不复制到应用配置，也不写日志
- 翻译后恢复原剪贴板
- 等待 API 时如果用户切换窗口，取消自动粘贴并把译文留在剪贴板
- 密码框与非编辑区域不触发
- Windows 与 macOS 均支持登录后自动启动

## 使用

1. 在 CC Switch 中启用一个 Codex 供应商。
2. 启动 QuickTranslate。
3. 将光标放在 Codex、浏览器、微信、Word 等程序的输入框中。
4. 输入中文后快速连续按三次空格。
5. 等待翻译完成，期间不要切换窗口或继续修改输入内容。

前两次空格会短暂出现在输入框末尾，触发翻译后会随原文一起被英文替换。

## Windows

系统要求：Windows 10/11 与 .NET Framework 4.8。

构建：

```powershell
./windows/build.ps1
```

输出：`artifacts/windows/QuickTranslate.exe`

程序使用低级键盘钩子识别三击空格，通过 UI Automation 判断当前焦点是否属于可编辑控件，再用 `Ctrl+A`、`Ctrl+C` 和 `Ctrl+V` 完成读取与替换。开机启动项写入当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。

微信、飞书等 Chromium/自绘输入框使用应用兼容识别路径。键盘钩子中不执行 UI Automation 查询，避免目标程序响应较慢时被 Windows 停用钩子。排障日志保存在程序同目录的 `logs/quicktranslate.log`，只记录应用、控件类型、字符数与错误，不记录输入正文、译文或 API Key。

## macOS

系统要求：macOS 13 或更高版本，构建时需要 Xcode Command Line Tools。

构建：

```bash
chmod +x ./macos/build.sh
./macos/build.sh
```

输出：`artifacts/macos/QuickTranslate.app`

将应用移动到 `/Applications` 后启动。首次启动时，macOS 会要求辅助功能权限：

1. 打开“系统设置 → 隐私与安全性 → 辅助功能”。
2. 允许 QuickTranslate。
3. 退出并重新打开 QuickTranslate。

macOS 版使用 `CGEventTap` 识别三击空格，通过 Accessibility API 判断输入控件，再用 `Command+A`、`Command+C` 和 `Command+V` 原地替换。登录启动通过 `SMAppService` 注册，可在“系统设置 → 通用 → 登录项”中查看。

当前构建使用临时签名，首次打开可能需要在 Finder 中右键应用并选择“打开”。正式分发应使用 Apple Developer ID 签名并公证。

## GitHub Actions

每次推送到 `main` 都会构建两个可下载产物：

- `QuickTranslate-Windows`
- `QuickTranslate-macOS`

在仓库的 Actions 页面打开最新一次 `Build`，即可下载对应平台的 artifact。

## 翻译规则

默认提示词要求模型：

- 翻译为自然、简洁的英文
- 保留语义、语气、段落、Markdown、名称、数字、网址与代码片段
- 只返回译文，不提供解释或注释

## 隐私

输入内容会发送到 CC Switch 当前供应商配置指向的第三方 API。QuickTranslate 本身不记录输入、译文或 API Key。请勿在没有授权的情况下翻译敏感内容。
