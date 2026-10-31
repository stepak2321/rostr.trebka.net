## Rostr 1.3.1

### Improved
- **Quick actions in the dim menu.** The dock on the left side has three new buttons for the game you are in. **Server hop** joins a random server you have not been in yet, **Rejoin** starts the same server again, and **Copy join link** puts a link on the clipboard that a friend with Rostr can open to join your server. The button shows a check mark when the link was copied, or a cross when there is no server to link to yet. Server hop and Rejoin switch the dim off, so it does not cover the new client while it starts.
- **Notes in the dim menu.** A new Notes button in the dock opens a small note pad on top of the dim, with a separate note for every game, for example codes, trade values or quest steps. The title shows the game you are in, and the note changes by itself when you change games. Just click it and type, the dim stays on while you do. The note is saved on your PC a moment after you stop typing, and it is still there after a restart. Drag the top to move it, press X to close it. Rostr remembers where you put it. Notes are 8,000 characters at most.

### Fixed
- **Games started from the Roblox app are detected.** When you left a game and started another one from the Roblox app while the client stayed open, Rostr kept thinking you were still in the first game. Rostr now follows the game the client is really in.
- **The wiki follows the game.** The game wiki in the Dim menu loads the wiki of the new game when you change games, also when the wiki was already open or the new game was started from the Roblox app.
- **These games show in the Library.** A game you join from the Roblox app is added to Recent, with its name and picture, the same as a game started from Rostr.
- **No old server info on the next game.** After you leave a game, the old server address and region are cleared, so the next game does not show or save the previous region.

This release includes the features from [Rostr 1.3.0](https://github.com/stepak2321/rostr.trebka.net/releases/tag/v1.3.0).
