# 数据与隐私

AxDeck 仅读取应用已有的登录状态与用量记录，不会修改、续期或注销它们的登录，也不会上传会话内容或收集使用数据。剪贴板记录、文件中转、系统监控和设备电量的数据都只在本机使用。

## 读取的数据

| 工具 | 配额 | Token 用量与费用 |
| --- | --- | --- |
| Claude | 读取 Claude Code 的登录（`%USERPROFILE%\.claude\.credentials.json`），不可用时改用 Claude 桌面版的登录，向 Anthropic 查询账户用量。账户同时属于多个组织（例如个人 Pro 与 Team）时，用桌面版为各组织保存的登录分别查询。每一行可以单独关闭"联网获取额度"：关闭的行不读取登录、不联网，只显示 Claude 桌面版在本机记录的额度（`plan-usage-history.json`） | 读取 Claude Code 的本地会话记录（`projects\**\*.jsonl`），不含 Claude 桌面版的普通聊天 |
| Codex | 读取 `%USERPROFILE%\.codex\auth.json` 中的登录，向 OpenAI 查询账户用量；查询失败时显示会话记录中的配额，并标明可能已过时 | 读取本地会话记录（`sessions` 与 `archived_sessions`） |
| Antigravity | 从正在运行的 Antigravity 桌面版的本机服务（仅限 127.0.0.1）读取 | 读取 `%USERPROFILE%\.gemini` 下桌面版与 CLI 的本地会话数据库 |
| Cursor | 以只读方式从 `%APPDATA%\Cursor\User\globalStorage\state.vscdb` 读取登录，向 Cursor 查询账户用量 | 向 Cursor 查询账户用量明细，包含该账户在其他设备上的用量 |
| Grok Bot | 读取 Grok Bot 桌面版自己的登录（`%APPDATA%\Grok Bot\sand-secrets.json`），向 Cursor 查询该账户的每周额度。每一行可以单独关闭"联网获取额度"：关闭的行不读取登录、不联网，只显示 Grok Bot 桌面版在本机缓存的每周额度（`%APPDATA%\Grok Bot\sand-client-persistence`） | 不提供 |
| ZCode | 读取 ZCode 的登录，查询 Coding Plan、Start Plan 与 MCP 每日额度 | 读取本地请求记录（`%USERPROFILE%\.zcode\cli\db\db.sqlite`） |
| DeepSeek Harness | 读取 DeepSeek Harness 的登录（`%USERPROFILE%\.dsh\.credentials.yaml`），向 DeepSeek 开放平台查询账户余额与累计消费金额；未登录账户时，使用其中保存的 API 密钥查询余额 | 读取本地会话记录（`%USERPROFILE%\.dsh\sessions`） |
| Kimi Code | 读取 Kimi Code 的登录（`%USERPROFILE%\.kimi-code\credentials\` 下的 `kimi-code.json`，海外版为 `kimi-code-env-…json`）、服务地址与站点设置（`config.toml`，以及可选的 `region` 文件），向所选站点（api.kimi.com 或 api.kimi.ai）查询套餐额度与会员等级；不续期登录，不修改 Kimi Code 的任何文件 | 读取本地会话记录（`%USERPROFILE%\.kimi-code\sessions`） |
| OpenCode | 以只读方式读取 OpenCode 数据库（`%USERPROFILE%\.local\share\opencode\opencode.db`）中的 OpenCode Console 登录，没有时读取同一目录下 `auth.json` 中的 API 密钥，向 opencode.ai 查询 OpenCode Go 额度；不续期登录，不修改 OpenCode 的任何文件 | 以只读方式读取同一数据库中的模型请求记录 |
| 智谱 GLM、Z.ai GLM、DeepSeek、Kimi Code API、OpenCode Go、OpenRouter、New API、Sub2API、自定义 | 使用在设置中填写的密钥与站点地址；Kimi Code API 的密钥只发往 api.kimi.com 与 api.kimi.ai，读取的额度不写入 `kimi-quota.json`；OpenCode Go 的密钥只发往 opencode.ai；OpenRouter 的密钥只发往 openrouter.ai。密钥只通过 HTTPS 或发往本机地址；New API、Sub2API、自定义来源打开“允许 HTTP”后，也会通过 HTTP 以明文发送 | 不提供 |

- 设置了 `CLAUDE_CONFIG_DIR`、`CODEX_HOME`、`ZCODE_HOME`、`DSH_HOME` 或 `KIMI_CODE_HOME`（须为绝对路径）时，从该目录读取。OpenCode 按 `XDG_DATA_HOME` 与 `OPENCODE_DB` 确定数据库位置。
- 这些账户接口与本地数据格式并非公开的稳定接口。无法读取时，面板显示实际状态，不会显示虚构的数值。
- 「软件更新」页以只读方式读取 Windows 的卸载信息（`Uninstall` 注册表键，三处：当前用户、64 位与 32 位的本机），取其中的名称、版本、发布者、图标路径与安装目录，用来知道这台机器上装了哪些应用；系统组件、补丁（KB）项与嵌套项跳过。它同时运行本机的 `winget.exe` 读取 `winget upgrade` 的输出（包 ID、名称、当前版本与可用版本），并向[下面](#联网与安全)列出的版本接口询问常见应用的最新版本。不读取任何应用的文件内容。

## 本机保存的文件

所有文件保存在 `%LOCALAPPDATA%\AxDeck`（截图除外，它由你决定放在图片文件夹里）：

| 文件 | 内容 |
| --- | --- |
| `settings.json` | 设置，不含任何登录令牌或 API 密钥 |
| `projects.json` | 你手动登记的项目目录，以及每个项目的显示名称、工作来源、可选的启动程序路径、默认文件组编号和手动选择的 `services.json` 站点主机。AxDeck 不通过扫描磁盘、窗口标题、进程、git remote 或文件夹时间发现项目和站点；只有你触发打开或恢复时，才检查项目路径 |
| `services.json` | 「设置 › 服务与工具」页使用的隧道入口、SSH Host 与 BGE-M3 模型路径，只保存在本机，不出现在任何发布包里；文件由你自己填写，可以随时修改 |
| `secrets.dat` | 手动填写的 API 密钥，使用 Windows 的加密功能按当前用户加密 |
| `model-prices.json`、`manual-model-prices.json` | 同步的模型单价、手动添加的单价 |
| `official-prices.json` | 从 OpenRouter 同步的模型厂商官方渠道单价，只包含记录中用到的模型，以及 Kimi Code 计费所需的 Kimi K3、K3.1 Pro 与 K3.1 Pro Highspeed。没有这个文件时，读取旧版本保存的 `moonshot-prices.json` |
| `claude-quota.json` | 最近一次联网读取的 Claude 额度（各项百分比、重置时间与订阅名称），重启后接口被限流时用来继续显示 Fable 等额度；不含登录令牌；账户与组织仅以其 ID 的单向哈希记录（不保存 ID 本身），每个组织一条，只在同一账户登录时使用；超过 7 天不再使用，登录失效或退出登录时删除 |
| `kimi-quota.json` | 最近一次联网读取的 Kimi Code 额度（各项百分比、重置时间、加油包与会员等级），登录过期或接口暂时不可用时继续显示，重启后同样适用；不含登录令牌与其他账户资料；账户仅以其 ID 连同服务地址的单向哈希记录（不保存 ID 本身），只在同一站点的同一账户登录时使用；超过 7 天不再使用，退出登录或账户未订阅 Kimi Code 套餐时删除 |
| `update-prompt.txt`、`updates\` | 已提示过的版本号、下载的安装包或免安装压缩包（免安装版更新时还有解压出的新版本文件） |
| `software-updates.json` | 上一次软件更新检查的结果：检查时间、已安装应用的数量，以及每个可更新项的名称、当前与可用版本、来源，失败时还有失败原因的原文。不含应用的文件内容、账户或设备信息 |
| `cursor-prices.json` | Cursor 合并套餐每周已含用量的读数（金额与各类 Token 数），用于估算该套餐下的费用 |
| `apple-music-lyrics.json` | Apple Music 中每首歌匹配到的歌词编号，重启后再次播放时直接使用 |
| `usage-history.json` | 各项额度今天的已用百分比与记录时间，用来在配额条后面画出走势；每 10 分钟最多记一条，保留 30 天。不含 Token 数、账户、对话内容或任何文本 |
| `traffic-today.json` | 今日累计的磁盘读取、磁盘写入、网络下载与网络上传的字节数，以及对应的系统计数器读数与日期；跨过零点从零开始 |
| `focus-timer.json` | 专注计时的总时长、剩余秒数与结束时间，用在面板关闭或 AxDeck 重启后接着走；可选的会话名称与已选工作来源；每个进行中的会话最多 120 个仅含元数据的采样；最多 50 个已完成或已放弃会话的历史。采样只含各来源的状态、最近写入时间与记录数，不含对话内容 |
| `command.txt` | 第二个 AxDeck 进程留给正在运行的进程的命令（例如来自 `axdeck://` 地址），读取后立即删除；写入的是命令名与参数，不含其他内容 |
| `shelf.dat`、`shelf\` | 剪贴板记录、文件中转的文件路径和两者的分类，以及复制的图片（每张一个文件），都使用 Windows 的加密功能按当前用户加密。普通的剪贴板记录在退出 AxDeck 时清除（异常退出的，在下次启动时清除）；“常用”和一直保留的分类中的内容保留到手动删除 |
| `themes\` | 自定义的面板主题文件 |
| `sensor-stops.json`、`sensor-pending.json` | 系统监控因电脑卡住而停止读取的项目（硬盘或显卡的型号、主板型号与 BIOS 版本），以及正在读取的项目，用于在下次启动时发现上次卡住的读取；在设置中重新开启后删除对应记录 |
| `crash.log` | 错误记录，超过 1 MB 时清空 |
| `logs\bge-m3.log` | 「设置 › 服务与工具」页启动的本机 `llama-server` 的输出，保留下来以便启动失败时能查到原因 |
| `logs\axtv-emulator.log` | 「设置 › 服务与工具」页启动的本机 `emulator.exe` 的输出，同样是为了启动失败时能查到原因 |

## 联网与安全

- 仅连接以下地址：各工具的官方接口、你在设置中填写的站点、OpenRouter 模型目录及记录中用到的模型的渠道列表（同步模型单价，只发送模型名称）、歌词服务（LRCLIB、网易云音乐、QQ 音乐、酷狗音乐）、播放器提供的封面地址、软件更新页查询版本号用的 GitHub Releases API 与 VS Code、Firefox 的公开版本接口，以及 GitHub Releases（检查 AxDeck 自身的更新；启用增强传感器时下载传感器服务与 PawnIO 官方模块）。
- Claude 桌面版的数据目录包括 `%APPDATA%\Claude`、商店版 `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude` 和 `%LOCALAPPDATA%\Packages\AnthropicPBC.Claude_*\LocalCache\Roaming\Claude`，以及 `%LOCALAPPDATA%\Claude-Data\<安装名>`。
- 读取的登录令牌仅在内存中使用，不写入磁盘；Claude 桌面版与 Grok Bot 桌面版的登录缓存使用当前用户的 Windows 加密功能在内存中解密。
- 自定义脚本在沙盒中运行，无法访问文件、进程或 .NET 对象，限制详见[支持的工具](ai-tools.md#自定义脚本)。
- 开启「显示今日流量」时，AxDeck 在后台读取所有磁盘与网络接口的累计计数器，把当天的增长记进 `traffic-today.json`；只保存四个字节数，不保存地址、主机名或数据包内容，不上传。
- 系统监控的读数（包括温度和硬盘空间）只在本机读取和显示，不保存、不上传。硬盘空间来自各盘符的文件系统，盘符所在的物理硬盘通过 Windows 的卷信息查询。温度默认通过 Windows 自带的接口读取：CPU 用性能计数器中的 ACPI 热区，GPU 用显卡驱动报告的数据（显卡内核接口没有时，改用 NVIDIA、AMD、Intel 驱动自带的接口），硬盘以不可读写的方式打开后查询其温度；不需要管理员权限，不安装驱动。如果正在运行 HWiNFO（已开启共享内存）或 LibreHardwareMonitor（已开启远程网页服务，只连接本机 localhost:8085），也会从中读取 CPU 温度和功耗。启用[增强传感器](enhanced-sensors.md)后，CPU 温度和功耗由 AxDeck 的传感器服务通过 PawnIO 驱动从处理器读取。
- 整机功耗和本机电池同样只在本机读取和显示，不保存、不上传，且只在系统监控页或收起行选了该读数时读取：CPU 功率来自 Windows 能量计（Energy Meter）的性能计数器，NVIDIA 显卡功率来自显卡驱动自带的 nvidia-smi.exe（只在显卡有负载、且页面或收起行要显示功耗时由 AxDeck 启动，随 AxDeck 退出而结束；显卡空闲时不读取，以免唤醒休眠的独立显卡），电池电量、充放电功率和容量来自 Windows 的电池接口，电源模式和节电模式来自 Windows 的电源接口；本次累计能耗只在内存中由这些读数计算，不写入磁盘也不上传。不需要管理员权限，不安装驱动。
- 帧率默认不读取。开启“显示帧率”时，需要把当前用户加入 Windows 的“Performance Log Users”组，Windows 会请求一次管理员确认；之后只读取前台程序绘制画面的事件，用来计算帧率并显示程序名称，不保存、不上传。
- 设备电量只在设备电量页、收起行的“设备电量”或“设置 › 显示与设备”显示时读取，不保存、不上传，也不联网。蓝牙设备的电量来自 Windows 自己读取的数值（与“Windows 设置 › 蓝牙和其他设备”一致），必要时读取设备的蓝牙电池服务；使用 2.4G 接收器或数据线的设备通过其 HID 接口发送只读查询；Xbox 手柄通过 Windows 游戏控制器接口；AirPods 与 Beats 只在已连接时从近距离的蓝牙广播中读取。支持的品牌见[设备电量支持](devices.md)。不会配对、断开或改动任何设备与 Windows 设置，不需要管理员权限，不安装驱动。在设置中调整过顺序、名称、颜色或显示位置的设备记在 `settings.json` 里（设备标识、名称、类型、自定义名称、颜色与显示位置）。
- 配额走势只记录每次读数中各项额度的已用百分比，用来画走势线；读取的额度本身来自上表的来源，不额外读取任何文件。判断某个工具是否在工作时，只读取它会话记录文件的元数据（记录数与修改时间），不打开、不读取记录内容；记录数不等于对话数。
- 剪贴板里的图片只在你在右键菜单中选择「读取图片内容…」时分析：二维码由本机的解码器读出，文字由 Windows 自带的识别组件（`Windows.Media.Ocr`）识别，两者都不联网，图片与识别结果都不离开本机。文字生成二维码同样在本机完成。
- 音乐页的「输出设备」只读取本机播放设备的名称与状态，用来列出可选的设备；切换由 Windows 的音频服务完成，AxDeck 不保存设备列表，也不改动任何音频设置。
- 剪贴板记录只在“记录剪贴板”开启时读取系统剪贴板中的文字和图片，连同复制它的应用的进程名一起保存在本机，不上传。“不记录的应用”中的应用，以及密码管理器等应用声明不希望被剪贴板历史保存的内容，不会记录。文件中转只记录文件的路径，并读取文件的图标、大小和修改时间用于显示，不读取文件内容，也不复制文件。详见[剪贴板与文件中转](clipboard.md)。
- 桌面页只读取桌面文件夹里的内容，且大部分是只读的：你自己的桌面（Desktop）与所有账户共用的公共桌面都按目录逐项读出（名字、类型、修改时间等元数据，不读文件内容），用于画出图标；画图标另读快捷方式与可执行文件自带的图标。页面显示时用文件系统观察器盯着这两个文件夹（只监听变化事件，不读内容），面板收起时观察器放掉。往桌面上放东西（拖入、粘贴、改名）只写到你自己桌面的文件夹；删除走系统 shell 进回收站。拖动排序时，除了上述只读的目录读取，还会向资源管理器的桌面图标列表读取每一项的名字与位置（为此在 explorer.exe 进程内临时分配一块内存，读完立即释放），并在你松手后把新的坐标写回同一个列表（`LVM_SETITEMPOSITION`）；位置的持久化由资源管理器自己完成，AxDeck 不读写它的位置记录文件或注册表。这一读写不涉及文件内容，也不联网。「运行时接管桌面」会读写一个注册表值（`HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` 下的 `HideIcons`），即 Windows「查看 › 显示桌面图标」用的同一个开关。右键菜单与属性对话框调用 Windows 的 Shell 组件（COM），菜单上做什么由你选择，AxDeck 不自行发起任何操作。不联网。详见[桌面页](desktop.md)。
- 律动条所用的声音仅在内存中分析，不会保存。
- 「设置 › 服务与工具」页只涉及本机和你自己的域名：隧道部分读取本机的 cloudflared 与 SSH 配置，只连接你在本机 `services.json` 里填写的站点和你自己的 NAS；BGE-M3 部分读取并启动本机 127.0.0.1:8091 上的 `llama-server`；模拟器部分只与本机的 Android SDK（`emulator.exe`、`adb.exe`）交互，不联网。仓库和发布包都不含任何主机名、端口或本机路径，这些只从 `services.json` 读取，不离开本机。模型留在本机，不保存任何凭据或令牌。打开 BGE-M3 的「开机自启」会在当前用户下写一个 Run 键值（`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\AxDeckBgeM3`），内容是启动 `llama-server` 的完整命令行（含服务与模型的本机路径），关闭开关即删除。页内的运行开销与能力诊断复用现有监控快照，不额外启动采样器；这一页的「复制诊断」只写入剪贴板，包含 `services.json` 的路径和存在状态，但不包含其中的主机名、SSH Host、端口、模型路径、会话内容或凭据，也不上传。详见[服务与工具](services.md)。
- `axdeck://` 地址只在当前用户下注册（`HKCU\Software\Classes\axdeck`），内容是打开 AxDeck 自身，不含其他程序，也不处理别人的内容。地址或命令行启动时如果已有 AxDeck 在运行，新进程把命令写进 `command.txt` 后立即退出，由运行中的进程读走并删除；两者都在本机，不联网。
- 专注计时只保存在本机（`focus-timer.json`），不上传。可选的会话名称和已选 AI 工作来源只用于在本机标识这次会话。计时运行期间最多保存 120 个会话元数据采样，每个采样包含各已选来源是否在工作、最近写入时间、本地记录数与正在写入的记录数。已完成或已放弃的会话会连同名称、来源和采样保留在最多 50 条的历史中。AxDeck 不读取或保存对话内容，计时结束时也不发送任何请求，只发一条 Windows 系统通知。
- 开发工作页只显示本机已有的记录：你登记的项目、专注计时、AI 工作状态条使用的同一份会话记录元数据，以及中转站里最近的文本和文件组。它不读取会话内容，不扫描项目目录，不根据其他应用推测当前项目，也不联网。打开登记的目录或启动程序、粘贴文件组、粘贴文本，都只在你主动选择时发生。
- 今日工作态势只从 `focus-timer.json` 的已完成会话及其元数据采样汇总，不新增采样、不读取会话内容、不上传。一个会话声明过的工具只有在采样中留下活动记录时才算活跃来源。
- 项目关联站点只保存你从 `services.json` 站点表里选中的主机字符串；工作页复制命令时只把文本放进剪贴板，不启动 SSH/cloudflared、不打开浏览器、不探测站点状态，也不因此发出网络请求。
- 「软件更新」页的检查分两部分。本机部分只读卸载注册表并运行 `winget.exe`：AxDeck 用隐藏窗口、不经 Shell 启动它，读取 `winget upgrade` 的标准输出后让它结束，由 WinGet 自己按它的源配置联网取回可更新的包——AxDeck 不代你接受任何源协议，只有 WinGet 明确要求包协议时才追加 `--accept-package-agreements` 重试一次。联网部分只问映射表里那几个应用的最新版本：GitHub Releases API（`api.github.com`，未认证，每小时最多 60 次）、VS Code 的 `update.code.visualstudio.com` 与 Firefox 的 `product-details.mozilla.org`。请求带固定的 `User-Agent: AxDeck`，不含账户、设备或使用数据，也不上传已安装应用的清单。这些出站请求保留系统代理（企业网络里 GitHub 常常只能经代理访问），只有访问本机服务时才关闭代理。点「更新」时同样由 `winget.exe` 下载并安装那一个包。

## 网易云音乐完整控制

- 完整控制需要网易云开启本机调试端口。通过 AxDeck 启用时，使用的端口为 127.0.0.1:21631。
- 端口开启期间，本机其他程序和浏览器网页可能借此操控网易云，读取其页面内容与登录状态。
- AxDeck 连接前会确认端口仅监听本机地址且属于网易云进程，但无法消除端口本身带来的风险。
- 通过这个端口，AxDeck 读取正在播放的歌曲、进度、播放模式和当前歌曲是否已喜欢，只在您点击按钮时让网易云执行对应的操作。点击红心时，由网易云自己把歌曲加入或移出您账户中的"我喜欢的音乐"。为显示红心，AxDeck 只取得网易云是否已登录、当前歌曲是否已喜欢这两项结果，不读取或保存您的账户信息和其他歌曲。
- 临时启用时，端口会一直开到网易云以普通方式重新启动为止：退出 AxDeck、完整控制连接失败（AxDeck 改为只读取播放信息）都不会关闭它。
- 打开"始终启用完整控制"开关后，AxDeck 会修改网易云安装目录中 `libcef.dll` 的 2 处字节，修改后网易云每次启动都会打开这个调试端口。端口会一直存在，直到在"设置 › 播放器 › 网易云音乐"中关闭"始终启用完整控制"后网易云再次启动，或网易云更新换掉这个文件；退出 AxDeck 不会关闭它。
- 请仅在可信的环境中启用。

## 酷狗音乐完整控制

- 完整控制会修改酷狗安装目录中 `libcef.dll` 的 9 处字节，修改后酷狗每次启动都会打开本机调试端口 127.0.0.1:12233。
- 端口开启期间，本机其他程序可能借此操控酷狗，读取其页面内容与登录状态。
- AxDeck 连接前会确认端口仅监听本机地址且属于酷狗进程，但无法消除端口本身带来的风险。
- 通过这个端口，AxDeck 只读取正在播放的歌曲、播放状态、进度、时长、封面地址和歌曲的哈希值，只在您点击播放、暂停、上一首或下一首时让酷狗执行对应的操作，不读取或保存您的账户信息。
- 歌曲的哈希值和时长会发送给酷狗的歌词服务，用于查询这首歌的歌词。
- 端口会一直存在，直到在"设置 › 播放器 › 酷狗音乐"中关闭"始终启用完整控制"后酷狗再次启动，或酷狗更新换掉这个文件；退出 AxDeck 不会关闭它。请仅在可信的环境中启用。
