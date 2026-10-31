## Rostr 1.2.5

### New
- **Shareable server links.** Press **Copy link** on any server to copy a `rostr://join?place=…&job=…` link. A friend with Rostr can open it and land in the same server. Rostr always asks before joining, and only accepts a numeric place ID and a valid server ID. It needs an account selected in the sidebar.
- **Rate-limit handling.** When Roblox answers the server list with HTTP 429 (too many requests), Rostr now retries by itself, following the `Retry-After` time Roblox sends, or backing off 1, 2 and 4 seconds when it sends none. Server lists are cached for a few seconds and identical requests are shared, so pressing Refresh again does not hammer the API. If Roblox keeps refusing, Rostr shows the last list it has, or tells you how long to wait.
- **Show browsing servers on Discord.** A new switch in Settings > Discord. Turn it off and opening a game page no longer shows "Browsing servers" and the game name on your Discord profile. It only says you are in the launcher. Playing a game is not affected.

- **Gallery.** A new page in the sidebar, under Friends, for screenshots and recordings of your Roblox games. Press **Screenshot** to save a PNG of the game, or **Record** to save an MP4 of it. Files are named after the game and the time, for example `Bee Swarm Simulator 2026-10-26 18-30-12`. Rostr tells you when a file is saved, with a tray notification if you are inside the game.
- **Hotkeys that work in the game.** `Ctrl` + `Shift` + `F10` takes a screenshot and `Ctrl` + `Shift` + `F11` starts and stops a recording, even while Roblox is in front.
- **Look at your files inside Rostr.** Click a tile to open the picture or play the video right in the app, with play, seek and volume controls. You can copy it (to paste into Discord or a chat), show it in its folder or delete it. Filter the page by All, Screenshots or Videos.
- **Recording uses ffmpeg.** The first time you press Record (or turn on Instant replay), Rostr asks to download ffmpeg (a free tool, about 80 MB) into its own data folder. It is only used for recording and for the video thumbnails.
- **New Settings tab: Gallery.** Change the screenshot and recording hotkeys (click the key box, press the keys you want, Esc cancels, Reset restores the default), and choose where your screenshots and recordings are saved. By default they go to `Pictures\Rostr`. If another app already uses the keys you pick, Rostr keeps the old ones and tells you.
- **Recording settings.** In Settings > Gallery, a new Recording card lets you choose the frame rate (30 or 60 FPS), the quality (Low, Medium or High) and whether to **record game sound**. Before, recordings were fixed at 30 FPS with no sound. Higher quality and 60 FPS look better but make bigger files and use more CPU. Sound is off by default.
- **Game sound in videos.** With **Record game sound** on, the video carries everything your PC plays through the speakers or headphones. That is the sound of the whole PC, not only Roblox, so music or a Discord call you hear will be in the video too. If no sound device can be used, Rostr records the video without sound and tells you why.
- **Instant replay.** Turn on **Keep the last 30 seconds** in Settings > Gallery, and Rostr records Roblox quietly in the background while it is open. Press `Ctrl` + `Shift` + `F9` (you can change it in Settings > Gallery) and the last 30 seconds are saved as an mp4 in your Gallery, named after the game and the time with "replay" at the end. A **Save replay** button on the Gallery page does the same. It uses the frame rate, quality and sound you picked. The first time you turn it on, Rostr asks to download ffmpeg if it does not have it yet.
- **Rename and favorite clips.** Open a screenshot or video in the Gallery viewer and use **Rename** to give it a new name (Enter saves, Esc cancels) or **Favorite** to mark it with a star. Favorites show a star on their tile, and a new **Favorites** filter shows only them.

### Improved
- **The log starts fresh on every launch.** `launcher.log` is emptied each time Rostr starts, so it never grows from run to run.
- **Server browsing, reworked.** Each server is now a card with a ping-colored edge, people and region icons, a bold ping, a green Join button and a dimmed Copy link. Sort and filter options sit together in one bar with SORT and FILTERS labels.
- **Library, reworked.** Games are shown as tiles with a large picture, the playtime on the picture, a star in the corner and Play, Servers and Rejoin buttons. The tiles stretch to fill the row, and the number of columns follows the window width. The stats are separate cards, private server joining has its own card, and Favorites and Recent have headings with a count.
- **Ready to play page, reworked.** A bigger logo and title, three shortcut tiles (Library, Friends, Roblox website) and a cleaner Find a game card.
- **Friends tab, reworked.** IN GAME, ONLINE and OFFLINE counts at the top, one toolbar with search, the offline toggle and Refresh, and a card for each friend showing their display name, @username and status. Join only appears when you can actually join that friend.

### Fixed
- Fixed the server list failing straight away with a rate-limit error when you refreshed quickly. It now waits and retries first.
- Fixed recording failing right after it started with `Unrecognized option 'hide_banner'` when a very old ffmpeg was installed on the computer. Rostr now ignores an ffmpeg that is too old or has no screen capture, and offers its own download instead.

### Notes
- Recording works best with Roblox in a window or borderless window. In real fullscreen the game area is the whole screen. Do not resize the Roblox window while recording.
- Texts added in this version may still show in English in some languages.
- Instant replay keeps about 30 seconds in a temporary folder inside Rostr's data folder and deletes it when Rostr closes. A saved replay can start up to a second behind the moment you pressed the key. It pauses while you record a video yourself, and starts again afterwards. It only records while the Roblox window is visible and not minimized, and it restarts by itself if you resize the window.
- Game sound is taken from your default output device. If you switch headphones or speakers while recording, the sound of the new device is not added until the next recording.
- A shared server link only works for someone who has Rostr. Servers can fill up or close before your friend opens the link.
- Roblox can change how often it allows the server list to be requested. If you still get asked to wait, wait a few seconds and press Refresh.
- Rostr is an independent project and is not affiliated with Roblox Corporation.
