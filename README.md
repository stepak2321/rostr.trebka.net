# Rostr

**See the server before you join it.**

Rostr is a Windows launcher for Roblox. It shows every server's players, ping, FPS and region before you click, lets you jump into a random one, and keeps an eye on your game with a small in-game overlay.

> Rostr is an independent project. It is not made by, affiliated with or endorsed by Roblox Corporation. Roblox is a trademark of Roblox Corporation.

## Download

Get `Rostr.exe` from the [latest release](https://github.com/stepak2321/rostr.trebka.net/releases/latest) and double-click it. There is nothing to install, and you do not need to install .NET.

**Requirements:** Windows 10 or 11 (64-bit) and a Roblox account.

## Getting started

1. Run `Rostr.exe`.
2. Add your account with the login button in the sidebar. A small browser window opens for you to log in to Roblox.
3. Search for a game by name, place ID or roblox.com link, then press **Open** to choose a server or **Random** to join one straight away.

You can also press Play on roblox.com. The game opens in Rostr first, so you can pick a server before joining.

## Features

- **Server browser.** Every server lists its players, ping, FPS and region. Regions are detected automatically while the list loads. Sort by most or fewest players, newest or best ping, and filter by region, minimum players or maximum ping. Colors show quality at a glance: green is smooth, amber is fine, red is trouble.
- **Random server and Rejoin.** Join a random server in one click, or go back to your last one with Rejoin.
- **FPS cap and FastFlags.** Set an FPS cap with the slider (30 to 360) or type a higher number. One-click presets for FPS unlock, low graphics, max performance, high quality and less telemetry. Raw FastFlags can be edited too.
- **Multiple accounts.** Add as many accounts as you like and switch from the sidebar. Saved logins are encrypted with your Windows login. Multi Roblox lets you run more than one client at once.
- **Library and Friends.** Recent games and favorites with your playtime in each, private server links, and a view of who is online.
- **In-game overlay.** A small click-through box in the corner of the Roblox window, shown only while Roblox is in front. Choose which lines it shows: ping, CPU, RAM, session time, region and account name. The default hide and show hotkey is `Ctrl` + `Shift` + `O`, and you can change it.
- **Discord status.** Shows the Roblox game you are in on your Discord profile, with the game name, its creator, a timer and two buttons: Get Rostr and See game page. You can turn it off, or hide the game name, in Settings. It does nothing if Discord is not running.
- **Automatic updates.** Rostr checks the GitHub releases when it starts and installs new versions by itself. Every download is verified against its SHA-256 checksum before it replaces the old exe. You can turn this off in Settings, then use Check for updates when you want one.
- **Client management.** Rostr installs the Roblox client for you and removes old versions so they do not pile up on your disk.

## Good to know

- A ping marked with a tilde (`~`) is an estimate. Roblox servers do not answer ping, so Rostr measures the last router on the way instead.
- Roblox does not publish when a server was created. The **Age** column shows how long Rostr has known a server, not its true age.
- Rostr keeps its data in `%LocalAppData%\Rostr`: your saved accounts (encrypted), settings, FastFlag overrides and log files.
- To catch Play on roblox.com, Rostr registers itself for `roblox-player:` links for your Windows user only. It saves the original handler the first time so it can be restored.

## Uninstall

Close Rostr and delete `Rostr.exe`. To remove your saved data too, delete the `%LocalAppData%\Rostr` folder.

## Issues and feedback

Found a bug or have an idea? Open an [issue](https://github.com/stepak2321/rostr.trebka.net/issues) on this repository.
