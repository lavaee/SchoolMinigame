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

Pushing is deliberately separate from saving. A designer edits several fields, looks at the preview, and then
decides to push. That bumps a revision number, and the push itself reaches every running client inside a round
trip, because the app is not asking on a timer — it is waiting on an answer. Saving therefore costs a running
player nothing at all, and pushing costs them a card they can take whenever they like.

The admin page mirrors the shape rules in `Assets/Scripts/Core/PlantVisuals.cs` to draw its silhouettes.
That is a deliberate duplication, and it is the one place in this project where two sources of truth
exist: a preview drawn by calling into Unity is not possible from a browser, and a designer editing a
sixth stage needs to see what six stages look like. The mirror is pinned by tests on both sides — the shape
numbers, the default-mornings fallback and the thirst posture are asserted for the same values.

## Why can the server run without the repository?

Because the folder that gets handed over is not the folder it is developed in, and a server that reads `data/`
one level above itself only works while it is sitting inside the checkout. The first time that folder left
this machine it would find no `data/` there and serve nothing at all.

So a packaged copy carries its own pristine files in `server/default-data/`, seeded into `content/` the same
way, and `server.js` prefers that folder whenever it exists. Inside the repository it does not exist and
`data/` is still the source — which matters, because that is where the brief puts the files, where the editor
build reads them from, and where the byte-for-byte test points. One server file, two layouts, decided by what
is on disk rather than by a flag somebody has to remember to pass.

The copy is the one place in this project where the supplied files exist twice, so it is checked rather than
trusted: `tools/package-server.ps1` compares every packaged file against `data/` and fails on any difference.
It then moves the whole package to a temporary directory with nothing above it, starts it on a spare port and
asks it where its content came from. A package answering with a path inside the repository would be a package
that breaks on the machine it was built for, so that is an error and not a warning.

`test.js` resolves its content through the same function the server uses, so the suite travels with the
package and passes there. Whoever receives it can run `node test.js` and watch the protocol's 228 checks go
by on their own machine, with neither Unity nor this repository anywhere near it.

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

## Why does the app wait on an answer instead of asking every few seconds?

Because ten seconds is a long time to look at a screen and wonder whether the button worked. The app used to
ask the server "which revision are you serving?" on a timer, so a push was felt anywhere between instantly and
ten seconds later — five on average, and never at the moment you were showing somebody.

It asks that question once now and leaves it open. The server parks the request and answers it the instant a
push happens, so the news travels one round trip: measured on the phone, nine milliseconds after the push
began. The design pays for that with one obligation — the request has to be able to come back empty. The
server holds it for a while (`updateWatchHoldSeconds`, twenty seconds by default) and then answers "still the
same revision", so an idle app re-issues rather than trusting a connection that a network may have dropped in
the meantime. Anything that fails to answer means "nothing is waiting", exactly as a failed poll did, and the
separate connection watch is still the thing that decides the server has gone.

This is not a WebSocket. A socket is the right tool for a channel that both ends talk on; this is a one-way
announcement to a couple of phones on one desk. A held request gets there with no handshake, no framing, no
dependency, and over the same HTTP path the content already travels — including `adb reverse` and the local
network, both of which are already proven on the device. If something ever needs to travel the other way, the
seam is one function.

The offer is made even mid-round now, rather than waiting for a quiet moment. That is safe because of two
things that were already true: the card stops the clock rather than the round, and applying content defers
the interface rebuild until the round set closes. A round cannot be stranded by an update the player chose to
take, so there is no reason to make them wait for one to be offered.

## Why does a push wait for a button?

Because a push is somebody else's action, and it used to be applied to whatever the player was in the middle
of. The version watch noticed a new revision, reloaded the content, rebuilt the interface and flashed a chip
for three seconds: a round could vanish mid-answer, and the only record of why was one word in the corner.
That is tolerable when the person pushing and the person playing are the same person, which is the one case a
demo is not.

The watch offers now instead of applying. A card names the revision that is waiting and says the game has
stopped where it is; the round clock stops with the card — the same pause a dropped connection uses, for the
same reason, that nothing shared may run while somebody is reading — and one button applies it. The update is
still not a secret while the card is up: the status chip says one is waiting, so the corner answers the
question before the card does. If the interface is rebuilt for any other reason while a revision is pending,
the card comes back: a rebuild is not an answer, and content that quietly stopped being offered would be
content the player never got.

The card is built the way every other panel is, find-first/create-second, which is why it works on a device
today with no re-bake and will be editable in the scene after the next one.

## Why does the status chip remember what it was asked to say?

RTLTMPro shapes a string as it is assigned, and shaping is not idempotent: hand it text it has already
shaped and the letters come back as isolated presentation forms. The chip's flash used to save the label's
own text, replace it for a few seconds, and put it back — so every flash ended in glyph soup, which is what
`ﺏﺯ ﺵﺱﻰﺭ ۶` was, and what the player saw instead of "از سرور ۶".

It keeps the string it was given now and restores that. The rule generalises past this one label: never read
a rendered string back out of an RTL label and assign it somewhere again, because the second shaping is not
the same string as the first.

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

## Why is a sound found by its file name instead of being wired up?

Because the alternative makes adding a sound a code change. `AudioLibrary` maps each moment — a click, a
correct answer, a level up — to a file name, and the file is loaded from `Assets/Resources/Audio/FX` under
that name. Dropping `No Water.mp3` into the folder is the whole installation, which is what the person
supplying the audio actually wants to do. A missing file is silence rather than an error, so the game is
playable with no audio at all and stays playable when one is removed. A clip assigned to a slot on
`AudioDirector` still wins, so the scene keeps the last word — the same defer-to-the-author rule as the
interface.

Two details are deliberate rather than incidental. The four **"Select flowerpot" takes** are chosen at
random on every answer press and never repeat the one just used, because one file on every press is audibly
a machine. And the effects play through a **pool of sources** rather than one, because `PlayOneShot` reads
the source's pitch at the moment it is called: with a single source, every new sound retunes whatever is
still ringing. The levels themselves are in the `audio` block of `data/dev.json`, for the same reason as
everything else tunable — they are worth adjusting while listening, not by rebuilding.

## How do you change how the interface moves?

Through the `juice` block of `data/dev.json`: how long a panel takes to arrive and how small it starts, how
far a button squashes under a finger, how far it overshoots on release, the strength of the punch and the
jelly, and the timing of the text reveal. These are numbers you can only judge by watching, and the file is
read at runtime and can be pushed into a running build from the admin page, so a change is felt on the next
tap rather than at the next launch. The defaults live in `Assets/Scripts/UI/Juice.cs`, and an absent key
keeps its default.

**Animation curves are deliberately not in there.** A curve is a shape you draw, and drawing a shape in
JSON is misery. The day/night cycle keeps its curves as Inspector fields, where they can be dragged.

## Why does the sun move when you go to the next morning?

Because the morning is the game's whole rhythm — nothing grows except at one — and a transition that
happens instantly is easy to miss. A sunrise gives it a beginning and an end, and it is the one moment
allowed to take its time.

`DurationSeconds`, `Degrees`, `StartIntensity` and `PeakIntensity` come from the `dayNight` block of
`data/dev.json`. The two `AnimationCurve`s — how the intensity rises, how the rotation progresses — are
Inspector fields, because that is where a curve belongs and because the component is meant to be dropped on
`Sun Light` and shaped by hand. It prefers a component it finds there and only creates one at runtime when
there is none, so hand-shaped curves are never overridden by the code's defaults. It finds the sun through
`RenderSettings.sun`, then a directional light named "sun", then any directional light, and logs which one
it used.

Three smaller decisions: it runs on **unscaled** time, so it still plays if something has paused the game;
it **honours reduce-motion**, arriving at its final state immediately rather than not moving at all, which
is this project's rule for every animation; and calling it twice is safe, because the second call kills the
first without completing it and leaves the light where it got to rather than snapping it back.

## Why does the clock stop after a question?

Because the answer arriving and the next question arriving were the same instant, and the player never got
to see what they had done. `feedbackPauseSeconds` in `data/dev.json` — one second by default — holds the
result on screen with the shared clock stopped. The clock is checked where it already stops for the
connection gate, and the pause is cleared even when a round set is cancelled or times out, so a paused
session cannot get stuck.

It is in `dev.json` and not in the supplied `config.json`, which already holds feedback timings, because
that file is hash-pinned by a test — a new setting there would mean editing the challenge's own file. The
supplied timings are kept and the pause is added on top, so none of the original pacing was taken away.

## Why is the font loaded from Resources when the scene already names one?

Because the scene names a font, and that reference is the problem: pointing it at Lalezar means editing
`Main.unity`, which is hand-authored now and not something a script should be rewriting. So the generated
Lalezar asset lives under `Assets/Resources/Fonts/` and `GameBootstrap` loads it by name, logging one line
saying which font won. Lalezar is used when the scene's reference is empty or still points at the old
Persian font; a font assigned on purpose is left alone.

The sweep over scene-authored labels is needed because the widget builders skip font assignment on anything
already in the scene, so without it every hand-laid-out label would keep drawing in the old face. It sets
`.font` and never `.text`: assigning text re-runs Persian shaping, which is exactly the mistake that
produced mojibake before.

Two consequences are worth stating plainly. The Lalezar atlas is built by
`Window/SmallPot/Regenerate Font Assets Only`, never by `Generate Project`, which would re-bake the scene
along with it. And **Lalezar lacks 36 of the isolated presentation forms** — the ones a standalone
single-letter word needs. Connected text only ever uses the initial, medial and final forms, so ordinary
words are unaffected. Two characters in the supplied data are affected and fall back to Amiri, which is
kept as the glyph fallback precisely because it has the fullest coverage of the three fonts in the project.
The mismatch is visible on those two characters, and it cannot be fixed from this side of the font.

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
| `Assets/Tests/EditMode` | 84 tests over the rules above |
| `data/` | the supplied files, plus our two optional ones |
| `tools/` | verify, sync, build, serve, package |
| `server/` | the zero-dependency content server, its admin page and 228 checks |
| `server/package/` | the launcher and the notes copied into a handed-over server |
