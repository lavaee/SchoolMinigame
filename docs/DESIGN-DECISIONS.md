# Design decisions

Short answers to the questions this project is likely to be asked, with the reasoning behind each.

## Why Unity, and why an APK rather than a web page?

Unity was the safe choice for a phone build in the time available, and the brief allowed either. The
WebGL module is not installed for this editor, so a browser build would have meant installing a new
module and re-testing everything; an APK is also what "it runs on my phone" means most directly. The
project is nonetheless written so the same code would build for WebGL: no platform-specific APIs are
used outside `DataLoader`, which already abstracts file access behind `UnityWebRequest`.

## How does growth actually work?

The plant has a stage index plus a count of watered mornings spent toward the next stage. Going to the
next morning applies exactly one of four outcomes:

| Night | Result |
| --- | --- |
| Watered, and the required mornings are now complete | grows, progress resets |
| Watered, but not enough mornings yet | progress increases, nothing visible changes state |
| Watered while thirsty, no growth yet | recovers: thirst clears, growth is untouched |
| Not watered | thirst, once `afterMorningsWithoutWater` dry nights have passed |

Thirst never takes growth back. It only blocks progress: `stages` has no "thirsty" entry, so thirst is an
overlay on whatever stage the plant already reached. Watering itself changes nothing but the state of the
soil and a badge — the promise is that growth happens at the next morning, so it does.

The one-morning delay is also what makes the drop economy interesting: you spend four drops to prepare a
night, and you find out what it did the next time you press the button.

## Where do the numbers come from, and why are colours not in `config.json`?

Every number the player can feel — drops per correct answer, the reward threshold and bonus, the round
count, the timer, the watering cost, the mornings per stage, the animation budget — is read from
`config.json`. That is what the brief asked for, and replacing the file changes the game without a
rebuild.

The palette lives in code (`SceneKit.Palette`) because it is art direction, not a game rule: the brief
says the *numbers* come from config. Colours also have to stay legible together, which is a design
decision rather than a setting a non-programmer would want to tune blind. The timing budget is a middle
case, and the compromise is that the code derives plausible durations from the one value config does
give (`ui.maxAnimationMs`) instead of inventing new keys the supplied file does not have.

## What happens when the data is wrong?

Every source is optional. `DataLoader` tries a fresh server copy, then the on-device cache, then the
bundled file, and accepts a layer only if it parses as JSON. Anything missing falls through; a missing
*key* renders as `[key]` rather than blank or a crash; a `null` or wrongly-typed number keeps the
documented default and records a warning. `strings-base.json` alone is enough to play the whole game —
there is a test that asserts exactly that, so a damaged `strings.json` cannot break the build.

A missing-key marker is only ever visible in the debug panel, never in the flow the player is judged on.

## How do you know the five states are readable without words?

Four independent channels, none of them colour:

1. **Silhouette** — height, number of leaf pairs, and whether there is a blossom.
2. **Posture** — a thirsty plant's leaves tilt down and the whole plant sits lower.
3. **Soil** — thirsty soil is dry and pale.
4. **Badges** — a droplet when the pot has been watered, a struck-through droplet when it is thirsty.

`GrowthTests` builds all five states and asserts their visual signatures are pairwise different, so
"the states look distinct" is a tested claim rather than an opinion. Colour reinforces the signals but is
never the only one, which is also why the drop counter animates its number instead of just changing it.

## Why does the minigame shuffle, and why is there one timer?

Round order and option order are reshuffled on every play, from an injected `Random`, so the session
cannot be memorised and the tests can seed it. One timer covers all five rounds because a per-round
timer would punish a slow reader five times over; the clock is part of the tension, not a second
obstacle. Wrong answers show the right word separately, in the same card, and subtract nothing.

## Why is the scene authored now, when it used to be built at launch?

Because the first version optimised for the wrong thing. Building the world in code made "replace the data
and the game changes" true down to the geometry, which was a good demonstration — but it also meant there
was nothing in the scene to edit. No model to swap, no clip to attach, no widget to re-skin. Anyone taking
the project on would have had to work through the generator to change a leaf.

So the generator runs once, from a menu item, and bakes what it produces into the scene as ordinary
objects. The runtime then *binds* instead of creating: `PotRig` is a component whose references are
visible in the inspector, and the builders are idempotent — they find a widget by name and update it if it
is there, and only create it if it is not. Running the same builder in the editor and at startup is what
keeps the two in step: the editor pass puts the interface in the scene, the startup pass refreshes the
labels from the current data without replacing anything you re-skinned.

That last promise needed teeth. "Update it if it is there" still meant writing every font size, colour and
rect again on each launch, which quietly undid a hand-finished interface a frame after the scene loaded: a
resized rect snapped back, an `RTLTextMeshPro` setting reverted, and there was no way to finish the look by
hand. So a widget the builder *finds* is marked `AuthoredWidget`, and from then on it is left alone. The
layout and the component parameters belong to the scene; the builder refreshes only what the data owns —
the words, the sprites, the states. The first pass authors, every later pass defers.
`Window/SmallPot/Rebuild Interface From Code` drops those marks, for when it is the code's own layout that
changed and the scene should follow it.

The line is between the runtime and a rebuild, and it is worth stating plainly because it is easy to
assume otherwise. The *runtime* defers to the scene: every launch finds the widgets and leaves them alone.
A *rebuild* — `Generate Project` or `Rebuild Stand-in Scene` — starts from an empty scene and writes
everything from code, so hand-finished work inside the scene is replaced. That is the point of those menu
items, but it should never cost a day: the scene as it was is copied into `Logs/backups/` before the
rebuild starts.

Three details follow from that, and all three are the kind of thing that looks like a bug until you know
why:

* **A material created in code cannot be saved into a scene.** It carries `HideAndDontSave`, so the scene
  would come back with a white surface rather than a missing one. The baker writes real `.mat`, `.asset`
  and `.png` files instead.
* **Nine-slice borders and pivots do not survive a naive import.** The baker copies the border, the pivot
  and the mesh type from the sprite it generated, so the baked sprite is the same picture, and replacing
  one PNG with your own art is a drop-in.
* **`CreateMissingParts` is off in the shipped scene.** While it is on, a deleted part silently reappears
  on the next launch, which is exactly the wrong behaviour once the art is yours.

The cost is honest: a new *visual stage* is still pure data, because the stage profile resolves
parametrically, but a new *part* — a fruit, a trellis — is placed in the scene.

## Why does the server keep its own copy of the data?

The supplied files in `data/` are checked byte-for-byte by a test, and that check is only worth something
if nothing writes to them. So the server owns a working copy in `server/content/`, seeded from `data/` on
first use, and the admin page edits that. Reset restores any file from the supplied copy, so the fallback
is one button rather than a re-clone.

Pushing is deliberately separate from saving. A designer edits several fields, looks at the preview, and
then decides to push — bumping a revision number that a running client polls every ten seconds. The client
is never interrupted: a round set in progress and a report on screen are left alone, and the reload
happens between them. The notice is a three-second chip, because a player who is mid-something should not
be stopped by a content change they did not ask for.

The admin page mirrors the shape rules in `Assets/Scripts/Core/PlantVisuals.cs` to draw its silhouettes.
That is a deliberate duplication, and it is the one place in this project where two sources of truth
exist: a preview drawn by calling into Unity is not possible from a browser, and a designer editing a
sixth stage needs to see what six stages look like. The mirror is pinned by tests on both sides — the shape
numbers, the default-mornings fallback and the thirst posture are asserted for the same values.

## Why does the app try more than one address?

Because one server can be reached at more than one address and the phone cannot know which one is live: the
machine's address on the network over Wi-Fi, and - if you forward the port over a cable - `127.0.0.1`. The
app tries every address it has in turn and keeps the first that answers.

The one address it never offers on a device is loopback. A phone is a different machine, so `127.0.0.1`
there means the phone itself: an address that cannot possibly work, sitting at the top of the failure list
and in the settings panel looking like a setting somebody chose. So the build no longer bakes it, the
loader drops it on a device even when a cached or served file still contains it, and the gate never
suggests it. A typed address is obeyed, so the USB route still works: forward the port with
`adb reverse tcp:8787 tcp:8787` and enter `127.0.0.1` by hand. That is an instruction to follow; a baked
loopback is only a guess that happens to be wrong.

The order is decided by a pure function with its own tests, because the order is the whole feature.

The firewall is the other half of the same problem, and it cannot be solved from inside the app: Windows
blocks inbound connections by default, so a phone on Wi-Fi is refused even when the address is right.
`tools\serve-data.ps1` reports that state and prints the one-line rule that fixes it, adding the rule
itself when it happens to be running as an administrator.

## Why is the app's own configuration never read from the cache?

Because it is the one file that says where the server is. `dev.json` holds the address list and the flags,
and the loader used to treat it like the employer's content: remote first, then the cached copy, then the
copy inside the app. That order has a trap in it. A phone that had once cached a `dev.json` — from a build
made before an address was added, or before a flag such as `requireServer` existed — went on using that
copy. A new APK would ship with the right address baked into it and the phone would ignore it: still
pointing at localhost, still with the gate switched off, and nothing on screen to say so.

That is the shape both reported symptoms had. A content change that was pushed and never arrived, and a
"no connection" screen that appeared in the editor but not on the device, are the same failure seen twice:
the app was reading an old picture of itself.

So the app's own configuration skips the cache. It is either the copy inside the build or the copy the
server is serving right now, so a device cannot keep running on an address the build has moved on from, and
installing a new APK is enough to change where it looks. The employer's files keep the old order, because
for content a cached copy is a feature — it is what lets the game run with no server at all.

Skipping the cache for that one file was not enough on its own, though, because *any* cached file can be
older than the build that is running. A phone that installed a new APK kept the copies the previous one had
saved, so a file the build had since gained keys in was still missing them — which is how the connection
gate came to show `[gate.title]` on a phone while the editor, reading `data/`, showed the sentence. So the
cache is now stamped with the build that wrote it, and a different build drops it and starts from its own
files. The stamp is written back the moment the cache is cleared, or the next load would clear it again and
take a deliberately planted one with it.

## What happens when the server goes away mid-game?

The game stops and says so. A build that insists on a server asks whether it is still there every few
seconds — `connectionCheckSeconds` in `dev.json`, three by default — and when the answer stops coming it
pauses and raises the same gate the launch path uses, with a different sentence: this connection dropped,
rather than it was never there.

That is the opposite of the earlier behaviour, deliberately. A dropped connection used to be invisible:
the app kept the last answer it had, carried on playing, and only noticed at the next launch, which made a
dead server look like a working one. Pausing is also the kinder failure. The round set keeps its remaining
time and the drops it had earned, and the retry loop resumes from exactly where it stopped instead of
costing the player the session. The pause is done through time rather than by tearing anything down, which
is why the minigame's clock reads scaled time — with the clock at its normal speed that is the same number
it always was.

## Why can the app refuse to start?

Because silence is the worst possible answer to a failed connection. The first version of this app treated
a missing server as a shrug - the bundled files are a floor, so nothing is lost - and that is the right
posture for the brief. It is the wrong posture for a demo whose whole point is that the content on screen
came from the server a second ago. A phone that quietly plays bundled data looks exactly like a phone that
received the push, and a change that never arrived looks like a change nothing was listening for. That is
not a hypothetical: it is what happened, and it took opening the APK to find out why.

So `dev.json` carries `requireServer`. With it on, the game refuses to start until an address answers,
shows the addresses it tried, lets the address be corrected on the device, and retries every ten seconds
on its own. With it off, the original behaviour is intact, and the status chip still reports where the
content came from.

The cost is honest to name: the app is no longer playable with no server at all, so the brief's "a dead
server is not an error" is now a switch rather than a property. The switch is in a data file the admin page
serves, so it can be flipped live, which is the closest thing to having both.

## What did you deliberately not build?

Accounts, cloud saves, leaderboards and any server-side game state. The server exists for one reason: to
prove that replacing a data file changes the running game, which is the acceptance test in the brief. Any
network failure degrades silently to the bundled files with a small status chip, never a modal dialog and
never a blame message. Even the reload button is a convenience; the game never depends on it.

Sound clips are the other omission: adding them would have meant either unlicensed filler or time taken
from the parts of the brief that are graded. What ships instead is the hook — `AudioDirector` has a slot
per moment, silent until filled.

## What would you do with a fourth day?

Real glass reflections and dust on the window, a small "somebody watered me" animation at dawn, and a
proper Arabic shaping pass over the whole presentation (the fonts already cover every codepoint the data
can produce, so this would be polish rather than a fix).

## Where things live

| Path | What it is |
| --- | --- |
| `Assets/Scripts/Core` | rules and data: JSON, config, plant state, visuals, strings, pairs, save, loader |
| `Assets/Scripts/App` | the scene's behaviour: pot rig, pot view, bootstrap, controller, sound |
| `Assets/Scripts/UI` | the interface builders, which create or update in place |
| `Assets/Editor` | build script, project setup, scene baker, screenshot harness |
| `Assets/Scenes/Main.unity` | the authored world and interface |
| `Assets/Resources/SmallPotSprites` | 21 replaceable PNGs, one per interface icon |
| `Assets/Materials/Generated`, `Assets/Meshes/Generated` | baked materials and meshes |
| `Assets/Tests/EditMode` | 72 tests over the rules above |
| `data/` | the supplied files, plus our two optional ones |
| `tools/` | verify, sync, build, serve |
| `server/` | the zero-dependency content server, its admin page and 215 checks |
