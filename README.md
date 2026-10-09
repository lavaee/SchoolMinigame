# Small Pot — گلدان کوچک

A small Persian-language game about a pot on a window ledge. Collect drops in a short word game, water
the pot, and go to the next morning — that is when the plant grows, never straight away.

* **Engine:** Unity 2022.3.62f3, Built-in Render Pipeline, Linear colour space.
* **Target:** Android 9 and later (`minSdk 28`), portrait, ARMv7 + ARM64, IL2CPP.
* **Data-driven:** every word the player reads and every number the game counts lives in JSON. Replacing
  those files changes the game without rebuilding the app.

## Running it

**In the editor.** Open the project with Unity 2022.3.62f3, open `Assets/Scenes/Main.unity` and press
Play. The scene contains one empty object; the camera, the window, the pot and the whole interface are
built at runtime from the data files.

**On a phone.**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-apk.ps1
adb install -r Builds\SmallPot.apk
```

Add `-DataServerUrl http://127.0.0.1:8787` to the build command to bake in a content server address, and
`tools\serve-data.ps1` to serve `data/` over USB (`adb reverse`) or the local network.

**Tests.**

```powershell
& "F:\Unity\Editors\2022.3.62f3\Editor\Unity.exe" -batchmode -runTests `
  -projectPath . -testPlatform EditMode -testResults Logs\tests-editmode.xml
```

72 EditMode tests cover the JSON reader, the string fallback chain, Persian number formatting, the whole
morning transition matrix, the pair game's round building, and the byte identity of the supplied data
files. They also fail the build if any Persian text is hard-coded in a script.

## Where the data lives

The files the challenge supplied are in `data/` and are never edited:

| File | What it holds |
| --- | --- |
| `config.json` | every number: drops, rounds, seconds, costs, growth `mornings` |
| `strings-base.json` | the 27 strings the game can read |
| `strings.json` | four of those strings, overridden |
| `pairs.json` | 15 word/antonym/distractor entries |

`tools\sync-data.ps1` copies them into `Assets/StreamingAssets/data/`, which is the copy that ships
inside the APK. `tools\verify-data.ps1` checks their SHA-256 against the originals.

At runtime each file is resolved in this order:

1. a fresh copy from the data server, if one is configured and reachable;
2. the on-device cache in `persistentDataPath/data`;
3. the copy bundled in the APK.

A file that is missing, unreachable or not valid JSON simply falls through to the next source, so the
game is fully playable with no server at all. The gear button in the top corner shows which source each
file came from.

Two extra files are ours, not the challenge's: `data/dev.json` (the server address and debug flags) and
`data/strings-extra.json` (optional lines the game uses when it can). Both are lowest priority, and the
game still reads correctly with both deleted.

## Art and audio

There are no art or audio assets: the window, the pot, the plant, the icons and every button are meshes
and sprites generated in code at startup, in a deliberately flat isometric style. The only imported
assets are two fonts — Vazirmatn and Amiri, both OFL — and RTLTMPro (MIT) for Persian letter shaping.
Sources and licences are listed in [CREDITS.md](CREDITS.md).

## Deliberate omissions

* No accounts, cloud save, leaderboards or server-side game state: the server only hands out content.
* No sound. It was left out rather than shipped as unlicensed filler.
* No WebGL build — that module is not installed for this editor, so the APK is the deliverable.
* No analytics, crash reporting or third-party SDKs.
* The one-minute phone video is recorded by hand; the repository cannot record it.
