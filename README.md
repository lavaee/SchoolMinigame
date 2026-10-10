# Small Pot — گلدان کوچک

A small Persian-language game about a pot on a window ledge. Collect drops in a short word game, water
the pot, and go to the next morning — that is when the plant grows, never straight away.

* **Engine:** Unity 2022.3.62f3, Built-in Render Pipeline, Linear colour space.
* **Target:** Android 9 and later (`minSdk 28`), portrait, ARMv7 + ARM64, IL2CPP.
* **Data-driven:** every word the player reads and every number the game counts lives in JSON. Replacing
  those files changes the game without rebuilding the app.
* **Authored scene:** `Assets/Scenes/Main.unity` holds the camera, the lighting, the window, the pot and
  the whole interface as ordinary objects, so swapping a model or a sprite is an inspector job.

## Running it

**In the editor.** Open the project with Unity 2022.3.62f3, open `Assets/Scenes/Main.unity` and press
Play.

**On a phone.**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-apk.ps1
adb install -r Builds\SmallPot.apk
```

Add `-DataServerUrl http://127.0.0.1:8787` to bake in a content server address.

**Content server with the admin UI.**

```powershell
node server\serve.js                 # prints the admin URL and every LAN URL
adb reverse tcp:8787 tcp:8787        # so a USB-attached phone can reach it
```

Then open <http://127.0.0.1:8787/>. `node server\test.js` runs 215 checks over the server in-process.

**Tests.**

```powershell
& "F:\Unity\Editors\2022.3.62f3\Editor\Unity.exe" -batchmode -runTests `
  -projectPath . -testPlatform EditMode -testResults Logs\tests-editmode.xml
```

72 EditMode tests cover the JSON reader, the string fallback chain, Persian number formatting, the whole
morning transition matrix, the pair game's round building, and the byte identity of the supplied data
files. They also fail the build if any Persian text is hard-coded in a script.

## Making it yours

Nothing below needs a change of code, and none of it is final: the stand-in art is a starting point, and
the scene is where you replace it.

| To change | Do this |
| --- | --- |
| The pot, the soil, the stem, the leaves, the seed, the blossom, the badges | Drag your object into the matching slot on the `PotRig` component of `World/Pot`. |
| How big your plant parts are | `StemUnitHeight` is how tall your stem mesh is at scale 1; `LeafUnitScale`, `SeedUnitScale` and `BadgeScale` size the rest. `MaxLeafPairs` is how many pairs a stage may show. |
| The leaf and stem colours that signal thirst | `ColorPlantParts` on the rig. Turn it off when your own art carries its own colours — posture, soil wetness and the badges still say it. |
| Any interface icon | Overwrite the matching PNG in `Assets/Resources/SmallPotSprites/`. `TextureFactory` loads from there before generating anything, so your drawing wins. |
| A panel or button background | The first sprite assigned to an `Image` is kept, so replacing it in the hierarchy survives every rebuild. Colours keep being driven, because colour is what carries state. |
| Materials and meshes | `Assets/Materials/Generated/` and `Assets/Meshes/Generated/`. Generated in code means unsaveable, which is why the baker writes them out. |
| The font | `FontAsset` on the `App` object. `Window/SmallPot/Generate Project` rebuilds the font atlas and the scene together. |
| Sound | `AudioDirector` on `App` has one slot per moment — click, correct, wrong, reward, water, morning, growth, thirst. Empty is silence, so add clips one at a time. |
| A whole new pot state | Add the stage to `growth.stages` in `config.json` plus a `pot.state.<id>` string. The visuals resolve parametrically, so nothing in the scene needs a new prefab. |

Two menu items help while editing. **Small Pot / Rebuild Stand-in Scene** regenerates the camera, the
lighting, the `World` and the interface from scratch and re-bakes the art — it asks first, and it only
replaces the objects it made. **Small Pot / Bake Generated Art to Assets** writes out any art that is
still only in memory, which is what you want after editing the generator by hand.

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

### Editing content while the game is running

The server never writes to `data/`. It keeps a working copy in `server/content/`, seeded from `data/` on
first use, and that is what it serves and what the admin page edits:

* the left rail lists the six files with a dirty marker and a validity dot;
* `config.json` gets grouped number inputs and a stage list you can add to, remove from and reorder —
  the mornings table is derived from the order you put the stages in;
* the strings files get a key/text table with `dir="rtl"` inputs, and `pairs.json` a word/antonym
  table; every file also has a raw JSON view, so nothing is hidden;
* a live preview shows the growth chain and how many watered mornings full bloom takes, the economy
  (what one session earns against what watering costs), and **a silhouette of every pot state** drawn
  from the same shape rules the game uses, so you can see whether five states still look like five;
* **Push to clients** bumps a revision. A running client polls it every ten seconds, reloads, and says
  so in a quiet chip. Nothing is interrupted: a round in progress and a report on screen are left alone
  until they are finished.

Saving a file does not push it, and a file that is invalid JSON, or that breaks a rule such as a stage
with no matching `mornings` entry, is refused and left untouched. Reset restores any file to the
supplied version byte for byte.

## Art and audio

The stand-in art — window, pot, plant, icons and every button — is generated in code in a deliberately
flat isometric style, and then baked into the project as real assets: 21 PNG sprites under
`Assets/Resources/SmallPotSprites/`, 16 materials, 5 meshes and the scene itself. All of it is meant to
be replaced. The only imported assets are two fonts — Vazirmatn and Amiri, both OFL — and RTLTMPro
(MIT) for Persian letter shaping. Sources and licences are in [CREDITS.md](CREDITS.md).

There are no sound clips, deliberately: `AudioDirector` has a slot per moment and plays nothing until
you fill one, so the repository ships no unlicensed audio.

## Deliberate omissions

* No accounts, cloud save, leaderboards or server-side game state: the server only hands out content.
* No sound clips shipped — the hooks are there, the audio is yours.
* No WebGL build — that module is not installed for this editor, so the APK is the deliverable.
* No analytics, crash reporting or third-party SDKs.
* The one-minute phone video is recorded by hand; the repository cannot record it.
