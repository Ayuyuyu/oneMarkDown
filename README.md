# OneMarkDown

给**经典版 OneNote 桌面端**（ex-OneNote 2016，支持 COM 加载项）用的 Markdown 粘贴工具。

复制一段 Markdown → 点「粘贴 Markdown」或按一次热键 → 光标处直接出现排版好的内容，不用先粘纯文本再手动转换。

## 两种形态

| 形态 | 入口 | 适用 |
| --- | --- | --- |
| **A. 加载项** | OneNote 功能区「Markdown」选项卡 | 经典桌面版 OneNote |
| **B. 热键工具** | 托盘图标 + 全局热键 | 任何版本 OneNote，也能粘到 Word / VS Code 等 |

两者共用同一套渲染内核（Markdig → OneNote 友好 HTML → CF_HTML → 剪贴板），`dist/` 里同时带上两者。

## 它能做什么

- 把 Markdown 渲染成 OneNote 富文本，插入到当前光标处，并且是**一个可撤销步骤**
- 支持：多级标题、段落、加粗/斜体/删除线、有序与无序列表（含嵌套）、任务列表、表格、引用块、代码块、行内代码、链接、图片、分隔线
- 「从 .md 文件插入」：文中相对路径引用的图片自动按 base64 内嵌
- 自动粘贴失败时内容已在剪贴板，手动按 `Ctrl+V` 结果完全一样
- 纯本地运行：不联网、不上传笔记内容

## 它做不到什么

- 不支持新版统一 OneNote（Store 版）与 OneNote for Windows 10 —— 它们没有 COM 加载项模型（热键工具仍可用）
- 不做「已渲染内容转回 Markdown 源码」的反向往返
- 不做实时预览/实时渲染：OneNote 没有富文本编辑扩展点，官方 API 也定位不到光标
- 代码块只有等宽字体 + 灰底，没有语法高亮
- 不支持 LaTeX 公式

## 环境要求

| 项 | 要求 |
| --- | --- |
| OneNote | 经典桌面版（`ONENOTE.EXE`，界面左上角有「文件」菜单） |
| 运行时 | .NET Framework 4.8（Win10 2004+ / Win11 自带） |
| 编译 | .NET SDK 或 VS 2022（「.NET 桌面开发」工作负载） |
| 安装权限 | 写 HKLM 需要管理员（脚本会弹 UAC） |

## 安装

把 `dist` 里的 `OneMarkDown-v<版本>-<时间戳>.zip` 解压到任意位置，进入 `installer` 目录后运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1 -Machine
```

脚本只有一个参数 `-Machine`，产物的位置由它自身的路径推导（同级 `AddIn\` 目录），不依赖其它任何目录。它依次完成：

1. 校验 .NET Framework 与经典版 OneNote
2. 结束占用旧 DLL 的 `dllhost.exe` 代理进程（升级时必需）
3. 复制文件到 `%LOCALAPPDATA%\OneMarkDown\bin`
4. `Unblock-File`
5. 写 COM 注册（CLSID + `AppID`/`DllSurrogate`）与 OneNote 加载项项
6. 清理宿主残留的禁用记录，并打印自检表格

装完**完全退出 OneNote**（任务管理器确认没有 `ONENOTE.EXE`）再打开，功能区顶部应出现「Markdown」选项卡。

卸载：

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\uninstall.ps1
```

> 改了代码要先运行 `tools\build.ps1`（编译并刷新 `dist\`）再装。

## 用法 A：加载项

1. 在任意编辑器里写 Markdown，复制
2. 切到 OneNote，把光标点到目标位置
3. 点「Markdown」→「粘贴 Markdown」

图片建议走「从 .md 文件插入」：剪贴板模式没有基准目录，`![](./img/a.png)` 这种相对路径无法解析，会在正文里留一行红字提示。

## 用法 B：热键工具

双击 `OneMarkDown.exe`，常驻托盘、无主窗口、不需要管理员。启动时会弹一条提示条写明本次实际生效的热键。

| 热键 | 作用 |
| --- | --- |
| `Win+Alt+V` | 渲染剪贴板里的 Markdown 并自动 Ctrl+V |
| `Win+Alt+O` | 选择 `.md` 文件插入 |
| `Win+Alt+Q` | 退出 |

热键是**按候选列表自动回退**的：`Win+Alt+M` 被占用就换 `Win+Alt+V`，再不行用 `Ctrl+Alt+M`。实际绑定结果写在提示条和日志里。

托盘菜单：粘贴 Markdown / 从 .md 文件插入 / 打开日志目录 / 关于 / 退出。双击托盘图标打开「关于」，不会直接往笔记里粘东西。

粘贴目标不限定 OneNote：只要前台不是本程序、也不是桌面或任务栏，就直接发 `Ctrl+V`。所以 VS Code、记事本、Word、聊天框都能用（能否显示格式取决于目标是否读 HTML）。

开机自启：把 `OneMarkDown.exe` 的快捷方式放进 `shell:startup`，注意同目录的 DLL 要一起带着，不能单独拷 exe。

### 一个容易忽略的细节

热键是 `Win+Alt+V`，触发瞬间 Win 和 Alt **还处于物理按下状态**，此时合成的按键在系统看来是 `Win+Alt+Ctrl+V`，目标不会当成粘贴。所以发送前会：

1. 轮询 `GetAsyncKeyState` 等 Win/Alt/Ctrl/Shift 全部松开（最多 1200ms）
2. 再留 40ms 让最后一个 keyup 被消息队列消化
3. 然后才发 `Ctrl+V`

超时仍未松开时，会给仍按下的修饰键补发 keyup 强制清干净。

## 编译与打包

编译与打包都在 `tools\build.ps1`。

```powershell
# 编译 + 打包到 dist\
powershell -ExecutionPolicy Bypass -File .\tools\build.ps1

# 只编译不打包
... -NoPack

# 编译 + 跑单元测试 + 打包
... -Test

# 先删 bin/obj
... -Clean
```

产物：

- 编译输出：`src/OneMarkDown.AddIn/bin/Release/`、`src/OneMarkDown.Shell/bin/Release/`
- 分发目录 `dist/`：`AddIn/`（加载项 DLL + 热键工具）、`installer/`，以及一个 `OneMarkDown-v<版本>-<时间戳>.zip`（内容为前两个目录，直接位于压缩包根下）

> `build.ps1` 会先结束正在运行的热键工具（它锁着 bin 目录里的 DLL）。

图标由 `tools/make-icon.ps1` 生成（圆角蓝底 + 白色 Markdown 标记，16~256 共 7 个尺寸各自矢量重绘），预览见 `docs/icon-preview.png`：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\make-icon.ps1
```

## 验收

OneNote 的 HTML 导入子集是黑盒，只能实测。用仓库里的 `samples/fidelity.md`（不随分发包发布）整篇渲染进 OneNote，逐项对照 20 条编号记录「通过 / 降级 / 不支持」。

需要重点确认的几项：代码块单格表格的底色与边框、表头 `bgcolor`、引用块左侧竖线、分隔线（用表格模拟）、大图（>720px）缩放。

## 排查

| 现象 | 先查 |
| --- | --- |
| 功能区没有「Markdown」选项卡 | 文件 → 选项 → 加载项，看 `OneMarkDown.Connect` 是否列出并启用；再看日志 |
| 日志 | `%LOCALAPPDATA%\OneMarkDown\logs\addin.log`，第一行 `GetCustomUI("<ribbonId>")` 能确认宿主用什么 id 调用 |
| 注册项 | `HKLM\SOFTWARE\Microsoft\Office\OneNote\Addins\OneMarkDown.Connect` 的 `LoadBehavior` 应为 `3` |
| 被宿主禁用 | `HKCU\Software\Microsoft\Office\16.0\OneNote\Resiliency\DisabledItems`，重新运行 install.ps1 会清理 |
| 粘贴出来是纯文本 | OneNote 的粘贴选项被设成了「仅保留文本」：文件 → 选项 → 高级 → 粘贴 |
| 自动粘贴没生效 | 焦点没回到编辑区。内容已在剪贴板，手动 `Ctrl+V` 即可 |

`installer\diagnose.ps1` 是只读脚本，一次性输出环境、注册、进程模块与日志，加 `-SkipComTest` 可跳过 COM 激活测试。

加载项点「诊断信息」会把版本、位数、注册状态和日志尾部复制到剪贴板。

## 结构与关键实现

```
src/OneMarkDown.AddIn/           # 共用内核 + 加载项宿主
  Connect.cs                     IDTExtensibility2 + IRibbonExtensibility 入口
  Ribbon/                        Ribbon.xml 与回调分发
  Interop/                       手写 ComImport 声明（不依赖 Office PIA）
  Markdown/                      Markdig 渲染 + DOM 变换（OneNote HTML 子集适配）
  Clipboard/                     CF_HTML 构造与 Win32 剪贴板读写
  Input/                         前台校验 + keybd_event 注入
  Commands/                      命令编排
  Diagnostics/                   滚动日志与诊断报告
src/OneMarkDown.Shell/           # 托盘 + 全局热键外壳
  Program.cs                     单实例、STA、无主窗口
  HotkeyWindow.cs                消息窗口与 RegisterHotKey
  ShellApplicationContext.cs     命令编排、热键回退、托盘菜单动作
  PasteFlow.cs                   渲染 → 剪贴板 → Ctrl+V 的共用流程
  TrayIcon.cs                    托盘图标与右键菜单
  ClipboardPaster.cs             跨窗口 Ctrl+V（keybd_event）
  Hud.cs                         不抢焦点的提示条
  app.ico                        多尺寸图标（tools/make-icon.ps1 生成）
tests/OneMarkDown.Tests/         xUnit：CF_HTML 字节偏移、渲染快照、图片解析、剪贴板往返
samples/fidelity.md              真机保真度验收清单
installer/install.ps1            安装（只安装，不编译）
installer/uninstall.ps1          卸载（先结束代理进程再删目录）
installer/diagnose.ps1           只读诊断
tools/build.ps1                  编译 + 单元测试 + 打包到 dist/
tools/make-icon.ps1              生成 app.ico
docs/icon-preview.png            图标预览
```

### 为什么用剪贴板 + 按键注入，而不是直接写页面 XML

OneNote 的 COM API 没有可靠的「光标/选区」读写，只能整页或按 `one:OE` 节点替换，做不到「插入到光标处」，而且整页重写会破坏撤销栈、引发同步冲突。剪贴板方案完全不碰页面 XML，**加载项没有损坏笔记的能力**。

### 为什么 CF_HTML 要手写

`HTML Format` 剪贴板的 `StartHTML / EndHTML / StartFragment / EndFragment` 全是**按 UTF-8 编码后的字节位置**。含中文或 emoji 时用字符数计算会整体错位，粘出来就是乱码或空白。`CfHtmlWriterTests` 锁住了这一点，`ClipboardRoundTripTests` 再把数据真正写进剪贴板读回原始字节校验。

同理，写剪贴板不能走 `System.Windows.Forms.DataObject`：传 `byte[]` 会被 `ToString()` 成字面量 `System.Byte[]`，传 `string` 会按系统 ANSI 编码写入而与 UTF-8 偏移量错位。`ClipboardWriter` 直接用 Win32 `GlobalAlloc` + `SetClipboardData` 写 UTF-8 字节。

### 加载项为什么跑在 dllhost 里

当前 OneNote 版本不能在自身进程内宿主托管（.NET）COM 加载项：`ONENOTE.EXE` 里从不出现 `mscoree.dll` / `clr.dll`，Office 会把加载项降级成 `LoadBehavior=2`。

解决办法是在 CLSID 上注册 `AppID` + `AppID\{...}\DllSurrogate=""`，让 CLR 跑在 `dllhost.exe` 代理进程里，OneNote 通过跨进程 COM 访问（同机 OneMore 也是这个机制）。安装脚本已包含这些注册项，`InprocServer32` 默认值写裸 `mscoree.dll`。

由此带来四个必须注意的点，改动时别踩回去：

1. **卸载/升级前先结束代理进程**。退出 OneNote 不会结束 `dllhost.exe`，它一直锁着 `OneMarkDown.AddIn.dll`。两个脚本都会先按 CLSID 找到并结束它；卸载仍失败时把目录改名搬到 `%TEMP%`。
2. **对话框必须显式传 owner 并前置**。代理进程没有主窗口，`Process.GetCurrentProcess().MainWindowHandle` 是 0，用它拿 owner 会变成「无主对话框」——弹到 OneNote 后面看不见，而宿主正同步等这个 COM 回调返回，表现为 OneNote 卡死。`Dialogs.FindHostWindow()` 先取前台窗口（属于 ONENOTE 就用它），再退回按进程名查找，弹窗前 `SetForegroundWindow`。
3. **按键注入用 `keybd_event`，不要用 `SendKeys`**。`SendKeys.SendWait` 依赖调用线程的消息循环，而我们在宿主的同步 COM 回调栈上，容易挂住或打空。
4. **读剪贴板用 Win32，不要用 `System.Windows.Forms.Clipboard`**。它走 OLE 数据对象，在上述上下文里会拿不到内容（表现为「剪贴板里没有内容」）。`ClipboardWriter.TryGetText` 用 `GetClipboardData(CF_UNICODETEXT)`，退回 `CF_TEXT`。

确认是否真的跑在代理里：

```powershell
Get-CimInstance Win32_Process -Filter "Name='dllhost.exe'" |
  Where-Object { $_.CommandLine -match '7C4E1B2A' } |
  Select-Object ProcessId, CommandLine
```

> 判断加载项有没有加载，**不要看进程里有没有 `OneMarkDown.AddIn.dll`**：托管程序集通常不出现在 `Process.Modules` / `tasklist /m` 里，这样查必然误判。可靠依据是 dllhost 的 `/Processid:{CLSID}` 命令行、进程里有没有 `mscoree.dll` / `clr.dll`。

## 许可

仅个人使用。
