Small Pot - content server
==========================

What this is
------------
The small HTTP server the game's content comes from, and the page it hosts for editing that content. It
serves the JSON files the game reads - config.json, pairs.json, strings.json, strings-base.json - and it
can tell every app that is already open that they changed, so a live demo can show the game changing
without rebuilding it.

Running it
----------
Double-click start-server.cmd and leave the window open. It prints two addresses: one for the editor page
on this machine, and one for a phone on the same Wi-Fi.

On the phone, open the app. If it cannot find a server it says so and offers a field: type the address
this window printed, press "Try again", and it will remember it.

If a phone on the same Wi-Fi still cannot connect, the firewall is almost always the reason. Run this once
in an administrator PowerShell (start-server.cmd adds the rule itself when it is run as administrator):

  New-NetFirewallRule -DisplayName 'Small Pot content server' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8787 -Profile Private

Over a USB cable instead: with the phone attached, run "adb reverse tcp:8787 tcp:8787", then type
http://127.0.0.1:8787 into the app.

What is in here
---------------
  server.js       the server. Plain Node.js, no packages to install.
  public/         the editor page, served at http://127.0.0.1:8787/
  default-data/   the pristine content: the files above plus strings-extra.json (the words for the screens
                  this project added) and dev.json (the settings). Never written to.
  content/        the working copy that is actually served and edited. Seeded from default-data/, and it is
                  what the editor page changes.
  test.js         the server's own test suite. "node test.js" runs it and prints every check.
  start-server.cmd / start-server.ps1   the launcher, which finds this machine's address and starts it.
  node.exe        a copy of Node.js, when the packager included one. If it is not here, install the LTS
                  build from https://nodejs.org - nothing else is needed. MIT licensed, from nodejs.org.
  SmallPot.apk    the game, when the packager included one. Copy it to an Android phone and install it.

Replacing the content
---------------------
Put the replacement file in default-data/ and delete the same-named file from content/, or press "Reset" in
the editor. content/ is only ever seeded from default-data/ when a file is missing, so no edit is ever
overwritten by accident.

Editing the words and numbers
-----------------------------
Open http://127.0.0.1:8787/ in a browser on this machine. Change a value, press "Push to clients", and every
app that is open is told as the button is pressed - not on a timer - and offers the change with a button of
its own, so nothing is applied behind the player's back.
