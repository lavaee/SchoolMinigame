# Demo script

## Before the session

1. `powershell -File tools\verify-data.ps1` — confirm the supplied files are untouched.
2. `powershell -File tools\build-apk.ps1 -DataServerUrl http://127.0.0.1:8787`
3. `adb install -r Builds\SmallPot.apk`
4. `powershell -File tools\serve-data.ps1` — serves `data/` and runs `adb reverse` when a phone is attached.
5. Leave `data\strings.json` and `data\config.json` open in an editor, and open the settings panel in the
   game once to check the file sources read "server".

If the network misbehaves, nothing is lost: the game already runs from the bundled files. The live change
can also be shown with `adb push data\config.json /sdcard/Android/data/com.smallpot.game/files/data/`
followed by the reload button, or by rebuilding — the fallback order makes the demo robust rather than
fragile.

## The one-minute phone video

Recorded on the phone, in portrait, one continuous take where possible.

| Seconds | On screen | Said |
| --- | --- | --- |
| 0–6 | The window and the pot at the seed stage, with the drop counter reading ۳ | "This is the pot. It has three drops and it is still a seed." |
| 6–10 | Press **آب دادن** — refused, because four drops are needed | "Watering costs four, so first I have to earn one." |
| 10–32 | Press **جمع کردن قطره**, play three or four rounds of **جفتها**; one answer wrong on purpose, the kind correction appears | "Five words, one clock. A wrong answer shows the right word and takes nothing away." |
| 32–38 | The reward moment: the number counts up | "That is the reward moment." |
| 38–44 | Return, press **آب دادن** — the pot is watered, the badge and the wet soil appear | "Only now is the night prepared. Nothing has grown yet." |
| 44–52 | Press **برو به صبح بعد**, the plant grows, the report card appears | "Growth happens at the next morning, and the card says what changed." |
| 52–60 | Settings → **کاهش حرکت** on, then another morning | "Reduce motion keeps every state readable without the animation." |

## The live change (choose one, it takes under a minute)

**1. Change a word.** Edit `data/strings.json`, change `report.title` to `صبح شد`, then press
**بارگذاری دوباره** in the settings panel and go to the next morning. No rebuild, no reinstall. This is
the one to use if the room is quiet: it is unmistakable and reversible.

**2. Change a number.** Edit `data/config.json`, set `watering.costDrops` to `2`, reload, and water the
pot for two drops instead of four. Good for showing that the economy is data, not code.

**3. Add a sixth state.** Append `"fruit"` to `growth.stages` and add `"bloomToFruit": 3` to
`growth.mornings` in `data/config.json`, then add `"pot.state.fruit": "میوه داده"` to `data/strings.json`.
Reload and water through to bloom: the pot grows again into a stage the code has never heard of. The
silhouette comes from the stage's position in the list, and the transition lookup is derived from the
stage ids, so no code changes at all. This is the strongest of the three because it shows the extension
point rather than a value.

Keep the original file contents in a scratch buffer so the change can be reverted on screen.

## Questions to be ready for

* Why growth waits for the morning, and why thirst never takes growth back → `docs/DESIGN-DECISIONS.md`.
* Why `thirsty` is not in `growth.stages` (it is an overlay, not a stage).
* What happens when a data file is missing, truncated or has the wrong type (`[key]` markers and defaults,
  and `strings-base.json` alone is playable).
* How the five states stay readable without text (silhouette, posture, soil, badges; asserted by a test).
* What was deliberately left out: accounts, cloud save, leaderboards, server-side state, sound.
