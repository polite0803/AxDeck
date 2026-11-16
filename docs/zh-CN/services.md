# 服务与工具

「服务/工具」页把本机上的三件事放在一起：个人的 Cloudflare 隧道环境，本地的 BGE-M3 向量服务，以及 Android 模拟器（AxTV）。页面上的每个动作都在本机完成，AxDeck 不上传任何内容，也不需要管理员权限。

这个页面取代了三个原先各自独立的 Python 小工具（Cloudflare Tunnel Manager、BGE-M3 控制面板与 AxTV 模拟器管理器）：三者的逻辑都已经用原生 C# 重新实现，AxDeck 直接调用它们本来调用的系统程序（`cloudflared.exe`、`ssh.exe`、`tasklist`、`llama-server.exe`、`emulator.exe`、`adb.exe`），不经过 Python，也不再启动那些打包好的 `.exe`。

## Cloudflare 隧道

### 环境

| 项目 | 含义 |
| --- | --- |
| 基础环境 | `cloudflared.exe`、SSH 配置与代理脚本是否齐全 |
| cloudflared | 本机 `cloudflared.exe` 的路径与版本 |
| 本地进程数 | 当前在运行的 `cloudflared` 进程数量 |
| 公网 443 | 公网 443 端口是否可达 |
| 边缘状态 | Cloudflare 边缘节点的返回结果，1033 表示连接器离线 |
| SSH 配置 | `~/.ssh/config` 是否存在 |
| 代理脚本 | `~/.ssh/cloudflared-ssh.bat` 是否存在 |
| ProxyCommand | SSH 配置里的 ProxyCommand 能否正常执行 |

### 站点入口

站点表不写在程序里，而是来自本机的 `services.json`（见下面的[配置文件](#配置文件)），列出你在里面填的入口，按类型给出对应的操作：

| 类型 | 打开方式 | 页面上的操作 |
| --- | --- | --- |
| HTTP | 浏览器直接打开 | 「打开」 |
| SSH | 走 SSH 配置里的 ProxyCommand | 「测试」「复制」 |
| TCP / 远程桌面 | 需要 `cloudflared access tcp` 本地转发 | 「启动」「停止」「复制」 |

「探测站点」会逐个访问入口并显示结果，「复制诊断」把上面这些环境读数复制到剪贴板，「编辑 SSH 配置」用记事本打开 `~/.ssh/config`，「打开配置」用记事本打开 `services.json`（文件还不存在时先写一份只含占位符的模板）。

### 配置文件

AxDeck 的发布包和仓库里不含任何主机名、端口或本机路径：这些属于你个人的网络，写在本机的 `services.json` 里。它和 `settings.json` 放在同一个目录（`%LOCALAPPDATA%\AxDeck\services.json`），每次页面刷新都会重新读取，改完点「刷新」即可生效，不用重启。

```json
{
  "tunnel": {
    "hostname": "your-host.example.com",
    "defaultSshHost": "your-host.example.com",
    "sshHosts": ["your-host.example.com", "nas"],
    "ports": [10000],
    "sites": [
      { "host": "your-host.example.com", "kind": "http", "label": "站点", "backend": "container:8080", "path": "/" },
      { "host": "db.your-host.example.com", "kind": "tcp", "label": "数据库", "backend": "postgres:5432", "proto": "tcp", "port": 15432, "client": "127.0.0.1:15432" }
    ]
  },
  "bge": { "modelPath": "C:\\models\\bge-m3.Q5_K_M.gguf" }
}
```

| 字段 | 含义 |
| --- | --- |
| `tunnel.hostname` | 「公网 443」和「边缘状态」探测的主机名 |
| `tunnel.defaultSshHost` | 「SSH 登录测试」与远端容器操作使用的 Host（`~/.ssh/config` 里的名字）；留空时取 `sshHosts` 的第一个 |
| `tunnel.sshHosts` | 页面下拉里可选的 SSH Host |
| `tunnel.ports` | 本地转发器监听的端口，用于显示环境读数 |
| `tunnel.sites[].kind` | `http`、`tcp`、`rdp` 或 `ssh`；`tcp`/`rdp` 需要 `proto` 与 `port`，`client` 说明客户端该连哪里 |
| `bge.modelPath` | `llama-server` 加载的 bge-m3 gguf 文件；留空表示未配置 |

文件不存在、读不出来或某项填错时，页面不会报错退出：缺失的入口会被跳过，页面会提示写一份 `services.json`。

## BGE-M3 向量服务

这一块管理本机的 llama.cpp 向量服务：用 `llama-server` 以嵌入模式加载 `services.json` 里 `bge.modelPath` 指定的模型，在 `http://localhost:8091` 上提供 `/v1/embeddings` 接口（1024 维）。

### 依赖

- **llama.cpp**：需要安装 `llama-server.exe`。AxDeck 会依次在 WinGet 的 `ggml.llamacpp_*` 包目录、WinGet 的 Links 目录以及 `Program Files\llama.cpp` 下寻找。
- **模型文件**：在 `services.json` 的 `bge.modelPath` 里填写，例如 `"C:\\models\\bge-m3.Q5_K_M.gguf"`。AxDeck 不附带模型，也不猜路径。

两者都缺失时，页面会直接显示「未找到 llama-server.exe」或「模型缺失」，不会尝试安装或下载。

### 启动与停止

- **启动**：以 `llama-server -m <模型> --host 0.0.0.0 --port 8091 --embeddings` 启动，等待服务真正就绪后才报成功；模型较大时这一步需要几秒。
- **停止**：结束 `llama-server` 进程。如果服务不是由 AxDeck 启动的（例如在别处手动启动），AxDeck 会在确认 8091 端口上确实是这个服务后再结束它。
- AxDeck 退出时不会停掉这个服务：它是一个独立的服务，关闭面板不应该中断别人的向量请求。

### 状态解读

| 项目 | 含义 |
| --- | --- |
| 服务状态 | 运行中 / 加载模型中… / 已停止 / 未找到 llama-server.exe / 模型未配置 / 模型缺失 |
| 处理状态 | 空闲（没有槽位在处理）/ 处理中（有槽位在处理） |
| 槽位 | 空闲槽位数 / 总槽位数 |
| 累计请求 | 服务启动后处理过的请求数 |
| 累计 Token | 已处理的 prompt token 数，超过一千按 `1.2k` 显示 |
| 端口 | 8091、模型 Q5_K_M、维度 1024 |
| 模型文件 | `services.json` 里配置的路径，以及该文件是否存在 |
| API | `http://localhost:8091/v1/embeddings` |

页面打开时每两秒读一次状态，切到别的页面就停止读取。

### 日志

`llama-server` 的输出写入 `%LOCALAPPDATA%\AxDeck\logs\bge-m3.log`，「打开日志」用记事本打开它。启动失败时这个文件里会留下原因。

### 关于本机的 HTTP 代理

本机如果配置了出网代理（`http_proxy` 等环境变量），AxDeck 访问本机服务时会**绕过**它直接连接。代理会把 `http://localhost:8091/health` 这类绝对地址原样转发，llama.cpp 收到后路径对不上，会返回 404；原来的 Python 工具不读这些环境变量，所以没有这个问题。

## AxTV 模拟器

这一块管理 Android Studio 的 AVD：启动、关闭、摆放窗口、安装与运行 AxTV 应用、截图。原先的 AxTV 模拟器管理器是一个独立的 Tkinter 窗口（桌面上那个 `AxTV 模拟器管理器.vbs` 只是它的无黑窗启动器），现在它的功能在这里。

### 依赖

- **Android SDK**：默认在 `%LOCALAPPDATA%\Android\Sdk`，用到其中的 `emulator\emulator.exe` 与 `platform-tools\adb.exe`。
- **AVD**：AVD 定义在 `~/.android\avd`。AxDeck 读的是本机真实存在的 AVD 列表，不会自己创建，也不会下载系统镜像。

缺少 SDK 或没有可用 AVD 时，页面会直接说明缺什么，不会尝试安装。

### 模拟器的启停

- **启动**：冷启动（不读快照），命令为 `emulator.exe -avd <AVD> -no-snapshot-load -gpu auto -no-boot-anim`。模拟器是控制台程序，它启动的虚拟机必须挂在一个控制台上，所以 AxDeck 给它一个**从创建起就隐藏的独立控制台**：既不会闪出命令行窗口，虚拟机也不会因此另开一个可见的终端（那个终端一关，模拟器就跟着没了）。
- **关闭**：走 `adb emu kill`，让模拟器自己保存状态再退出，最多等 30 秒。
- **强制结束**：直接结束 `emulator.exe` 与 `qemu-system-x86_64.exe`。快速启动的快照可能因此损坏，日常优先用「关闭」。
- **重启**：先关后开。
- **切换 AVD**：两个 AVD 共用 5554 端口和同一套 SDK，不能并行运行，所以「切换 AVD」是关掉当前这个、再冷启动另一个（当前选中的 AVD 记在设置里）。

### 窗口的形状

模拟器是一个独立的 Qt 窗口，AxDeck 通过 Win32 调整它的位置和大小：

| 操作 | 效果 |
| --- | --- |
| 全屏 | 去掉标题栏与边框，铺满目标显示器 |
| 小窗 | 显示器宽度的 70%，按 AVD 物理面板的宽高比反推高度，居中 |
| 窗口归位 | 强制回到居中的小窗 |

窗口的宽高比在模拟器启动时就由 AVD 的**物理面板**（`config.ini` 里的 `hw.lcd.width` / `hw.lcd.height`）定死，运行时改不了，所以横竖屏是两个不同的 AVD：想要真正的竖屏窗口，得冷启动那个竖屏 AVD。页面里的「切换 AVD」就是做这件事的。

多显示器时，摆放位置以模拟器所在的那块屏为准；竖屏 AVD 会主动挑一块 9:16 的竖屏屏来投放。

### 显示方向

「切换方向」只改虚拟机的 guest 分辨率（`wm size 1080x1920` / `wm size reset`），不动 AVD 的配置，也不会去改物理面板。改完会截一帧自检：如果画面是黑的（截图不足 20 KB），会自动回滚到横屏并把窗口归位，不会把模拟器留在黑屏状态。

### 应用与安装

| 操作 | 说明 |
| --- | --- |
| 安装 | 选一个 `.apk` 装进模拟器。签名与已装版本不一致时会提示；勾选「自动卸载旧版重装」（默认开启）后会先卸载再装，**旧版的应用数据会一并清除** |
| 启动应用 / 停止应用 | 启动或强制停止 `com.github.axtv.dv` |
| 卸载 | 从模拟器里移除这个应用 |

如果模拟器没在运行，这些操作会直接说明，不会空等。

### 截图与日志

- 截图保存在 `%USERPROFILE%\Pictures\AxDeck\AxTV`，文件名带时间戳。
- 模拟器的输出写入 `%LOCALAPPDATA%\AxDeck\logs\axtv-emulator.log`，「打开日志」用记事本打开它。

## 隐私

- AxDeck 只读取本机的状态并在你点击按钮时执行对应的操作，不会把任何内容发到外部。
- 隧道部分只会连接你在 `services.json` 里填写的自己的域名和你自己的 NAS，不涉及第三方服务。AxDeck 的发布包不含任何主机名、端口或本机路径。
- BGE-M3 部分完全在本机运行，模型不会离开这台电脑。
- 模拟器部分只与本机的 Android SDK 交互，不联网。
- 不保存任何凭据或令牌。
