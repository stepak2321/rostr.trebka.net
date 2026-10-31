# Contributing

Thanks for helping with Rostr! Bug reports, translations, themes and pull requests are all welcome.

## Build from source

You need Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (the app uses WPF).

```bat
dotnet run --project RobloxLauncher -c Release
```

Helper scripts in the repository root:

- `build.bat` asks for a version (for example `1.4.0`) and publishes a single-file, self-contained `dist\Rostr.exe`.
- `Start.bat` starts `dist\Rostr.exe`, or runs from source with `Start.bat dev`.
- `Clean.bat` removes `bin\`, `obj\` and `.vs\`.

## Project layout

```
RobloxLauncher/
  Core/            Roblox API and login, launching, updater, Discord, tray, multi-instance, translations
  settings/        App settings and FastFlag handling
  Assets/          Logo and icon
  MainWindow.*.cs  UI logic, split by page (Play, Friends, Library, Settings, ...)
  *.xaml           Windows and styles
build.bat  Start.bat  Clean.bat
```

## Guidelines

- Keep changes focused and match the existing style (nullable enabled, small helper classes in `Core/`).
- UI text goes through `Loc.T("...")`. Translations live in `Core/Loc.cs` and `Core/Loc.Strings.cs`. Text without a translation falls back to English.
- **Never commit** cookies, `accounts.json`, logs or any real account data, not even in examples or screenshots.
- Open a pull request that says what changed and why. By contributing you agree your work is licensed under the [MIT License](LICENSE).

## Publishing a release (maintainers)

1. Run `build.bat` and enter the version, for example `1.4.0`.
2. Create a GitHub release with the tag `v1.4.0` and attach `dist\Rostr.exe` (optionally also `Rostr.exe.sha256`).
3. Installed copies find the new release and offer the update.

## Forking

Change `GitHubRepo` in [`RobloxLauncher/Core/ProjectInfo.cs`](RobloxLauncher/Core/ProjectInfo.cs) to your own repository. Otherwise your build checks the original repository for updates and offers its releases.
Also replace the Discord application ID and site link in `Core/DiscordPresence.cs` if you want your own Rich Presence branding.
