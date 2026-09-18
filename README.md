# Windows Clipboard Manager

Windows 剪贴板历史工具：常驻托盘，热键唤出面板，记录**文字 / 富文本 / 图片 / 文件**四类内容，
支持搜索、收藏、双上限自动淘汰、敏感信息脱敏显示与深浅色跟随。纯本地运行，**不做任何联网**。

- 平台：Windows 10/11 x64
- 技术栈：.NET 8 + WPF；SQLite（`Microsoft.Data.Sqlite`，手写 SQL，不用 EF）；手写 P/Invoke（不引 CsWin32）
- 默认热键：`Ctrl + Shift + V`（可在设置中修改）

## 功能

| 能力 | 说明 |
| --- | --- |
| 捕获 | 监听 `WM_CLIPBOARDUPDATE`，按优先级一次开锁读四种格式；用 `GetClipboardSequenceNumber` 去重（一次复制系统会投递 3 条通知） |
| 粘贴 | 单击或 `Enter` 回写剪贴板并粘贴到原窗口；可选自动发送 `Ctrl+V`（`autoPaste`） |
| 搜索 | `LIKE` 匹配文本内容与文件名，高亮命中片段；搜索在数据库侧过滤 |
| 类型标识 | 文本 / 图片 / 文件 / HTML / **网站**（识别出单个网址时）各有独立颜色与矢量小图标；配色低饱和，只做区分不抢文字 |
| 收藏 | 右键菜单收藏，收藏项**永不参与自动淘汰**；列表带 ★ 标记 |
| 上限 | 条数上限（默认 100 条）+ 磁盘占用上限（默认 500MB，可选 200MB/500MB/1GB/2GB/不限）；超限按「非收藏优先 → 体积大者优先 → 同级最旧优先」淘汰；只剩收藏仍超限时提示用户，不自动删除 |
| 隐私 | 敏感信息（手机号/身份证/银行卡/邮箱/密钥等）**只显示前几位**（`maskSensitiveData`，默认开）；只影响显示，粘贴仍是原文；遵循系统 `CanIncludeInClipboardHistory` / `CanUploadToCloudClipboard` 跳过密码管理器 |
| 设置 | 条数/磁盘上限、热键、开机自启（含延迟启动）、按进程名排除、是否记录图片、脱敏开关、主题、点击面板外部自动隐藏、亚克力强度；面板底部齿轮按钮可直达 |
| 存储占用 | 设置页实时显示「当前占用 / 上限」与明细（数据库、缓存文件数、记录条数），并可一键打开缓存目录 |
| 托盘 | 显示面板 / 设置 / 清空历史（二次确认）/ 开机自启 / 退出；资源管理器重启后自动重挂 |
| 外观 | 应用图标（exe / 窗口 / 托盘同一份 ICO）、圆角窗口与控件（按钮、输入框、下拉框、勾选框、滚动条、菜单）、深浅色主题（跟随系统 / 强制浅 / 强制深，`WM_SETTINGCHANGE` 即时切换） |
| 交互 | 点击面板外部自动隐藏（附加项 B-01，默认开，可在设置里关掉）；Esc / 热键 / 关闭按钮同样可收起 |
| 亚克力 | 面板支持系统亚克力（模糊 + 染色）背景，强度 0–100 可调（附加项 B-04）：0 = 不透明，越大越透；设置页拖动即时预览，取消自动回退 |

## 使用

1. 运行 `ClipboardManager.exe`，托盘出现图标（若被 Windows 收进「隐藏的图标」，可拖到可见区）。
2. 按 `Ctrl + Shift + V` 唤出面板；`↑↓` 选择、`Enter` 粘贴、`Delete` 删除、右键收藏、`Esc` 关闭。
3. 托盘图标右键 → 设置：修改上限、热键、开机自启、排除应用、主题等。
4. 数据全部写在**程序目录**：`data/history.db`（记录）、`data/blobs/`（图片本体）、`data/logs/app.log`（日志）。
   换机器/U 盘直接整目录拷走即可，**不写 `%APPDATA%`、不写注册表**（自启项 `HKCU\...\Run` 除外）。

### 命令行参数

| 参数 | 作用 |
| --- | --- |
| `--diag` | 输出诊断日志（启动耗时、目录定位、托盘/主题/淘汰等 DIAG 行） |
| `--duration=N` | N 秒后自动退出（用于体积/内存/冷启动的自动化测量） |
| `--delay=N` | 延迟 N 秒再开始监听（开机自启时避开开机高峰） |

## 应用图标

图标由 `src/ClipboardManager.App/Assets/app-icon-source.png`（方形源图）经脚本生成：

```powershell
.\scripts\make-icon.ps1 -Source src\ClipboardManager.App\Assets\app-icon-source.png
```

脚本会裁掉源图留白、缩放到 16/32/48/64/256 五个尺寸、把圆角方块外侧做成透明（避免深色任务栏上出现白色方角），
再组装成 `Assets\app.ico`。该 ICO 同时用于：exe 文件图标（`ApplicationIcon`）、窗口图标与托盘图标
（作为嵌入资源在运行时读取，缺失时回退到代码绘制的剪贴板图标）。

## 构建与测试

需要 .NET 8 SDK。脚本已封装 SDK 路径（本机装在 `D:\dotnet`，见 `scripts/_dotnet.ps1`）。

```powershell
.\scripts\build.ps1     # 构建解决方案（Debug）
.\scripts\test.ps1      # 运行全部测试（xUnit，当前 195 项）
```

> 若 PATH 里的 `dotnet` 指向没装 SDK 的目录（例如只有运行时的 `C:\Program Files\dotnet`），
> 会报 `No .NET SDKs were found`；脚本里统一用完整路径 `D:\dotnet\dotnet.exe`。

### 发布（双形态）

```powershell
.\scripts\publish-fd.ps1   # 形态 B：框架依赖单文件，需目标机装 .NET 8 Desktop Runtime
.\scripts\publish-sc.ps1   # 形态 A：自包含单文件，免运行时
```

| 形态 | 产物 | 体积 | 适用 |
| --- | --- | --- | --- |
| B（默认） | `artifacts/publish-fd/ClipboardManager.exe` | **2.52 MB** | 目标机已装 .NET 8 Desktop Runtime |
| A | `artifacts/publish-sc/ClipboardManager.exe` | **64.01 MB** | 免运行时、任意 Windows x64 |

> 关于体积：需求原期望「≤25MB」。WPF 的裁剪在 .NET SDK 中**已被禁用**（官方《Known trimming
> incompatibilities》），Native AOT 也不支持任何桌面 GUI 框架，因此「免运行时 + ≤25MB」在 C#/.NET
> 桌面栈内不可兼得。当前给出的是两条现实路径；若必须 ≤25MB，只能换 Avalonia+NativeAOT（约 25–35MB，
> 压缩后更小）或换语言/运行时（C++/Rust，如 Ditto 约 5MB）。

## 架构

```
src/
  ClipboardManager.Core/    纯逻辑：格式解析/生成（DIB、HTML Format、DROPFILES）、搜索片段、
                            脱敏、淘汰规划、主题解析、自启命令行、ICO 写出   ← 无 Win32、无 WPF，可单测
  ClipboardManager.Interop/ Win32 封装：隐藏消息窗口、剪贴板读写、热键、托盘、自启项
  ClipboardManager.Storage/ SQLite 仓储（手写 SQL + 预编译语句）、blobs 本体存储（哈希分桶、原子写）
  ClipboardManager.App/     WPF：面板窗口、设置窗口、主题字典、托盘图标绘制、AppHost 编排
tests/ClipboardManager.Tests/  xUnit（195 项）
docs/  产品计划文档.md（决策基线 D-01~D-13）· 技术设计文档.md（TDD）
```

关键设计点：

- **STA 亲和**：剪贴板 API 有线程亲和性，读写在 STA 线程完成，跨线程只传已拷进托管内存的纯数据。
- **隐藏消息窗口**：接收剪贴板/热键/托盘回调。用 `WS_POPUP | WS_EX_TOOLWINDOW` 的**隐藏顶层窗口**
  而不是 `HWND_MESSAGE` —— 后者无法成为前台窗口，`TrackPopupMenuEx` 弹不出托盘菜单。
- **托盘图标**：代码运行时生成 PNG → ICO 字节 → `CreateIconFromResourceEx`（注意该 API 收的是
  「单个图像」，不是整份 .ico）→ `Shell_NotifyIconW`；处理 `TaskbarCreated` 重挂、退出时先 `NIM_DELETE` 再 `DestroyIcon`。
- **图片本体分离**：数据库只存摘要与哈希，本体写 `data/blobs/<前2位>/<哈希>.png`；缩略图 512px 长边。
- **淘汰先提交事务、后异步删文件**，避免长事务；收藏项不参与自动淘汰。
- **脱敏只在展示层**：`SensitiveMasker` 用 `NonBacktracking` 正则 + 超时（剪贴板输入不可信），
  银行卡需通过 Luhn 校验才脱敏以压误报。

## 验收对照（需求 §8）

| # | 标准 | 结果 |
| --- | --- | --- |
| 1 | 冷启动 ≤ 500ms | ✅ Release 单文件 236.9ms；便携形态实测 91ms |
| 2 | 面板出现 ≤ 100ms | ✅ 稳态 7.2ms（首次 84.7ms，渲染栈初始化） |
| 3 | 20 条混合类型识别无误 | ✅ 文字/HTML/截图/文件 5/5/5/5 |
| 4 | 重启后历史保留 | ✅ 记录、收藏标记均保留 |
| 5 | U 盘换机可读（便携） | ✅ 整目录拷贝即用，数据在 exe 同目录 |
| 6 | 工具自身粘贴不产生重复记录 | ✅ 凭序列号去重 + 自循环防护 |
| 7 | 其他程序占用剪贴板时仍能捕获 | ✅ 占用率约 81% 时 6/6 捕获、0 错误（读重试 6×50ms） |
| 8 | 点击面板外部自动隐藏 | ⛔ 按开工决策②**主动取消**（改 Esc / 热键 / 关闭按钮），避免误关；自动隐藏列为附加项 |
| 9 | 数据目录位于程序目录 `data/` | ✅ 实测 `data/history.db`、`data/logs/app.log`，未写 `%APPDATA%` |
| 10 | 产物体积符合预期 | ⚠️ 形态 B 2.52MB ✅ / 形态 A 64.01MB（WPF 无法裁剪，说明见上） |

## 已知限制

- 剪贴板数据**未加密落盘**（原计划的 Q-1 未做）；脱敏只防「屏幕被看到」，不防「文件被拷走」。
- 图片记录按规范化像素哈希去重，同一图片重复复制不会新增记录。
- 托盘菜单、面板右键菜单用 Win32/WPF 原生实现，未做自绘皮肤。
- 首次运行需自行确认 Windows 未把托盘图标折叠进「隐藏的图标」。
