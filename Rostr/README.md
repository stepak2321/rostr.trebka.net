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

- **Server browser.** Every server lists its players, ping, FPS and region. Regions are detected automatically while the list loads. Sort by most or fewest players or best ping, and filter by region, minimum players or maximum ping. Colors show quality at a glance: green is smooth, amber is fine, red is trouble.
- **Random server and Rejoin.** Join a random server in one click, or go back to your last one with Rejoin. If Roblox is still running (even only in the tray), Rostr closes it and starts a fresh session. Turn on Multi Roblox if you want to keep it open and run several.
- **FPS cap and FastFlags.** Found in Settings > Performance. Set an FPS cap with the slider (30 to 360) or type a higher number. One-click FastFlag presets for low graphics, high quality and Direct3D 11. Raw FastFlags can be edited too, and Rostr tells you when Roblox will ignore a flag you saved. Two buttons next to the presets open the list of flags Roblox still allows (the official announcement and a community copy) in Chrome, or in your default browser if Chrome is not installed.
- **Multiple accounts.** Add as many accounts as you like and switch from the sidebar. Saved logins are encrypted with your Windows login. Multi Roblox lets you run more than one client at once.
- **Library and Friends.** Recent games and favorites with your playtime in each, private server links, and a view of who is online.
- **In-game overlay.** A small click-through box in the corner of the Roblox window, shown only while Roblox is in front. Choose which lines it shows in Settings > Overlay: ping, CPU, RAM, session time, region and account name. You can also pick the corner it sits in and preview it. The default hide and show hotkey is `Ctrl` + `Shift` + `O`, and you can change it.
- **Discord status.** Shows the Roblox game you are in on your Discord profile, with the game name, its creator, a timer and two buttons: Get Rostr and See game page. You can turn it off, or hide the game name, in Settings > Discord. It does nothing if Discord is not running.
- **Languages.** English, Čeština, Deutsch, Español, Français, Polski, Português, Русский and Türkçe. Rostr follows your Windows language on first start, and you can change it any time in Settings > Appearance. Texts that are not translated yet stay in English.
- **Themes.** Dark, Midnight, AMOLED, Ocean, Forest, Sunset, Rose and Light. Pick one in Settings > Appearance. They repaint the app instantly, no restart.
- **Client management.** Rostr installs the Roblox client for you and removes old versions so they do not pile up on your disk. Settings > Storage shows how much space Rostr uses and has a Clean now button.
- **Organized settings.** Settings has a tab list on the left: About, Performance, Overlay, General, Appearance, Discord and Storage, so every option is easy to find.
- **README, website and change log.** The **Open README** button in the sidebar shows this page without opening a browser, and the **Open web** button below it opens the Rostr website. When an update is available, **See change log** shows what is new in that version before you install it.
- **Self-update.** Rostr checks GitHub for a new release, shows what changed, and installs it for you after you confirm. The download is checked against its SHA-256 first.

## Updates

Rostr checks the [releases](https://github.com/stepak2321/rostr.trebka.net/releases) page shortly after it starts and then every few hours. When a newer version exists you can choose **Install now**, **See change log** or **Not now**. You can also check by hand in Settings > About with **Check for updates**, and open the change log of the installed version with **See change log**.

## Good to know

- Since 29 September 2025 Roblox only reads a short allowlist of FastFlags from `ClientAppSettings.json` and silently ignores all others. Rostr's presets only use flags from that list, but Roblox can change the list at any time, so a preset can stop working after a Roblox update. The FPS limit is not a FastFlag: use the FPS cap in Settings > Performance.
- A ping marked with a tilde (`~`) is an estimate. Roblox servers do not answer ping, so Rostr measures the last router on the way instead.
- Rostr keeps its data in `%LocalAppData%\Rostr`: your saved accounts (encrypted), settings, FastFlag overrides and log files.
- To catch Play on roblox.com, Rostr registers itself for `roblox-player:` links for your Windows user only. It saves the original handler the first time so it can be restored.

## Privacy and security

- A Roblox cookie gives full access to an account. Rostr stores saved logins **encrypted with Windows DPAPI**, so only your Windows user can read them, and sends them **only to roblox.com**. Never share your `accounts.json` or paste a cookie someone else gave you.
- Rostr does not intentionally write cookies or login tickets to its log file.
- Besides Roblox, Rostr contacts:
  - `api.github.com` and `raw.githubusercontent.com` for updates, this README and the change log
  - `setup.rbxcdn.com` to download the Roblox client
  - `ipinfo.io` and `ipwho.is` to look up the region of a game server from its IP address. These services see your IP address like any website you visit.
  - Discord's local pipe for the Discord status. Nothing is sent to Discord over the internet by Rostr itself for this.
- There is no telemetry and no account on any Rostr server.

To report a security problem, see [SECURITY.md](SECURITY.md).

## Disclaimer

Rostr is provided as is, without warranty. Features such as running several Roblox clients (Multi Roblox) and editing FastFlags change how the official client runs. They may go against Roblox's Terms of Use or stop working after a Roblox update. Read Roblox's rules and use Rostr at your own risk.

## Uninstall

Close Rostr and delete `Rostr.exe`. To remove your saved data too, delete the `%LocalAppData%\Rostr` folder.

## Issues and feedback

Found a bug or have an idea? Open an [issue](https://github.com/stepak2321/rostr.trebka.net/issues) on this repository. Want to help with the code? See [CONTRIBUTING.md](CONTRIBUTING.md).

Would you like Rostr in another language, or with a theme you made up? Open an issue and tell us which one. Texts that are not translated yet stay in English until they are added.

## License

Rostr is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
