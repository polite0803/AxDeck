# Software updates

The Software updates page gathers the available updates of every app installed on the PC, so you can update them one by one instead of opening each app to look for its own "check for updates".

The page does two things: it reads what is installed on this PC and asks whether anything newer exists, and it updates an item when you select it. It installs nothing by itself and accepts no licence agreement for you.

## What the page shows

- A status line at the top: how many updates are available and when the last check ran, or "Checking… (the first check takes 1–2 minutes)". The "Check now" button at its right runs a check whenever you want. After you press Update, the right side of the line shows how that went until the next check finishes.
- One row per app: its name, `current version → available version`, where the update came from (`WinGet` or `Vendor`), and the action at the right.
- More rows than fit can be reached with the mouse wheel.

## Where the list comes from

Three sources, each independent of the others:

| Source | What it is | What it provides |
| --- | --- | --- |
| WinGet | The output of `winget upgrade` | Packages that are installed and that WinGet knows have a newer version: the id, the current version and the available one. This is the main source and covers most development tools |
| Built-in table | `apps.json`, shipped with AxDeck | The latest version of common apps WinGet does not recognise but that publish a version endpoint (VS Code, Firefox, GitHub CLI, DBeaver, Obsidian), asked from GitHub Releases or the vendor's interface |
| Installed list | Windows' uninstall information (the registry) | Only what is installed on this PC: name, version and executable. It never says whether a newer version exists |

The table is meant to grow: when an app publishes a version endpoint, one entry adds it, without depending on a whole third-party app store.

## How often it checks

- With "Check for updates automatically" on, a check runs about 90 seconds after AxDeck starts, then every 6 hours.
- A failed check is retried after 15 minutes, an hour, then four hours; after a restart that follows a failure, AxDeck again waits 15 minutes instead of running the check at once.
- With "Check for updates automatically" on, opening the page runs a check in the background if the last result is older than 12 hours or there is none; a recent result is shown as it is.
- Every check calls `winget` once, about 1–2 minutes the first time, entirely in the background.
- The result is kept in `%LOCALAPPDATA%\AxDeck\software-updates.json`, so the last list and its time still show right after a restart.

## Updating an item

- A `WinGet` item has an "Update" button: the one package is upgraded silently in the background, the button reads "Updating…", then "Updated", and a new check runs by itself.
- An item with only a download page has a "Download" button: the app's download page opens in your browser, and the rest is yours to do.
- A failed upgrade turns the button into "Retry" and puts winget's own words in the status line (no matching package, administrator rights needed, and so on); selecting it tries again.
- An item that is no longer in the list, or a download page that will not open, also says so in the status line, so a click never looks ignored.
- Upgrades run one at a time: WinGet holds a lock, and concurrent runs fail each other.
- Nothing is updated on its own; every item waits for your click.

## Empty states

Three cases are shown apart, never one in place of another:

- `WinGet not found`: App Installer is not installed on this PC; it is needed before anything can be checked.
- `No updates found`: the check succeeded and nothing is waiting to update.
- `Checking…`: there is no result yet; the first check takes 1–2 minutes.

A failed check shows the reason as it came and keeps the previous list: "could not check" is never shown as "nothing to update".

## How versions are compared

- A version is compared by the numbers in it: `b10107` against `b12602` is ordered correctly, as are `18.4-2` against `18.7-2` and `2.1.25156` against `2.2.26323`.
- A version the table reports must really be newer than the installed one to be listed; a missing answer, or a response without the version field, counts as "not asked" for that round. No version is guessed, and "up to date" is not claimed on the strength of it.

## Settings

Settings › Software updates:

- **Check for updates automatically** (on by default): when off, no timed check runs, and "Check now" still works on the page.
- **Check now**: the same entry as the button on the page, with the same result.
- **Size**: the page's width and height are set in Settings › Expanded view.

## Limits

- WinGet only knows the packages it lists; an app it does not list never appears from that source. The built-in table holds a handful of common apps, so the others are shown neither as having an update nor as being up to date.
- `winget upgrade` takes no source to search, and one call cannot skip an unreachable source such as msstore, so a check takes 1–2 minutes and cannot be shortened to seconds.
- AxDeck accepts no source agreement for you; only when WinGet explicitly asks for package agreements is `--accept-package-agreements` added for one retry.
- For what this page reads and which addresses it contacts, see [Data and privacy](privacy.md).
