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

## What did you deliberately not build?

Accounts, cloud saves, leaderboards and any server-side game state. The server exists for one reason: to
prove that replacing a data file changes the running game, which is the acceptance test in the brief. Any
network failure degrades silently to the bundled files with a small status chip, never a modal dialog and
never a blame message. Even the reload button is a convenience; the game never depends on it.

Sound is the other omission: adding it would have meant either unlicensed filler or time taken from the
parts of the brief that are graded.

## What would you do with a fourth day?

Real glass reflections and dust on the window, a small "somebody watered me" animation at dawn, and a
proper Arabic shaping pass over the whole presentation (the fonts already cover every codepoint the data
can produce, so this would be polish rather than a fix).

## Where things live

| Path | What it is |
| --- | --- |
| `Assets/Scripts/Core` | rules and data: JSON, config, plant state, visuals, strings, pairs, save, loader |
| `Assets/Scripts/App` | the runtime scene: camera, lighting, pot rig, controller |
| `Assets/Scripts/UI` | canvas, HUD, minigame, settings, procedural sprites |
| `Assets/Editor` | build script, one-shot project setup, screenshot harness |
| `Assets/Tests/EditMode` | 72 tests over the rules above |
| `data/` | the supplied files, plus our two optional ones |
| `tools/` | verify, sync, build, serve |
| `server/` | the zero-dependency content server |
