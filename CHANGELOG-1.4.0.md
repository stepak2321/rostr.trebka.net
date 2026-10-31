## Rostr 1.4.0

### Improved
- **Trending page.** A new Trending button in the sidebar shows how many people are playing the games on the Rostr list. The list is sorted by players right now. Click a game to see a graph of its player count over the last 24 hours, 7 days or 30 days, with the date, time and number of players when you hover the line. Above the graph you see NOW, PEAK and LOWEST for the range you picked. Press Refresh to load the list again, and the page tells you when the numbers were last counted.
- **Counted by GitHub, not by your PC.** The player counts are collected once an hour by a GitHub workflow and saved in the `trending` folder of the repository. Rostr only reads those files, so nothing runs on your PC and nothing about you is sent anywhere. Each game keeps its last 30 days. The list holds up to 100 games.
- **Request a game.** Missing a game on the Trending list? The new **Request a game** button next to Refresh opens a GitHub issue with a short form where you paste the game link or place ID. If the game fits, it is added to the list and shows up after the next hourly count.
- **Request a language.** Settings > Appearance now has a **Request a language** button under the language list. It opens a GitHub issue where you write the name of the language and whether you can help translate it. Rostr does not add every request, but each one is read.

### Fixed
- **Texts of the new buttons are translated.** Request a language, Request a game and their short explanation are available in all nine languages.

This release includes the features from [Rostr 1.3.1](https://github.com/stepak2321/rostr.trebka.net/releases/tag/v1.3.1).
