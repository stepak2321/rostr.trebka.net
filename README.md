# Rostr

**See the server before you join it.**

Rostr is a Windows launcher for Roblox. It shows every server's players, ping, FPS and region before you click, lets you jump into a random one, and keeps an eye on your game with a small in-game overlay.

> Rostr is an independent project. It is not made by, affiliated with or endorsed by Roblox Corporation. Roblox is a trademark of Roblox Corporation.

## Download

Get `Rostr.exe` from the [latest release](https://github.com/stepak2321/rostr.trebka.net/releases/latest) and double-click it. There is nothing to install, and you do not need to install .NET.

**Requirements:** Windows 10 version 2004 (build 19041) or later, or Windows 11 (64-bit), and a Roblox account.

## Getting started

1. Run `Rostr.exe`.
2. Add your account with the login button in the sidebar. A small browser window opens for you to log in to Roblox.
3. Search for a game by name, place ID or roblox.com link, then press **Open** to choose a server or **Random** to join one straight away.

You can also press Play on roblox.com. The game opens in Rostr first, so you can pick a server before joining. The main page has shortcuts to Library, Friends and the Roblox website.

## Features

- **Server browser.** Every server is a card with a ping-colored edge, its players, ping, FPS and region. Regions are detected automatically while the list loads. A SORT and FILTERS bar lets you sort by most or fewest players or best ping, and filter by region, minimum players or maximum ping. Colors show quality at a glance: green is smooth, amber is fine, red is trouble.
- **Rate-limit handling.** If Roblox answers the server list with HTTP 429 (too many requests), Rostr waits and retries by itself, following the `Retry-After` time Roblox sends, or backing off 1, 2 and 4 seconds when it sends none. Server lists are also cached for a few seconds and identical requests are shared, so pressing Refresh again does not hammer the API. If Roblox keeps refusing, Rostr shows the last list it has, or tells you how long to wait.
- **Shareable server links.** Press **Copy link** on a server to get a `rostr://join?place=…&job=…` link. A friend with Rostr opens it and lands in the same server. Rostr always asks before joining, only accepts a numeric place ID and a valid server ID, and needs an account selected in the sidebar.
- **Random server and Rejoin.** Join a random server in one click, or go back to your last one with Rejoin. If Roblox is still running (even only in the tray), Rostr closes it and starts a fresh session. Turn on Multi Roblox if you want to keep it open and run several.
- **FPS cap and FastFlags.** Found in Settings > Performance. Set an FPS cap with the slider (30 to 360) or type a higher number. One-click FastFlag presets for low graphics, high quality and Direct3D 11. Raw FastFlags can be edited too, and Rostr tells you when Roblox will ignore a flag you saved. Two buttons next to the presets open the list of flags Roblox still allows (the official announcement and a community copy) in Chrome, or in your default browser if Chrome is not installed.
- **Multiple accounts.** Add as many accounts as you like and switch from the sidebar. Saved logins are encrypted with your Windows login. Multi Roblox lets you run more than one client at once.
- **Library and Friends.** The Library shows your favorites and recent games as tiles with a large picture, your playtime on each one, and Play, Servers and Rejoin buttons. It also has stat cards (total playtime, games played, most played) and a box for private server links. The Friends tab has IN GAME, ONLINE and OFFLINE counts, a search box, an offline toggle and a card for each friend with a Join button when you can join them.
- **Gallery.** Screenshots and recordings of your Roblox games, in one page under Friends in the sidebar. Press **Screenshot** or **Record**, or use the hotkeys while you play: `Ctrl` + `Shift` + `F10` takes a screenshot and `Ctrl` + `Shift` + `F11` starts and stops a recording. Click a tile to look at the picture or play the video inside Rostr, **rename** it, mark it as a **favorite** (a star on its tile, with a Favorites filter), copy it to paste into Discord or a chat, show it in its folder or delete it, and filter the page by All, Favorites, Screenshots or Videos. Files are saved in `Pictures\Rostr` by default. In Settings > Gallery you can change the hotkeys and the folder. Recording needs ffmpeg, a free tool: Rostr asks before downloading it (about 80 MB) the first time you press Record or turn on Instant replay.
- **Recording settings.** In Settings > Gallery, choose 30 or 60 FPS, the quality (Low, Medium or High) and whether to **record game sound**. Game audio recording is on by default. Both standard recordings and instant replay use Windows Graphics Capture for the Roblox window and capture audio from the Roblox Player process only; audio from separate apps or services is not included.
- **Instant replay.** Turn it on in Settings > Gallery and choose a buffer length of 15, 30, 60 or 120 seconds (30 seconds by default). Rostr shows whether the buffer is actively recording, waiting for Roblox or paused during a normal recording. Press `Ctrl` + `Shift` + `F9` (changeable) or the **Save replay** button on the Gallery page to save the selected duration as a clip, like on a console. It uses the frame rate, quality and sound you chose, uses some CPU, and pauses while you record a video yourself. The buffer lives in a temporary folder and is deleted when Rostr closes.
- **In-game overlay.** A small click-through box in the corner of the Roblox window, shown only while Roblox is in front. Choose which lines it shows in Settings > Overlay: ping, CPU, RAM, session time, region and account name. You can also pick the corner it sits in and preview it. The default hide and show hotkey is `Ctrl` + `Shift` + `O`, and you can change it.
- **Discord status.** Shows the Roblox game you are in on your Discord profile, with the game name, its creator, a timer and two buttons: Get Rostr and See game page. When no game is running it says you are in the launcher, or "Browsing servers" with the name of the game page you have open. In Settings > Discord you can turn it off, hide the game name, or switch off **Show browsing servers on Discord** so opening a game page only shows that you are in the launcher. It does nothing if Discord is not running.
- **Languages.** English, Čeština, Deutsch, Español, Français, Polski, Português, Русский and Türkçe. Rostr follows your Windows language on first start, and you can change it any time in Settings > Appearance. Texts that are not translated yet stay in English.
- **Themes.** Dark, Midnight, AMOLED, Ocean, Forest, Sunset, Rose and Light. Pick one in Settings > Appearance. They repaint the app instantly, no restart.
- **Client management.** Rostr installs the Roblox client for you and removes old versions so they do not pile up on your disk. Settings > Storage shows how much space Rostr uses and has a Clean now button.
- **Organized settings.** Settings has a tab list on the left: About, Performance, Overlay, Gallery, General, Appearance, Discord and Storage, so every option is easy to find.
- **README, website and change log.** The **Open README** button in the sidebar shows this page without opening a browser, and the **Open web** button below it opens the Rostr website. When an update is available, **See change log** shows what is new in that version before you install it.
- **Self-update.** Rostr checks GitHub for a new release, shows what changed, and installs it for you after you confirm. The download is checked against its SHA-256 first.

## Updates

Rostr checks the [releases](https://github.com/stepak2321/rostr.trebka.net/releases) page shortly after it starts and then every few hours. When a newer version exists you can choose **Install now**, **See change log** or **Not now**. You can also check by hand in Settings > About with **Check for updates**, and open the change log of the installed version with **See change log**.

## Good to know

- Since 29 September 2025 Roblox only reads a short allowlist of FastFlags from `ClientAppSettings.json` and silently ignores all others. Rostr's presets only use flags from that list, but Roblox can change the list at any time, so a preset can stop working after a Roblox update. The FPS limit is not a FastFlag: use the FPS cap in Settings > Performance.
- A ping marked with a tilde (`~`) is an estimate. Roblox servers do not answer ping, so Rostr measures the last router on the way instead.
- Rostr keeps its data in `%LocalAppData%\Rostr`: your saved accounts (encrypted), settings, FastFlag overrides, the log file (emptied every time Rostr starts) ffmpeg if you downloaded it for recording, and a temporary `replay` folder while Instant replay is on. Your screenshots and recordings are saved in `Pictures\Rostr`, or the folder you chose in Settings > Gallery.
- To catch Play on roblox.com, Rostr registers itself for `roblox-player:` links for your Windows user only. It saves the original handler the first time so it can be restored. For shareable server links it also registers its own `rostr://` links, again for your Windows user only. Nothing else uses that scheme, so there is nothing to restore.
- Roblox limits how often the server list can be requested. Rostr retries and caches as described above, but if you refresh over and over you can still be asked to wait a little.

## Privacy and security

- A Roblox cookie gives full access to an account. Rostr stores saved logins **encrypted with Windows DPAPI**, so only your Windows user can read them, and sends them **only to roblox.com**. Never share your `accounts.json` or paste a cookie someone else gave you.
- Rostr does not intentionally write cookies or login tickets to its log file.
- Besides Roblox, Rostr contacts:
  - `api.github.com` and `raw.githubusercontent.com` for updates, this README and the change log
  - `setup.rbxcdn.com` to download the Roblox client
  - `www.gyan.dev` to download ffmpeg, only if you press Record or turn on Instant replay and agree to the download
  - `ipinfo.io` and `ipwho.is` to look up the region of a game server from its IP address. These services see your IP address like any website you visit.
  - Discord's local pipe for the Discord status. Nothing is sent to Discord over the internet by Rostr itself for this.
- There is no telemetry and no account on any Rostr server.

To report a security problem, see [SECURITY.md](SECURITY.md).

## Disclaimer

Rostr is provided as is, without warranty. Features such as running several Roblox clients (Multi Roblox) and editing FastFlags change how the official client runs. They may go against Roblox's Terms of Use or stop working after a Roblox update. Read Roblox's rules and use Rostr at your own risk.

## Uninstall

Close Rostr and delete `Rostr.exe`. To remove your saved data too, delete the `%LocalAppData%\Rostr` folder. Your screenshots and recordings are not in that folder, so they stay until you delete them.

## Issues and feedback

Found a bug or have an idea? Open an [issue](https://github.com/stepak2321/rostr.trebka.net/issues) on this repository. Want to help with the code? See [CONTRIBUTING.md](CONTRIBUTING.md).

Would you like Rostr in another language, or with a theme you made up? Open an issue and tell us which one. Texts that are not translated yet stay in English until they are added.

## License

Rostr is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
