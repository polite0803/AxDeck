# 服务与工具

「服务/工具」页把本机上的两件事放在一起：个人的 Cloudflare 隧道环境，和本地的 BGE-M3 向量服务。页面上的每个动作都在本机完成，AxDeck 不上传任何内容，也不需要管理员权限。

这个页面取代了两个原先各自独立的 Python 小工具（Cloudflare Tunnel Manager 与 BGE-M3 控制面板）：两者的逻辑都已经用原生 C# 重新实现，AxDeck 直接调用它们本来调用的系统程序（`cloudflared.exe`、`ssh.exe`、`tasklist`、`llama-server.exe`），不经过 Python，也不再启动那两个打包好的 `.exe`。

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

列出 9 个 bgnone.cn 入口，按类型给出对应的操作：

| 类型 | 打开方式 | 页面上的操作 |
| --- | --- | --- |
| HTTP | 浏览器直接打开 | 「打开」 |
| SSH | 走 SSH 配置里的 ProxyCommand | 「测试」「复制」 |
| TCP / 远程桌面 | 需要 `cloudflared access tcp` 本地转发 | 「启动」「停止」「复制」 |

「探测站点」会逐个访问入口并显示结果，「复制诊断」把上面这些环境读数复制到剪贴板，「编辑配置」用记事本打开 SSH 配置。

## BGE-M3 向量服务

这一块管理本机的 llama.cpp 向量服务：用 `llama-server` 以嵌入模式加载本地模型 `E:\llama-models\bge-m3.Q5_K_M.gguf`，在 `http://localhost:8091` 上提供 `/v1/embeddings` 接口（1024 维）。

### 依赖

- **llama.cpp**：需要安装 `llama-server.exe`。AxDeck 会依次在 WinGet 的 `ggml.llamacpp_*` 包目录、WinGet 的 Links 目录以及 `Program Files\llama.cpp` 下寻找。
- **模型文件**：默认路径为 `E:\llama-models\bge-m3.Q5_K_M.gguf`。

两者都缺失时，页面会直接显示「未找到 llama-server.exe」或「模型缺失」，不会尝试安装或下载。

### 启动与停止

- **启动**：以 `llama-server -m <模型> --host 0.0.0.0 --port 8091 --embeddings` 启动，等待服务真正就绪后才报成功；模型较大时这一步需要几秒。
- **停止**：结束 `llama-server` 进程。如果服务不是由 AxDeck 启动的（例如在别处手动启动），AxDeck 会在确认 8091 端口上确实是这个服务后再结束它。
- AxDeck 退出时不会停掉这个服务：它是一个独立的服务，关闭面板不应该中断别人的向量请求。

### 状态解读

| 项目 | 含义 |
| --- | --- |
| 服务状态 | 运行中 / 加载模型中… / 已停止 / 未找到 llama-server.exe / 模型缺失 |
| 处理状态 | 空闲（没有槽位在处理）/ 处理中（有槽位在处理） |
| 槽位 | 空闲槽位数 / 总槽位数 |
| 累计请求 | 服务启动后处理过的请求数 |
| 累计 Token | 已处理的 prompt token 数，超过一千按 `1.2k` 显示 |
| 端口 | 8091、模型 Q5_K_M、维度 1024 |
| API | `http://localhost:8091/v1/embeddings` |

页面打开时每两秒读一次状态，切到别的页面就停止读取。

### 日志

`llama-server` 的输出写入 `%LOCALAPPDATA%\AxDeck\logs\bge-m3.log`，「打开日志」用记事本打开它。启动失败时这个文件里会留下原因。

### 关于本机的 HTTP 代理

本机如果配置了出网代理（`http_proxy` 等环境变量），AxDeck 访问本机服务时会**绕过**它直接连接。代理会把 `http://localhost:8091/health` 这类绝对地址原样转发，llama.cpp 收到后路径对不上，会返回 404；原来的 Python 工具不读这些环境变量，所以没有这个问题。

## 隐私

- AxDeck 只读取本机的状态并在你点击按钮时执行对应的操作，不会把任何内容发到外部。
- 隧道部分只会连接你自己的 `bgnone.cn` 域名和你自己的 NAS，不涉及第三方服务。
- BGE-M3 部分完全在本机运行，模型不会离开这台电脑。
- 不保存任何凭据或令牌。
