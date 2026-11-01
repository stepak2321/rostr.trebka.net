# Rostr

**See the server before you join it.**

Rostr is a Windows launcher for Roblox. It shows every server's players, ping, FPS and region before you click, lets you jump into a random one, and gives you tools while you play: a stats overlay, a quick menu with friends, a game wiki and notes, screenshots, recordings and instant replay.

> Rostr is an independent project. It is not made by, affiliated with or endorsed by Roblox Corporation. Roblox is a trademark of Roblox Corporation.

## Contents

- [Download](#download)
- [Getting started](#getting-started)
- [Find and join a game](#find-and-join-a-game)
- [While you play](#while-you-play)
- [Accounts and the Roblox client](#accounts-and-the-roblox-client)
- [Your app, your way](#your-app-your-way)
- [Updates](#updates)
- [Good to know](#good-to-know)
- [Privacy and security](#privacy-and-security)
- [Disclaimer](#disclaimer)
- [Uninstall](#uninstall)
- [Issues and feedback](#issues-and-feedback)
- [License](#license)

## Download

Get `Rostr.exe` from the [latest release](https://github.com/stepak2321/rostr.trebka.net/releases/latest) and double-click it. There is nothing to install, and you do not need to install .NET.

**Requirements:** Windows 10 version 2004 (build 19041) or later, or Windows 11 (64-bit), and a Roblox account.

## Getting started

1. Run `Rostr.exe`.
2. Add your account with the login button in the sidebar. A small browser window opens for you to log in to Roblox.
3. Search for a game by name, place ID or roblox.com link. Press **Open** to choose a server, or **Play** to join one straight away.

You can also press Play on roblox.com. The game opens in Rostr first, so you can pick a server before joining. Joins to a friend or to a specific server from the website go straight into Roblox. If you would rather skip Rostr's server list completely, turn on **Skip choosing a server** in Settings > General.

The sidebar has Library, Trending, Friends and Gallery, plus **Open README** and **Open web** at the bottom.

## Find and join a game

- **Server browser.** Every server is a card with a ping-colored edge, its players, ping, FPS and region. Regions are detected while the list loads. Sort by most or fewest players or best ping, and filter by region, minimum players or maximum ping. Green is smooth, amber is fine, red is trouble.
- **Play, Server hop and Rejoin.** Play joins a random server in one click. Server hop joins a random server you have not been in yet and asks whether to stay or hop again. Rejoin takes you back to your last server. Without Multi Roblox, Rostr closes a Roblox that is still running (even one sitting in the tray) and starts a fresh session.
- **Shareable server links.** Press **Copy link** on a server to get a `rostr://join?place=…&job=…` link. A friend with Rostr opens it and lands in the same server. Rostr always asks before joining, accepts only a numeric place ID and a valid server ID, and needs an account selected.
- **Rate-limit handling.** If Roblox answers the server list with HTTP 429 (too many requests), Rostr waits and retries by itself, following the `Retry-After` time Roblox sends, or backing off 1, 2 and 4 seconds when it sends none. Lists are cached for a few seconds and identical requests are shared, so pressing Refresh again does not hammer the API. If Roblox keeps refusing, Rostr shows the last list it has, or tells you how long to wait.
- **Library.** Your favorites and recent games as tiles with a large picture, your playtime on each one, and Play, Servers and Rejoin buttons. It also has stat cards (total playtime, games played, most played) and a box for private server links. Games you join from the Roblox app show up in Recent too.
- **Friends.** IN GAME, ONLINE and OFFLINE counts, a search box, an offline toggle, and a card for each friend with a Join button when you can join them.
- **Trending.** The player counts of the games on the Rostr list, sorted by players right now. Click a game for a graph of the last 24 hours, 7 days or 30 days, with NOW, PEAK and LOWEST. The counts are collected every hour by a GitHub workflow (`.github/workflows/trending.yml`) and Rostr only reads them, so nothing is counted on your PC. Missing a game? Press **Request a game** and fill in the issue form.

## While you play

### Quick menu

Press **Left Ctrl + Tab** while Roblox is in front. The game gets darker and blurred and a menu opens on top of it, while the game keeps running underneath. It only reacts while Roblox is in front and never takes Ctrl + Tab from other apps. Change the key, or turn the menu off, in Settings > Overlays.

- **Time.** Your local time and how long you have been playing. The playing time counts from the moment you get into the game and starts again for every game you join.
- **Friends playing.** Your friends who are in a game right now, with their picture, the game they are in and a **Join** button that starts Roblox in their server. Drag the top of the panel to move it. The list uses the active account and refreshes by itself while the menu is open.
- **Game wiki.** A small browser with the Fandom wiki of the game you are in, so you can read about the game without leaving it. It has Back, Reload, Wiki home and Search Fandom buttons, and it loads a new wiki when you change games. You can type in it and the menu stays on.
- **Notes.** A note pad with a separate note for every game, for codes, trade values or quest steps. It is saved on your PC a moment after you stop typing, is still there after a restart, and holds up to 8,000 characters.
- **Dock buttons.** A dock on the left has buttons for Friends, Wiki and Notes, and for **Server hop**, **Rejoin** and **Copy join link** for the game you are in. Server hop and Rejoin switch the menu off so it does not cover the new client while it starts.

Rostr remembers which panels were open and where you put them. The blur needs Windows transparency effects, otherwise the screen is only darkened.

### Stats overlay

A small click-through box in the corner of the Roblox window, shown only while Roblox is in front. Choose its lines in Settings > Overlays: ping, CPU, RAM, session time, region and account name. You can also pick the corner and preview it. The default hide and show hotkey is `Ctrl` + `Shift` + `O`, and you can change it. For the in-game FPS counter press `Shift` + `F5`.

### Gallery, recording and instant replay

- **Gallery.** Screenshots and recordings of your Roblox games in one page. Press **Screenshot** or **Record**, or use the hotkeys while you play: `Ctrl` + `Shift` + `F10` takes a screenshot and `Ctrl` + `Shift` + `F11` starts and stops a recording. Click a tile to look at the picture or play the video inside Rostr, rename it, mark it as a favorite, copy it to paste into Discord or a chat, show it in its folder or delete it. Filter by All, Favorites, Screenshots or Videos. Files go to `Pictures\Rostr` by default.
- **Recording settings.** In Settings > Gallery choose 30 or 60 FPS, the quality (Low, Medium or High), whether to record game sound, the hotkeys and the folder. Recordings capture the Roblox window with Windows Graphics Capture and sound from the Roblox Player process only, so audio from other apps is not included. Recording needs ffmpeg, a free tool: Rostr asks before downloading it (about 80 MB) the first time you press Record or turn on Instant replay.
- **Instant replay.** Turn it on in Settings > Gallery and choose a buffer of 15, 30, 60 or 120 seconds (30 by default). Press `Ctrl` + `Shift` + `F9` (changeable) or **Save replay** to save that time as a clip, like on a console. Rostr shows whether the buffer is recording, waiting for Roblox or paused during a normal recording. It uses some CPU, and the buffer lives in a temporary folder that is deleted when Rostr closes.

### Discord status

Shows the Roblox game you are in on your Discord profile, with the game name, its creator, a timer and two buttons: Get Rostr and See game page. With no game running it says you are in the launcher, or "Browsing servers" with the name of the game page you have open. In Settings > Discord you can turn it off, hide the game name, or switch off **Show browsing servers on Discord**. It does nothing if Discord is not running.

## Accounts and the Roblox client

- **Multiple accounts.** Add as many accounts as you like and switch from the sidebar. Saved logins are encrypted with your Windows login.
- **Multi Roblox.** Run more than one client at once. Rostr tags every client with the account that started it and saves the tags, so they survive a restart of Rostr. Launching again with an account (Play, Rejoin or Server hop) closes that account's old clients, so they never pile up. Clients of other accounts are left alone, and a client that a running session of another account is using is never closed. A client belongs to the newest launch that started before it, so two launches close together cannot take each other's client.
- **Your own Windows session only.** Rostr detects, closes and records only the Roblox clients in its own Windows session. Roblox running in an RDP session or under another Windows user is ignored.
- **FPS cap and FastFlags.** Found in Settings > Performance. Set an FPS cap with the slider (30 to 360) or type a higher number. One-click FastFlag presets cover low graphics, high quality and Direct3D 11, and raw FastFlags can be edited too. Rostr tells you when Roblox will ignore a flag you saved. Two buttons open the list of flags Roblox still allows (the official announcement and a community copy) in Chrome, or in your default browser if Chrome is missing.
- **Client management.** Rostr installs the Roblox client for you and removes old versions so they do not pile up on your disk. Settings > Storage shows how much space Rostr uses and has a **Clean now** button. It never deletes anything while a client is running.

## Your app, your way

- **Tray.** Closing the window keeps Rostr running in the tray (you can turn this off in Settings > General). Right-click the icon for a menu Rostr draws itself: the version, your active account, whether Roblox is running, switches for the stats overlay, Discord status and Skip choosing a server that stay open while you change several, Screenshot and Record under Capture, and Exit.
- **Other options.** In Settings > General: minimize Rostr when the game starts, keep it in the tray, start it with Windows, skip choosing a server, and turn Multi Roblox on or off.
- **Languages.** English, Čeština, Deutsch, Español, Français, Polski, Português, Русский and Türkçe. Rostr follows your Windows language on first start, and you can change it in Settings > Appearance. Texts that are not translated yet stay in English. Is yours missing? Press **Request a language** in Settings > Appearance.
- **Themes.** Dark, Midnight, AMOLED, Ocean, Forest, Sunset, Rose and Light. They repaint the app instantly, with no restart.
- **Organized settings.** A tab list on the left: About, Performance, Overlays, Gallery, General, Appearance, Discord and Storage.
- **README and change log inside the app.** **Open README** shows this page without opening a browser, and **See change log** shows what is new in a version.

## Updates

Rostr checks the [releases](https://github.com/stepak2321/rostr.trebka.net/releases) page shortly after it starts and then every few hours. When a newer version exists you can choose **Install now**, **See change log** or **Not now**. The download is checked against its SHA-256 before it is installed. You can also check by hand in Settings > About with **Check for updates**.

## Good to know

- Since 29 September 2025 Roblox only reads a short allowlist of FastFlags from `ClientAppSettings.json` and silently ignores all others. Rostr's presets only use flags from that list, but Roblox can change the list at any time, so a preset can stop working after a Roblox update. The FPS limit is not a FastFlag: use the FPS cap in Settings > Performance.
- A ping marked with a tilde (`~`) is an estimate. Roblox servers do not answer ping, so Rostr measures the last router on the way instead.
- Rostr keeps its data in `%LocalAppData%\Rostr`: your saved accounts (encrypted), settings, FastFlag overrides, game notes, the client tags, the log file (emptied every time Rostr starts), ffmpeg if you downloaded it, and a temporary `replay` folder while Instant replay is on. Screenshots and recordings are saved in `Pictures\Rostr`, or the folder you chose in Settings > Gallery.
- To catch Play on roblox.com, Rostr registers itself for `roblox-player:` links for your Windows user only. It saves the original handler the first time so it can be restored. For shareable server links it also registers its own `rostr://` links, again for your Windows user only. Nothing else uses that scheme.
- Roblox limits how often the server list can be requested. Rostr retries and caches as described above, but if you refresh over and over you can still be asked to wait a little.
- If a game closes by itself, open Settings > About > **Open log file** and look for the "Multi Roblox: closed" lines. They show which account's client Rostr closed and why.

## Privacy and security

- A Roblox cookie gives full access to an account. Rostr stores saved logins **encrypted with Windows DPAPI**, so only your Windows user can read them, and sends them **only to roblox.com**. Never share your `accounts.json` or paste a cookie someone else gave you.
- Rostr does not intentionally write cookies or login tickets to its log file.
- Besides Roblox, Rostr contacts:
  - `api.github.com` and `raw.githubusercontent.com` for updates, this README, the change log and the Trending counts
  - `setup.rbxcdn.com` to download the Roblox client
  - `www.gyan.dev` to download ffmpeg, only if you press Record or turn on Instant replay and agree to the download
  - `ipinfo.io` and `ipwho.is` to look up the region of a game server from its IP address. These services see your IP address like any website you visit.
  - a web search and Fandom, only when you open the wiki in the Quick menu, to find the wiki of the game you are in
  - Discord's local pipe for the Discord status. Rostr itself sends nothing to Discord over the internet for this.
- There is no telemetry and no account on any Rostr server.

To report a security problem, see [SECURITY.md](SECURITY.md).

## Disclaimer

Rostr is provided as is, without warranty. Features such as running several Roblox clients (Multi Roblox) and editing FastFlags change how the official client runs. They may go against Roblox's Terms of Use or stop working after a Roblox update. Read Roblox's rules and use Rostr at your own risk.

## Uninstall

Close Rostr and delete `Rostr.exe`. To remove your saved data too, delete the `%LocalAppData%\Rostr` folder. Your screenshots and recordings are not in that folder, so they stay until you delete them.

## Issues and feedback

Found a bug or have an idea? Open an [issue](https://github.com/stepak2321/rostr.trebka.net/issues). Want to help with the code? See [CONTRIBUTING.md](CONTRIBUTING.md).

Would you like Rostr in another language, or a game on the Trending list? Use **Request a language** in Settings > Appearance or **Request a game** on the Trending page, or open an issue yourself. Have a theme idea? Open an issue too.

## License

Rostr is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
