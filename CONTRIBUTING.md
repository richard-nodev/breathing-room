# Contributing

Thanks for wanting to help! Breathing Room is small on purpose, and every contribution should keep it kind, private and simple.

## The one design rule

**Breathing Room notices; it never tells people what to do.**

Many smokers push back hard against rules, and a countdown to the next cigarette turns the cigarette into an appointment. So the app has:

- **No targets, countdowns, "too early" warnings, streaks to lose, or health scare messages.**
- **Facts in plain words, and choices.** "12 min since the last one. That's #7 today." Never "you should".
- **A friendly tone.** Like a cat on your desk, not a clinic.

The reasoning is in [docs/DESIGN.md](docs/DESIGN.md). If a change makes the app nag, judge or pressure, it won't be merged, however clever it is.

## Building

You need Windows 10 or 11 and nothing else. The build uses the C# compiler that ships with Windows (.NET Framework 4.8).

```bat
build.cmd            :: runs the tests, then builds bin\BreathingRoom.exe
build.cmd package    :: also makes the release zip in dist\
build.cmd icon       :: redraws assets\icon.ico from the cat in src\CatView.cs
```

The tests run on every build, and a failing test stops it. Warnings are errors.

**The code must stay C# 5**, because that's what the built-in compiler understands. That means no `$"..."` strings, no `?.`, no `nameof`, and no `=>` members or properties.

## Project layout

| Path | What's there |
|---|---|
| `src/App.cs` | Startup, the global hotkey, the tray icon, and the controller that ties everything together. |
| `src/Store.cs`, `src/Stats.cs` | The data file and all counting logic (day boundary at 05:00, gaps, insights). No UI. |
| `src/Lines.cs` | Everything the cat says. |
| `src/PetWindow.cs`, `src/CatView.cs`, `src/SpeechBubble.cs` | The cat and its speech bubble. It's drawn with plain shapes, with no images. |
| `src/Friendship.cs` | Friendship points and levels, and the coats and accessories they unlock. |
| `src/Overlays.cs` | The afterglow and the corner badge. |
| `src/Play.cs`, `src/Scenes.cs` | The play window, its four rooms and the seven quick games. |
| `src/PlayRooftop.cs`, `src/PlayCafe.cs` | The two bigger games: Rooftop run and Café rush. |
| `src/Audio.cs`, `src/Sfx.cs` | A tiny waveOut mixer and the cat's voice. |
| `src/Updater.cs` | The daily update check against GitHub releases, and the download, checksum and exe swap behind *Update now*. |
| `src/*Window.cs` | Insights, settings, sound picker, Mochi's look, Ride the wave, welcome and about. |
| `assets/sounds/` | Real cat recordings (CC0), with `sounds.txt` listing them. They are embedded into the exe at build time. |
| `tests/Tests.cs` | Logic tests. |
| `tools/MakeIcon.cs` | Renders the app icon from the cat drawing. |
| `docs/` | Design notes and the README pictures. |

## Test mode

Set `BR_DATA_DIR` to an empty folder before starting the app and it runs in **test mode**:

- **Separate data:** it uses that folder instead of your real data.
- **Your PC is left alone:** it never touches the Windows startup list.
- **Scriptable:** it listens for commands written to `command.txt` in that folder, so you can drive it without a mouse.

```powershell
$env:BR_DATA_DIR = "$env:TEMP\br-test"
.\bin\BreathingRoom.exe
Set-Content "$env:TEMP\br-test\command.txt" "log"        # log a cigarette
Set-Content "$env:TEMP\br-test\command.txt" "insights"   # open "How it's going"
```

Commands:

- **Logging:** `log`, `retro`, `missed`, `back`, `later`
- **Windows:** `menu`, `insights`, `settings`, `voice`, `look`, `about`
- **Play and breathing:** `play:<game>:<room>` (games: `fetch`, `laser`, `treats`, `bubbles`, `mouse`, `hide`, `flight`, `run`, `cafe`; rooms: `living`, `garden`, `night`, `cafe`), `breathe`, `wave`
- **Mochi:** `stretch` (the stretch-break nudge right now), `friend:<points>`
- **Updates:** `update-check`, `update-now`. In test mode, set `BR_UPDATE_URL` to a local server's release JSON (shaped like GitHub's `releases/latest`) to try the whole update without GitHub. Test runs never contact GitHub otherwise.
- **Measuring:** `frames` writes the number of game frames so far to `frames.txt` (and the drawing mode in use to `drawing.txt`), so CPU per frame can be worked out. Set `BR_RENDER=software` or `BR_RENDER=hardware` to pin the drawing mode, and `BR_CACHE=off` to switch off the cached room bitmaps.
- **Sound:** `sound`, `soundinfo`, `pick:<moment>:<clip>:on|off`
- **Pictures:** `snap:<folder>`
- **Quitting:** `quit`

`scripts/ui-smoke.ps1` uses this to click through the whole app and check that nothing errors.

## README pictures

All pictures in `docs/images` are rendered by the app itself, never screenshotted, so they never contain anything from a real desktop:

```powershell
$env:BR_DATA_DIR = "$env:TEMP\br-snap"; .\bin\BreathingRoom.exe
Set-Content "$env:TEMP\br-snap\command.txt" "snap:$PWD\docs\images"
```

## Sounds

Only **real recordings** that are **CC0 or public domain** go in. No synthesized sounds. Add the clip to `assets/sounds/`, list it in `sounds.txt` (`id|kind|label|default moments`), and credit it in `CREDITS.txt`.

## Translations

Not wired up yet, but very welcome. If you'd like to translate, open an issue first so we can agree on how to structure it.

## Making a release

Every copy of the app checks this repository's releases about once a day, so publishing a release is all it takes to update everyone:

1. Raise the version in `src/AppInfo.cs` (all four places) and add a section for it at the top of `CHANGELOG.md`.
2. Commit, then tag and push: `git tag v1.1.0` and `git push origin v1.1.0`.
3. The [release workflow](.github/workflows/release.yml) runs the tests, builds the zip and its `.sha256` file, and publishes the release with that version's changelog as its notes.

Within a day, each running copy sees the new version, and **Update now** installs it. Keep the zip's name (`BreathingRoom-<version>-windows.zip`) and its checksum file: the updater looks for both.

## Pull requests

Keep them focused, describe the "why", and tick the checklist in the PR template. Be kind in reviews. See the [Code of Conduct](CODE_OF_CONDUCT.md).
