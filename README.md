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

The build bakes two addresses into the app: this machine's address on the local network, and
`127.0.0.1`. The app tries both and keeps whichever answers first, so one APK works over Wi-Fi and over
a cable. `-DataServerUrl` replaces the first, and `-NoDataServer` bakes neither.

**Content server with the admin UI.**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\serve-data.ps1
```

That prints every address a phone can use, starts the server, and forwards the port over USB when a
device is attached. Open <http://127.0.0.1:8787/> for the editor. If a phone on the same Wi-Fi still
cannot connect, the firewall is almost always the reason: the script adds the rule when it is run as an
administrator, and prints the one line to run by hand when it is not. `node server\test.js` runs 215
checks over the server in-process.

**The app will not start without a server.**

`data/dev.json` carries a `requireServer` flag, and it is `true` in this repository. When none of the
addresses answers, the app shows a screen that says so, lists the addresses it tried, offers a field to
correct the address and a "try again" button, and lets nothing else happen until one of them answers. It
also retries by itself every ten seconds, so starting the server after the app is not a dead end.

That is a deliberate departure from the brief, which only asks the game to survive a missing server. Set
`requireServer` to `false` in `data/dev.json` - or from the admin page, which serves that same file - and
the bundled data becomes the floor again, with the status chip reporting where the content came from.

**Tests.**

```powershell
& "F:\Unity\Editors\2022.3.62f3\Editor\Unity.exe" -batchmode -runTests `
  -projectPath . -testPlatform EditMode -testResults Logs\tests-editmode.xml
```

72 EditMode tests cover the JSON reader, the string fallback chain, Persian number formatting, the whole
morning transition matrix, the pair game's round building, and the byte identity of the supplied data
files. They also fail the build if any Persian text is hard-coded in a script.

## How the work is laid out

The brief asked for three days of history, so the commits come in three phases and each one is tagged:

| Tag | What it delivered |
| --- | --- |
| `phase-1-data-and-rules` | the supplied files under checksum, the JSON reader, the plant model, the morning table, the string fallback chain, the pairs game, and the 72 tests |
| `phase-2-scene-and-server` | the Persian fonts, the window and the pot, the interface, the Android build tooling, the thin content server with bundled fallback, and the docs |
| `phase-3-authored-assets-and-live-update` | the scene baked into objects you can edit, generated art written out as real assets, sound hooks, and the content editor that pushes a revision to a running app |

Each tag marks the last commit of its phase, so `git show <tag>` is a summary of that day's work. One
honest note: this machine's clock records two calendar dates across those three phases, because the rework
and the review ran back to back. Nothing is backdated, and `git log --date=short` shows it.

## Making it yours

Nothing below needs a change of code, and none of it is final: the stand-in art is a starting point, and
the scene is where you replace it.

| To change | Do this |
| --- | --- |
| The pot, the soil, the stem, the leaves, the seed, the blossom, the badges | Drag your object into the matching slot on the `PotRig` component of `World/Pot`. |
| How big your plant parts are | `StemUnitHeight` is how tall your stem mesh is at scale 1; `LeafUnitScale`, `SeedUnitScale` and `BadgeScale` size the rest. `MaxLeafPairs` is how many pairs a stage may show. |
| The leaf and stem colours that signal thirst | `ColorPlantParts` on the rig. Turn it off when your own art carries its own colours — posture, soil wetness and the badges still say it. |
| Your own soil surface | `SoilMaterial` on the rig. Empty keeps the stand-in; the wet-to-dry tint is applied over whatever you put there. |
| Any interface icon | Overwrite the matching PNG in `Assets/Resources/SmallPotSprites/`. `TextureFactory` loads from there before generating anything, so your drawing wins. |
| A panel or button background, or anything else about a widget | Change it in the scene. The interface is authored once and after that it is yours: a widget already there keeps its rect, its font, its sizes, its colours and its sprite, and only the words keep coming from the data. |
| Materials and meshes | `Assets/Materials/Generated/` and `Assets/Meshes/Generated/`. Generated in code means unsaveable, which is why the baker writes them out. |
| The font | `FontAsset` on the `App` object. `Window/SmallPot/Generate Project` rebuilds the font atlas and the scene together. |
| Sound | `AudioDirector` on `App` has one slot per moment — click, correct, wrong, reward, water, morning, growth, thirst. Empty is silence, so add clips one at a time. |
| A whole new pot state | Add the stage to `growth.stages` in `config.json` plus a `pot.state.<id>` string. The visuals resolve parametrically, so nothing in the scene needs a new prefab. |

Three menu items help while editing. **Small Pot / Rebuild Stand-in Scene** regenerates the camera, the
lighting, the `World` and the interface from scratch and re-bakes the art — it asks first, and it only
replaces the objects it made. **Small Pot / Bake Generated Art to Assets** writes out any art that is
still only in memory, which is what you want after editing the generator by hand. **Window/Small Pot /
Rebuild Interface From Code** hands the interface back to the builders: it drops the marks that say a
widget has been finished by hand, so the next build lays the whole thing out from the code again. You only
need it when the code's own layout is what changed; a widget that is already in the scene is otherwise
never touched again while the game runs.

Note the difference between the two kinds of rebuild. **Rebuild Stand-in Scene** and **Generate Project**
make the scene from scratch, so hand-finished work *in the scene* is replaced by what the code says — that
is what those menu items are for. The previous scene is copied to `Logs/backups/` first, so a rebuild is
never a one-way door. The runtime is the part that defers to you, and it does so on every launch.

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

More than one address is tried, because the same server is at a different address depending on how the
phone is connected. The build bakes the machine's address on your network and `127.0.0.1`, and the first
one that answers is the address used from then on. An app that starts before the server does keeps
retrying quietly in the background, and the settings panel takes an address by hand if you would rather
point it somewhere else without rebuilding.

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
* **Push to clients** bumps a revision, and every running client hears about it in the time one round trip
  takes: the app holds a request open and the push is what answers it, so there is no poll interval to sit
  through. What arrives is *offered* rather than applied — a card that names the revision, one sentence
  saying the game has stopped where it is, and a button that takes it. Nothing is applied behind the
  player's back, and nothing is lost while they decide: the round clock stops with the card and picks up
  where it left off;

Saving a file does not push it, and a file that is invalid JSON, or that breaks a rule such as a stage
with no matching `mornings` entry, is refused and left untouched. Reset restores any file to the
supplied version byte for byte.

A running client also notices when the server stops answering: a build that insists on one asks every few
seconds, and a connection that drops while you are playing pauses the game and puts a prompt up saying what
happened, resuming from where it stopped when the server is back. `127.0.0.1` is never offered on a phone —
a device cannot reach it, so the build does not bake it and the app ignores it wherever else it turns up;
type it by hand to use a USB cable. And a cached copy never outlives the build that wrote it, so installing
a new APK starts from the files inside it.

The chip in the corner is a demo aid rather than a player-facing thing: it appears when the data did not come
from the app bundle, and the number beside it is **how many of the six data files came from there** — "از سرور
· ۶ پرونده" is a client running entirely on what the server sent, while a smaller number means the rest came
from its own copy.

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
