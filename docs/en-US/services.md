# Services and tools

The Services & tools page brings three things that live on this PC together: your personal Cloudflare tunnel setup, the local BGE-M3 embedding service, and the Android emulator (AxTV). Every action on the page happens on this machine: AxDeck uploads nothing and needs no administrator rights.

The page replaces three separate Python tools (Cloudflare Tunnel Manager, the BGE-M3 control panel and the AxTV emulator manager): all three are now implemented in native C#. AxDeck calls the same system programs they used (`cloudflared.exe`, `ssh.exe`, `tasklist`, `llama-server.exe`, `emulator.exe`, `adb.exe`) directly — no Python, and the packaged `.exe` files are no longer started.

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

The site table is not built into the program; it comes from `services.json` on this PC (see [The config file](#the-config-file) below), and the entries you put there are listed with the action that fits their kind:

| Kind | How it is reached | Actions on the page |
| --- | --- | --- |
| HTTP | Opened straight in the browser | "Open" |
| SSH | Through the ProxyCommand in the SSH config | "Test", "Copy" |
| TCP / remote desktop | Needs a local `cloudflared access tcp` forwarder | "Start", "Stop", "Copy" |

"Probe sites" visits every entry and shows the result. "Copy diagnostics" puts the environment readings above on the clipboard, "Edit SSH config" opens `~/.ssh/config` in Notepad, and "Open config" opens `services.json` (writing a template of placeholders first when the file does not exist yet).

### The config file

Neither the repository nor the published package carries a host name, a port or a path on anyone's machine: those belong to your own network and live in `services.json` on this PC. It sits next to `settings.json` (`%LOCALAPPDATA%\AxDeck\services.json`), is read again on every refresh, and an edit takes effect when you press "Refresh" — no restart.

```json
{
  "tunnel": {
    "hostname": "your-host.example.com",
    "defaultSshHost": "your-host.example.com",
    "sshHosts": ["your-host.example.com", "nas"],
    "ports": [10000],
    "sites": [
      { "host": "your-host.example.com", "kind": "http", "label": "Site", "backend": "container:8080", "path": "/" },
      { "host": "db.your-host.example.com", "kind": "tcp", "label": "Database", "backend": "postgres:5432", "proto": "tcp", "port": 15432, "client": "127.0.0.1:15432" }
    ]
  },
  "bge": { "modelPath": "C:\\models\\bge-m3.Q5_K_M.gguf" }
}
```

| Field | Meaning |
| --- | --- |
| `tunnel.hostname` | The host "Public 443" and "Edge status" probe |
| `tunnel.defaultSshHost` | The Host (as named in `~/.ssh/config`) used by "SSH login test" and the remote container actions; when empty the first of `sshHosts` is used |
| `tunnel.sshHosts` | The SSH hosts offered on the page |
| `tunnel.ports` | The ports the local forwarders listen on, shown with the environment readings |
| `tunnel.sites[].kind` | `http`, `tcp`, `rdp` or `ssh`; `tcp`/`rdp` need `proto` and `port`, and `client` says where a client should connect |
| `bge.modelPath` | The bge-m3 gguf file `llama-server` loads; empty means none is configured |

A missing, unreadable or partly wrong file never stops the page: entries that cannot be used are skipped, and the page says to write a `services.json`.

These sites can also be linked by hand to projects registered in Development work: choose "Linked site" in the project editor. The Work page only shows the site label and copies a command; it never matches by project name, folder or git remote, starts a command or probes the site.

## BGE-M3 embedding service

This part manages the local llama.cpp embedding service: `llama-server` loads the model named by `bge.modelPath` in `services.json` in embeddings mode and serves `/v1/embeddings` on `http://localhost:8091` (1024 dimensions).

### What it needs

- **llama.cpp**: `llama-server.exe` has to be installed. AxDeck looks for it in the WinGet `ggml.llamacpp_*` package folder, in the WinGet Links folder, and under `Program Files\llama.cpp`, in that order.
- **The model file**: set it in `bge.modelPath` in `services.json`, for example `"C:\\models\\bge-m3.Q5_K_M.gguf"`. AxDeck ships no model and does not guess a path.

When either is missing the page says so ("llama-server.exe not found" or "model missing"); nothing is installed or downloaded for you.

### Starting and stopping

- **Start**: runs `llama-server -m <model> --host 0.0.0.0 --port 8091 --embeddings` and waits until the server really answers before reporting success. The model is large, so this takes a few seconds.
- **Stop**: ends the `llama-server` process. If the service was started somewhere else, AxDeck ends it only after it has confirmed that the server on port 8091 really is this one.
- Leaving AxDeck does not stop the service: it is a service of its own, and closing the panel should not cut off somebody else's embedding requests.

### Reading the status

| Item | What it means |
| --- | --- |
| Service state | Running / loading the model… / stopped / llama-server.exe not found / no model configured / model missing |
| Processing | Idle (no slot is working) / busy (a slot is working) |
| Slots | Idle slots / total slots |
| Requests | How many requests the server has handled |
| Tokens | The prompt tokens processed, shown as `1.2k` above a thousand |
| Port | 8091, model Q5_K_M, 1024 dimensions |
| Model file | The path set in `services.json`, and whether that file exists |
| API | `http://localhost:8091/v1/embeddings` |

The page reads the state every two seconds while it is open, and stops reading as soon as another page takes over.

### Starting with Windows

With "Start with Windows" on, AxDeck writes a startup entry for the current user (`AxDeckBgeM3` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) that starts `llama-server` at sign-in with the same arguments the page's "Start" uses — the model path comes from `services.json` — so AxDeck does not have to be running first. Turning it off deletes the entry and leaves a server that is already running alone. It cannot be turned on without `llama-server.exe` and a model path; the page says which is missing. The entry is separate from AxDeck's own start with Windows, so the two switch independently.

### The log

The output of `llama-server` is written to `%LOCALAPPDATA%\AxDeck\logs\bge-m3.log`: "View log" shows it inside the panel, and "Open log" opens it in Notepad. The server may be writing to the same file, so AxDeck opens it for shared reading and never fails on that account. When a start fails, the reason is left in that file.

### The HTTP proxy on this PC

When this PC has an outbound proxy configured (the `http_proxy` environment variables), AxDeck **bypasses** it for a server on this machine and connects straight to it. A proxy forwards `http://localhost:8091/health` as an absolute address, llama.cpp then sees a path it does not know and answers 404; the old Python tool did not read those environment variables, which is why it never ran into this.

## AxTV emulator

This part manages the Android Studio AVDs: starting and stopping them, placing their windows, installing and running the AxTV app, and taking screenshots. The AxTV emulator manager used to be a separate Tkinter window (the `AxTV 模拟器管理器.vbs` on the desktop was only its windowless launcher); its functions live here now.

### What it needs

- **The Android SDK**: by default at `%LOCALAPPDATA%\Android\Sdk`, using `emulator\emulator.exe` and `platform-tools\adb.exe` from it.
- **AVDs**: the AVD definitions live in `~/.android\avd`. AxDeck reads the AVDs this machine really has. It does not create any and does not download a system image.

When the SDK is missing or there is no AVD to run, the page says what is missing instead of trying to install anything.

### Starting and stopping

- **Start**: a cold start (no snapshot is loaded), with `emulator.exe -avd <AVD> -no-snapshot-load -gpu auto -no-boot-anim`. The emulator is a console program and the virtual machine it starts has to sit on a console, so AxDeck gives it a console of its own that is **hidden from the moment it is created**: no command line window flashes by, and the virtual machine does not open a visible terminal of its own either (closing that terminal would stop the emulator).
- **Stop**: goes through `adb emu kill`, so the emulator saves its state and leaves on its own, waiting up to 30 seconds for it.
- **Force end**: ends `emulator.exe` and `qemu-system-x86_64.exe` outright. The quick-start snapshot may be damaged by this, so prefer "Stop".
- **Restart**: closes the emulator and starts it again.
- **Switch AVD**: the two AVDs share port 5554 and the same SDK and cannot run side by side, so switching closes the running one and cold starts the other. The AVD in use is remembered in the settings.

### The shape of the window

The emulator is a separate Qt window, and AxDeck moves and resizes it through Win32:

| Action | What it does |
| --- | --- |
| Full screen | Drops the title bar and the border and fills the display |
| Small window | 70 percent of the display's width, the height follows the aspect ratio of the AVD's physical panel, centred |
| Reset window | Puts it back into the centred small window |

The window's proportions are fixed when the emulator starts, by the AVD's **physical panel** (`hw.lcd.width` and `hw.lcd.height` in `config.ini`), and cannot be changed while it runs. That is why landscape and portrait are two different AVDs: a real portrait window needs a cold start of the portrait AVD, which is what "Switch AVD" does.

With more than one display, the window is placed against the display the emulator is on; a portrait AVD looks for a tall 9:16 display of its own.

### The screen orientation

"Switch orientation" changes the guest resolution only (`wm size 1080x1920` and `wm size reset`); it never touches the AVD's configuration or the physical panel. A frame is captured afterwards to check the result: if the picture is blank (the capture is under 20 KB), the screen is put back to landscape and the window is restored, so the emulator is never left with a black picture.

### The app and installing it

| Action | What it does |
| --- | --- |
| Install | Installs a chosen `.apk` on the emulator. A package signed differently from the one already installed is reported; with "uninstall the old one and install again" turned on (the default) the old app is removed first, which **clears the app's own data** |
| Launch app / Stop app | Starts or force-stops `com.github.axtv.dv` |
| Uninstall | Removes the app from the emulator |

With the emulator not running, these say so instead of waiting for something that will not come.

### Screenshots and the log

- Screenshots are saved in `%USERPROFILE%\Pictures\AxDeck\AxTV`, with a timestamp in the name.
- The emulator's output is written to `%LOCALAPPDATA%\AxDeck\logs\axtv-emulator.log`, which "Open log" opens in Notepad.

## Privacy

- AxDeck only reads local state and runs the action you click; nothing is sent anywhere.
- The tunnel part talks only to the domains and the NAS you list in `services.json`, and to no third-party service. No published package carries a host name, a port or a path from anyone's machine.
- The BGE-M3 part runs entirely on this PC and the model never leaves it.
- The emulator part only talks to the Android SDK on this PC; it does not go online.
- No credentials or tokens are stored.

## Runtime cost and capabilities

The "Runtime cost and capabilities" section at the bottom of the page reports AxDeck's own cost and the readings this PC can provide. It does not start a second system sampler or sensor poll for diagnostics: it reuses the snapshot the system monitor already has. "Refresh" adds one process sample of about 250 ms.

| State | Meaning |
| --- | --- |
| Available | This snapshot or sample contains a real reading |
| Estimated | The value is derived from several components or converted, and is not a whole-machine measurement |
| Not sampled | The system monitor is not running, the reading's page is not open, or no snapshot has been taken yet. It does not mean the hardware lacks the sensor |
| Unavailable | No usable source or sensor was found, for example when the graphics driver reports no temperature |

CPU, memory, GPU, storage, temperatures, system power, battery and frame rate each show their state beside the reading. Power summed from the CPU, GPU and other components is always marked "estimated"; missing traffic says "not sampled" and is never drawn as zero. A reading that has a useful next step gets a button: CPU temperature and system power open Enhanced sensors, while frame rate, GPU temperature and drive temperature open the system monitor settings. When the frame-rate switch is on but Windows permission is missing, the button first asks for confirmation and then uses the existing grant flow. A confirmed absence of a battery or graphics adapter gets no misleading repair button. Readings that SensorGuard has automatically stopped keep their own explanation and "Turn on again" button, using the same recovery path as the system monitor.

"Copy diagnostics" copies the app's own cost, machine summary, capabilities, stopped readings and tunnel environment status. To make the report useful, it includes the `services.json` path and whether the file exists, but no host name, SSH host, port or model path from that file, and no conversation content, credentials or settings values. The report goes to the clipboard only and is not uploaded.
