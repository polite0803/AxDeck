# AGENT.md

给在本仓库工作的 AI 代理（以及人类贡献者）的项目约定。README 讲产品，这里讲怎么改代码。

## 项目是什么

AxDeck 是 Windows 11 的「灵动岛」：常驻屏幕顶部的面板，显示 AI 编程工具的额度与用量、音乐播放、系统读数、无线设备电量，并提供剪贴板与文件中转、把桌面搬进面板的「桌面」页。

- 技术栈：.NET 10 / C# / WPF，只支持 Windows 11 x64。
- 许可证：Apache 2.0。Atoll 等项目（GPL v3）只可作为交互参考，**不得复制、翻译或移植其源码**。
- 性能是一等需求：面板大部分时间处于收起或空闲状态，空闲时的 CPU、内存与动画开销都算缺陷。

## 解决方案结构

| 项目 | 目标框架 | 职责 |
| --- | --- | --- |
| `src/AxDeck.Core` | `net10.0` | 纯逻辑：设置、解析、布局计算、颜色、额度读取、更新、主题。**不含 WPF**，可被测试直接引用 |
| `src/AxDeck` | `net10.0-windows10.0.19041.0` | WPF 应用：`MainWindow`、各页面、设置窗口、`Native/` 下的 Win32 包装 |
| `src/AxDeck.Sensors` | `net10.0-windows` | 独立发布的传感器服务（PawnIO 驱动，命名管道通信）。不在安装包里，用户开启增强传感器时由 `Native.SensorSetup` 下载，并与构建期写入的 SHA-256 校验 |
| `tests/AxDeck.Tests` | `net10.0` | Core 的主测试套件（数量最多） |
| `tests/AxDeck.MediaTests` | `net10.0-windows10.0.19041.0` | 播放器桥接与连接的测试，链接 `Native/` 里的播放器适配代码 |
| `tests/AxDeck.BoxVerify` | `net10.0-windows10.0.19041.0` | 桌面页的端到端验证：真注册表、真文件系统，跑完还原注册表 |
| `tests/AxDeck.WindowsTests` | `net10.0-windows10.0.19041.0` | 真实 WPF 布局与绘制的测试，通过 `<Compile Include>` 直接链接 `src/AxDeck` 的源码 |

`tests/AxDeck.UpdateHarness` 与 `installer/` 不在 `AxDeck.sln` 里，不参与常规构建。

## 构建与测试

一条命令跑完整个门禁（构建 + 全部测试，逐步计时，任一步失败即非 0 退出）：

```bash
pwsh -File scripts/Gate.ps1
```

在真实开发机上跑全部 Windows 检查；`-Hosted` 只跑 CI 虚拟机上也成立的子集（读本机硬件的那几项会跳过）。脚本的参数、日志目录与退出码见文件头注释。提交前至少完整跑一次。

也可以分步手工跑：

```bash
dotnet build AxDeck.sln -c Release
```

测试是自写的 console runner，**没有 xUnit/NUnit/MSTest，`dotnet test` 找不到任何测试**。正确用法：

```bash
dotnet run --project tests/AxDeck.Tests -c Release --no-build
```

```bash
dotnet run --project tests/AxDeck.MediaTests -c Release --no-build
```

```bash
dotnet run --project tests/AxDeck.BoxVerify -c Release --no-build
```

```bash
dotnet run --project tests/AxDeck.WindowsTests -c Release --no-build -- --hosted
```

`AxDeck.WindowsTests` 的参数约定：

- `--hosted`：只跑在 CI 虚拟机上成立的检查，排除读本机硬件（显卡、显示器、温度、功耗、电池、外壳身份）的项。CI 用它。
- 不带参数：全部检查，包括读本机硬件的项。开发者在真实机器上跑。
- `--only <名字>`：只跑一项，便于快速定位。其中 `desktopinput`、`axtv-live`、`bge-live`、`desktop-toggle` 会移动真实鼠标、启动真实进程或改动真实桌面，**默认不进 CI，按名字显式运行**。

测试约定：

- 每个套件用 `Path.GetTempPath()` 下的独立临时目录，**绝不读写 `%LOCALAPPDATA%\AxDeck` 下的真实设置**。
- 不结束其他会话的进程，不修改用户的真实设置、注册表或桌面状态；`BoxVerify` 必须把动过的注册表值原样放回。
- 新增 Core 测试后，记得在 `tests/AxDeck.Tests/Program.cs` 里注册；Windows 测试同理注册到 `tests/AxDeck.WindowsTests/Program.cs`。
- `AxDeck.WindowsTests`、`AxDeck.MediaTests`、`AxDeck.BoxVerify` 都是通过 `<Compile Include>` 逐个链接 `src/AxDeck` 的文件，而不是引用 `AxDeck` 项目。新增一个被它们用到的源文件时，必须同时加进对应 `.csproj`，否则只在测试项目里「类型不存在」。
- `AxDeck.WindowsTests` 里的 `MainWindow` 是**测试自己声明的 partial stub**（见 `PageCapsuleTests.cs`、`ShadowLifetimeTests.cs`）。给 `MainWindow.xaml` 加了控件字段或方法，要在 stub 里补上同名声明，否则报「找不到 XXX」。

## 代码组织约定

- `MainWindow` 按关注点拆成多个 partial 文件：`MainWindow.Move.cs`、`MainWindow.Music.cs`、`MainWindow.CompactLayout.cs` 等。新功能优先新增 partial 文件，而不是把 `MainWindow.xaml.cs` 撑大。
- `src/AxDeck/Native/` 只放平台互操作（Win32、COM、Shell、WMI、PDH、HID 等），业务判断放 `AxDeck.Core`，这样能被 `AxDeck.Tests` 覆盖。
- 一个页面通常是三者：`XxxView`（纯 UI 构建）、`XxxPanel`（控制器、定时刷新、副作用）、`SettingsXxx`（设置页）。视图只显示，副作用回到控制器。
- 页面的注册是横切改动，改一处必漏：`DeckPage` 枚举、`DeckPages`（顺序/显隐/尺寸的多个 switch）、`MainWindow.PageSwitch.cs`、`MainWindow.xaml`、`SettingsWindow` 及其 partial、`MainWindow.Release.cs`。
- 枚举值以数字存进 `settings.json`，**新成员只能加在末尾**，不得重排或复用旧编号。
- 界面文本写成成对形式：`Loc.T("中文", "English")`。少量中文标签同时是数据标识（额度名、播放器名），它们在数据里保持中文、显示时用 `Loc.Label` 翻译，改这些字符串前先确认没有比较逻辑依赖它。
- 注释语言是混合的：早期文件以英文注释为主，桌面页、服务页等新代码以中文注释为主。跟随所在文件的语言，同一文件内保持一致；注释说明「为什么」而不是复述代码。
- 没有 `.editorconfig`、`global.json` 或「警告视为错误」。提交前请保证 `dotnet build -c Release` 为 0 警告 0 错误。
- 内置主题是 `src/AxDeck.Core/Themes/*.json` 嵌入资源，**文件名即主题 id**；主题之间的改动由 `ThemeCoverageTests` 校验覆盖率。

## 数据与设置

- 运行数据目录：`%LOCALAPPDATA%\AxDeck`（崩溃日志 `crash.log`、`themes/`、接管桌面时落的记号 `desktop-taken`）。正式实例必须按当前用户解析真实路径，**不得硬编码用户名或改用打包应用 LocalCache 下的副本**。
- `settings.json`：版本号记在 `AppVersion`。读取宽容——缺失成员取默认值，未知成员忽略；只有设置被改名、拆分、合并或语义变化时才需要加迁移步骤。
- `SettingsMigrations.Steps` 中每步标注**该分支构建的版本号**（即 `releases/` 下最新的文件名）。`scripts/Test-MigrationVersions.ps1` 会校验这一点，而且 `releases/` 只由维护者新增，PR 里加发布说明会被 CI 拒绝。
- 设置文件支持向上升级与降级共存（`Snapshots`），改动保存逻辑前先读 `SettingsStore`。
- `services.json`：本机专属的隧道主机、端口、模型路径，**不进仓库、不进发布包**，缺失或手写出错时降级而非抛异常。
- 读取本机其它应用的数据时只读，并且如实呈现：「未检测到本地数据」「本地无额度记录」是可接受的状态，**不允许编造或估算数值**。
- 访问本机 HTTP 服务时显式关闭系统代理（`HttpClientHandler { UseProxy = false }`），否则用户环境里的 `http_proxy` 会让请求打到错误地址。

## 版本、分支与发布

- 版本号来自 `releases/<version>.md` 里最新的一个，由 `src/AxDeck/ReleaseVersion.targets` 生成，不需要手改；发布构建用 `-p:Version` 覆盖。
- 分支模型：
  - 新功能与一般修复 → 目标分支 `dev`。
  - 版本进入测试阶段后有 `release/<version>` 分支，只接受问题修复。
  - 正式版从 `main` 上的提交打标签。
- 标签：`v1.2.0`（在 `main`）、`v1.2.0-alpha.1`（在 `dev`）、`v1.2.0-beta.1`（在 `release/1.2.0`）。标签名或所在分支不对，发布流程在审批前就失败。
- CI（`.github/workflows/ci.yml`）对 `dev`、`main`、`release/**` 的 PR 跑：Release 构建 + Core / Media / BoxVerify / WindowsTests(`--hosted`)。
- 发布（`.github/workflows/release.yml`）需要维护者审批，构建安装包、便携包与 `update.json`，并验证安装、应用内更新与卸载。

## 文档

- 用户可见的行为改动要同步更新文档，而且**中英各一份**：`README.md` / `README.zh-CN.md`，以及 `docs/{en-US,zh-CN}/` 下的对应页面（主题、播放器、设备、服务、隐私等）。
- 新功能对应新建一节；改动隐私相关的行为（读取了哪些文件、发出哪些请求）必须同步 `privacy.md`。
- `releases/` 下的发布说明由维护者写，不要把功能说明写进那里。

## 提交与评审

- 提交信息用英文，采用 `type: 一句话概括` 的形式（`feat:`、`fix:`、`chore:`、`docs:`），正文说明「问题现象 → 根因 → 改法」，结尾带 `Co-Authored-By:` 署名。
- CodeRabbit 以中文评审，关注：功能错误与回归、UI 线程访问、异步取消、事件订阅与资源释放、空闲时的 CPU/内存开销、资源泄漏与安全问题。仅凭个人偏好建议重构的意见可以不理。

## 常见陷阱

- **分层窗口的透明像素不接收点击**：`AllowsTransparency` 的窗口上，alpha=0 的像素会被系统直接穿透给下层窗口，WPF 自己的命中测试看不见这一点。需要接收点击的图层请用 alpha=1 的底色（见 `DesktopIconsView` 与 `MainWindow.xaml` 的 `HoverExtension`）。
- **给用户看或需要准点的定时器用 `PromptTimer`**，`DispatcherTimer` 基于 `WM_TIMER`，光标移动时可能延迟数秒。低频后台工作留在 `DispatcherTimer` 即可。
- **子进程的控制台与句柄**：WinExe 启动控制台程序会让它自开可见窗口；`CreateFileW` 的 `SECURITY_ATTRIBUTES` 不设 `bInheritHandle` 时子进程拿不到句柄。读别人正在写的日志文件要用 `FileShare.ReadWrite`。
- **懒加载页面的元素复用**：反复重建行并把同一个控件塞进新父容器会抛「指定元素已是另一个元素的逻辑子级」。构造时建一次，之后只改文本。
- 改动 `DeckPages`、页面集合或默认页面后，检查 `AxDeck.Tests` 里硬编码页集合的断言（`ShelfTests`、`SystemMonitorTests`、`DevicesPageTests` 等）。
