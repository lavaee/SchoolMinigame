# Demo script

## Before the session

1. `powershell -File tools\verify-data.ps1` — confirm the supplied files are untouched.
2. `powershell -File tools\build-apk.ps1` — bakes this machine's network address and `127.0.0.1`, so the
   same APK works over Wi-Fi or over a cable.
3. `adb install -r Builds\SmallPot.apk`, then `powershell -File tools\serve-data.ps1`. That prints the
   addresses the phone can use, starts the server, and runs `adb reverse` when a device is attached. Over
   Wi-Fi, allow the port when Windows asks; the script prints the one-line rule if it did not.
4. Open the admin page at <http://127.0.0.1:8787/> and the game on the phone. Open the settings panel in
   the game once to confirm the file sources read "server", and leave the admin page on `config.json`.

Confirm the whole loop before you present: change a word in the admin page, press **Push to clients**, and
watch the phone pick it up on its own within about ten seconds. Do it once so the moment is not the first
time.

If the network misbehaves, nothing is lost: the game already runs from the bundled files. The live change
can still be shown with `adb push` into
`/sdcard/Android/data/com.smallpot.game/files/data/` plus the reload button in the settings panel, or by
editing `data/` and rebuilding — the fallback order makes the demo robust rather than fragile.

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

All three happen in the admin page at <http://127.0.0.1:8787/>, and all three end the same way: press
**Push to clients** and let go of the mouse. The phone is not touched — it notices the new revision within
ten seconds, reloads, and says so in a quiet chip. That is the point of the exercise: content reaches a
running build without anyone rebuilding or reinstalling anything.

**1. Change a word.** In the admin page pick `strings.json`, edit `report.title` to `صبح شد`, push. Go to
the next morning on the phone and the report card says it. This is the one to use if the room is quiet: it
is unmistakable, and it is one field.

**2. Change a number.** Pick `config.json`, set `watering.costDrops` to `2`, push. The water button on the
phone now asks for two drops. The economy panel on the right already shows what the change does before you
push it — one session earns at most seven, so watering twice became possible.

**3. Add a sixth state.** In `config.json` add a stage to the list, name it `fruit`, give it three
mornings, then add `pot.state.fruit` in the strings table and push. Water through to bloom on the phone and
the pot grows again into a stage the code has never heard of. Two things make this the strongest of the
three: the **silhouette preview** redraws a sixth plant the moment you add the stage, so you can check the
five — now six — states still look different before showing anyone; and the runtime resolves an unknown
stage parametrically from its position in the list, so nothing in the scene or the code needs a prefab.

Keep the original values in a scratch buffer, or just press **Reset to supplied** afterwards — the admin
page restores the file byte for byte from the supplied copy.

## What to show if the phone misbehaves

Open `Assets/Scenes/Main.unity` and point at the `World/Pot` object: your own model goes in the `PotRig`
slots, your own icons go in `Assets/Resources/SmallPotSprites/`, your own clips go in `AudioDirector` on
`App` — all in the inspector, none of it in code. The game itself is the same build either way.

## Questions to be ready for

* Why growth waits for the morning, and why thirst never takes growth back → `docs/DESIGN-DECISIONS.md`.
* Why `thirsty` is not in `growth.stages` (it is an overlay, not a stage).
* What happens when a data file is missing, truncated or has the wrong type (`[key]` markers and defaults,
  and `strings-base.json` alone is playable).
* How the five states stay readable without text (silhouette, posture, soil, badges; asserted by a test).
* Why the world is authored in the scene now instead of built at launch, and what that costs: a new visual
  stage is data, but a new *part* is an inspector job. The generator stays behind a menu item so the
  stand-in can be regenerated.
* Why the server keeps its own working copy instead of writing to `data/`: the supplied files stay
  byte-identical and the checksum test keeps meaning something, while the admin page can edit freely.
* Why generated art has to be baked into assets at all (a material made in code carries `HideAndDontSave`,
  so a scene that referenced it would come back white).
* What was deliberately left out: accounts, cloud save, leaderboards, server-side state, sound *clips*.
