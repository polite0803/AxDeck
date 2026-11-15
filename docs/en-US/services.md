# Services and tools

The Services & tools page brings two things that live on this PC together: your personal Cloudflare tunnel setup and the local BGE-M3 embedding service. Every action on the page happens on this machine: AxDeck uploads nothing and needs no administrator rights.

The page replaces two separate Python tools (Cloudflare Tunnel Manager and the BGE-M3 control panel): both are now implemented in native C#. AxDeck calls the same system programs they used (`cloudflared.exe`, `ssh.exe`, `tasklist`, `llama-server.exe`) directly — no Python, and the packaged `.exe` files are no longer started.

## Cloudflare tunnel

### Environment

| Item | What it means |
| --- | --- |
| Core infra | Whether `cloudflared.exe`, the SSH config and the proxy script are all there |
| cloudflared | The path and version of the local `cloudflared.exe` |
| Local processes | How many `cloudflared` processes are running |
| Public 443 | Whether public port 443 is reachable |
| Edge status | What the Cloudflare edge answers; 1033 means the connector is offline |
| SSH config | Whether `~/.ssh/config` exists |
| Proxy bat | Whether `~/.ssh/cloudflared-ssh.bat` exists |
| ProxyCommand | Whether the ProxyCommand in the SSH config runs |

### Site entries

The 9 bgnone.cn entries are listed with the action that fits their kind:

| Kind | How it is reached | Actions on the page |
| --- | --- | --- |
| HTTP | Opened straight in the browser | "Open" |
| SSH | Through the ProxyCommand in the SSH config | "Test", "Copy" |
| TCP / remote desktop | Needs a local `cloudflared access tcp` forwarder | "Start", "Stop", "Copy" |

"Probe sites" visits every entry and shows the result. "Copy diagnostics" puts the environment readings above on the clipboard, and "Edit config" opens the SSH config in Notepad.

## BGE-M3 embedding service

This part manages the local llama.cpp embedding service: `llama-server` loads the model `E:\llama-models\bge-m3.Q5_K_M.gguf` in embeddings mode and serves `/v1/embeddings` on `http://localhost:8091` (1024 dimensions).

### What it needs

- **llama.cpp**: `llama-server.exe` has to be installed. AxDeck looks for it in the WinGet `ggml.llamacpp_*` package folder, in the WinGet Links folder, and under `Program Files\llama.cpp`, in that order.
- **The model file**: the default path is `E:\llama-models\bge-m3.Q5_K_M.gguf`.

When either is missing the page says so ("llama-server.exe not found" or "model missing"); nothing is installed or downloaded for you.

### Starting and stopping

- **Start**: runs `llama-server -m <model> --host 0.0.0.0 --port 8091 --embeddings` and waits until the server really answers before reporting success. The model is large, so this takes a few seconds.
- **Stop**: ends the `llama-server` process. If the service was started somewhere else, AxDeck ends it only after it has confirmed that the server on port 8091 really is this one.
- Leaving AxDeck does not stop the service: it is a service of its own, and closing the panel should not cut off somebody else's embedding requests.

### Reading the status

| Item | What it means |
| --- | --- |
| Service state | Running / loading the model… / stopped / llama-server.exe not found / model missing |
| Processing | Idle (no slot is working) / busy (a slot is working) |
| Slots | Idle slots / total slots |
| Requests | How many requests the server has handled |
| Tokens | The prompt tokens processed, shown as `1.2k` above a thousand |
| Port | 8091, model Q5_K_M, 1024 dimensions |
| API | `http://localhost:8091/v1/embeddings` |

The page reads the state every two seconds while it is open, and stops reading as soon as another page takes over.

### The log

The output of `llama-server` is written to `%LOCALAPPDATA%\AxDeck\logs\bge-m3.log`, which "Open log" opens in Notepad. When a start fails, the reason is left in that file.

### The HTTP proxy on this PC

When this PC has an outbound proxy configured (the `http_proxy` environment variables), AxDeck **bypasses** it for a server on this machine and connects straight to it. A proxy forwards `http://localhost:8091/health` as an absolute address, llama.cpp then sees a path it does not know and answers 404; the old Python tool did not read those environment variables, which is why it never ran into this.

## Privacy

- AxDeck only reads local state and runs the action you click; nothing is sent anywhere.
- The tunnel part talks to your own `bgnone.cn` domains and your own NAS, and to no third-party service.
- The BGE-M3 part runs entirely on this PC and the model never leaves it.
- No credentials or tokens are stored.
