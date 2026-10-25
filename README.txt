CAPRI COMPANION - GUIDE

START
  Capri.vbs            starts her with no console window (use this normally)
  Start-Capri.bat      starts her with a console window, so startup errors stay visible
  Run-SelfTest.bat     checks everything on this PC and writes selftest-report.txt
  Capri.exe            only after you build it (see BUILDING THE EXE)
Only one Capri runs at a time. Settings > Start with Windows adds her to startup.

FINDING HER
She has a tray icon (next to the clock), not a taskbar button.
  Double-click the tray icon      hide / show her
  Right-click the tray icon       show/hide, call her over, pause, busy, mute, full menu, quit
  Ctrl+Alt+C (anywhere)           call her to the pointer; press again next to her to hide her
                                  (change the key in tuning.json; turn off in Settings > Behaviour)

CONTROLS
  Click: reaction.  Double-click: dance.  Drag: carry her.
  Flick and let go: throw her (she bounces and lands; turn off in Settings > Behaviour).
  Shake while dragging: she gets angry.  Shift-click: cartoon swat.  Ctrl-click: head pat (with a little sound).
  Drop a file on her: a mock "threat scan" of its name. She never opens, moves or changes it.
  Right-click: the menu.  Escape or Quit: short dissolve and exit.

MENU
  Pause / Busy (quiet corner) / Mute / Hide
  Interact      do something, pet, swat, say a line, cursor attack, sit on the active window,
                perch at taskbar, fake sabotage
  Animations    every pose and antic by group, little routines, stop routine
  Settings
    Voice and subtitles   language, volume, random yapping, subtitles, remarks, voice library
    Appearance            size, smooth pose changes, idle blinking, Blackwall effects, swat sound
    Behaviour             cursor chasing, pointer knockback, sit on windows, throwing,
                          time-of-day moods, react to music, notice which app I'm using,
                          summon hotkey, sleep after inactivity, break reminders,
                          fullscreen apps, activity frequency
    Start with Windows
  Debug and help  movement status, our history, run self-test, reload tuning.json,
                  wander now, cursor test, this guide, folders
  Mood and rivalry score (read only), Quit

WHAT SHE DOES ON HER OWN
  - A shuffled rotation of antics, routines, perching and wandering (all defined in behaviours.json).
  - Multiple monitors: she can be dragged anywhere and sometimes flies to another screen.
  - Sits on the title bar of the window you're using, rides along when you move it,
    and falls off when it closes or maximizes.
  - Hides on her monitor while a fullscreen app is in front (Settings > Behaviour to change).
  - Falls asleep when you leave the PC and greets you when you come back. She also naps
    if you ignore her (Sleep after inactivity), and wakes up grumpy when poked.
  - Time of day: drowsier after midnight, an afternoon drink break, optional break reminders.
  - Music: bobs along to whatever Windows says is playing (Spotify, browsers, media players),
    sometimes comments on a new song, and dances more. Needs Windows 10 1809 or newer.
  - Memory: remembers when you met, days together, pats, throws, swats and scans
    (memory.json). Debug and help > Our history shows it.
  - App awareness (OFF until you turn it on): reacts to games, video and coding, and keeps
    quiet while you code. It reads only the name and title of the window in front; nothing
    is stored or sent anywhere. The app lists are in app-reactions.json.

NEWER FEATURES
  Settings window   right-click > Settings window... (or the tray menu): every option in tabs.
  Eyes              her eyes follow your cursor in her hover, beckon and wave poses.
  Talking mouth     while she speaks, her squiggle mouth switches to her open grin with the words.
  Music             headphones on while something plays; she nods on the real beat of the song,
                    whatever she's doing (hovering, dancing, gaming, sipping, waving)
                    (she listens to your PC's sound in memory only; nothing is recorded or saved).
                    Turn that off in Settings > Music and apps to use the fixed musicBpm tempo.
  Shadow            a soft shadow under her when she sits on a window or the bottom of the screen.
  Battery           on laptops she reacts to unplugging, plugging in, low and nearly empty battery.
  Catch me          Interact > Play catch me: click her as often as you can in 30 seconds.
  Unlocks           days together, pats, throws, cursor catches, welcome-backs, scans and game scores
                    unlock new routines and new lines (unlocks.json). Debug and help > Our history
                    shows what's unlocked and hints for the rest.
  Mixed-scale monitors (experimental): set "perMonitorDpi": true in tuning.json and restart for
                    sharp rendering on monitors with different scale settings.

FILES YOU CAN EDIT
  behaviours.json      every state: pose, timing, movement, what comes next, routines
  remarks.json         her text lines, by situation ({0} is filled in with a number or name)
  tuning.json          feel: throw speed, gravity, bounces, perch chance and height,
                       crossfade, music bob, how often she comments, the hotkey.
                       After editing: Debug and help > Reload tuning.json.
  app-reactions.json   which programs count as games, code, video, browsers
  unlocks.json         what can be unlocked and what it takes
Saved automatically: voice-settings.json (preferences), memory.json (history).
sprite-cache\ is rebuilt from the sprite sheets when they change; safe to delete.

SELF-TEST
Run-SelfTest.bat (or Debug and help > Run self-test) starts a second, silent Capri that
runs every state, checks every data file, voice clip, the volume, menus, monitors, the
hotkey, music and app detection, then shows a summary and writes selftest-report.txt.
It never changes your settings or memory. Send that report when something is wrong.

BUILDING THE EXE
  powershell -ExecutionPolicy Bypass -File tools\Build-Exe.ps1
Installs the ps2exe module for your user (once), makes capri.ico from her hover pose
(or uses your own capri.ico), and writes Capri.exe here. The exe still needs this folder
around it (lib\, images, voices, json files). Windows SmartScreen may warn the first time
because the exe is not signed.

SPRITES
Capri draws from sprites\frame-00..32.png. They are made from the sprite sheets by
tools\normalize_sprites.py, which separates each figure on the full sheet (so hair that crossed
into the next cell is no longer cut off), scales all sheets to the same size, and lines every
frame up on her visor so she no longer jumps or changes size between poses.
sprites\frames.json records where her feet are in each frame, which is how she sits on windows.
After editing a sheet, rebuild the frames (needs Python):
  pip install pillow numpy scipy scikit-image
  python tools\normalize_sprites.py
  python tools\make_music_frames.py     (headphones)
  python tools\make_mouth_frames.py     (talking mouth)
  python tools\make_eye_frames.py       (eyes)
Without the sprites folder she falls back to cutting the sheets as before.

LOGS (in this folder, only when something goes wrong)
  startup-error.log  runtime-error.log  audio-error.log  effects-error.log  cursor-error.log
Problems also appear in her speech bubble and as a tray notification.
There is no telemetry and nothing is uploaded.

CODE LAYOUT
  Capri.ps1          starts everything (loads lib\ in order)
  lib\native.cs      Windows APIs: cursor, monitors, windows, fullscreen, idle time, volume, audio
  lib\effects.cs     fast effect drawing and the global hotkey (needs WPF; falls back if not)
  lib\window.ps1     window, effect layers, sprites, pose crossfade
  lib\data.ps1       state variables, behaviours.json, tuning.json
  lib\screens.ps1    monitors, work areas, window perching
  lib\speech.ps1     bubble, remarks, settings, voice playback
  lib\memory.ps1     memory.json
  lib\states.ps1     state machine, paths, animations, throws, hiding, modes
  lib\awareness.ps1  music, beat sync, battery, app awareness, file drops, hotkey
  lib\play.ps1       catch-me game and unlocks
  lib\settings.ps1   the settings window
  lib\menu.ps1       voice library, menu, tray icon
  lib\input.ps1      mouse
  lib\loop.ps1       the per-frame loop
  lib\selftest.ps1   the self-test
