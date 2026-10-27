CAPRI - DESKTOP COMPANION

GETTING STARTED
  Double-click Capri.exe. Nothing to install (Windows 10 or 11).
  If Windows SmartScreen warns the first time: More info > Run anyway.
  She lives in the tray (next to the clock), not on the taskbar.
  To have her start with Windows: right-click her > Settings... > General > Start with Windows.

FINDING HER
  Double-click the tray icon     hide / show her
  Right-click the tray icon      call her over, pause, busy, mute, stay here, settings, quit
  Ctrl+Alt+C (anywhere)          call her to your pointer; press again next to her to hide her
                                 (pick another key in Settings > General > Hotkey keys)

PLAYING WITH HER
  Click             she startles ("caught!")
  Double-click      she dances
  Drag              carry her; drop her on a window's title bar and she sits on it
  Flick and let go  throw her; she bounces and lands
  Shake her         while dragging: she gets angry
  Ctrl-click        head pat          Shift-click   cartoon swat
  Scroll the wheel  over her: tickles    Click her nonstop: she gets annoyed
  Drop a file       a pretend "threat scan" of its name (the file is never opened or touched)
  Right-click       her menu

THE MENU
  Chat with her...      talk to her; reminders and games always work there, real replies
                        need your own Claude API key (see PRIVACY)
  Reminders...          "20" = in 20 minutes; also 1h30, 17:45, 5pm, tomorrow 9:00,
                        every day at 9, weekdays at 8:30, every monday at 18:00
  Leave a sticky note   notes stay after a restart; PIN keeps one on top
  Focus timer           she waits in her corner with a countdown, then a short break
  Stay here             she keeps moving and talking in place; drag or throw her to move the spot
  Let clicks through    clicks go to whatever is under her; undo from the tray or with the hotkey
  Busy / Pause / Mute / Hide
  Play                  catch me, toy ball, invite a friend (a second Capri), pet, swat
  Tricks                sit on a window, perch on the taskbar, peek from behind a window,
                        her own cursor, cursor glitch trail, possess a window, and more
  Animations            pick any animation or little routine yourself
  Settings...           every option, in one window
  Debug and help        status, our history, self-test, updates, and
                        "Flip the drawing she's showing now" if she faces the wrong way

WHAT SHE DOES ON HER OWN
  Wanders, rests, perches, chases your cursor, sits on windows and peeks from behind them, does
  tricks every few minutes, comments on things and says voice lines. She stays off the middle of
  the window you're working in. Settings > Behaviour changes how often each kind of thing happens.
  She sleeps when you're away and greets you when you're back, hides during fullscreen games,
  and goes quiet in a corner while Windows Do Not Disturb or Focus is on (drag her out, or untick
  Busy, to bring her back). Quiet hours (Settings > General) stop the yapping at night.
  Optional: music reactions, weather for your city, battery reactions, noticing which app you use.

PRIVACY
  No telemetry. Nothing leaves this PC unless you turn on:
    - weather: your city name goes to open-meteo.com
    - chat replies: your messages go to Anthropic, using your own API key from
      console.anthropic.com (stored encrypted for your Windows account in chat-key.dat)
  The daily update check only asks GitHub for the latest version number.

UPDATES
  She checks for a new version once a day. Right-click > Debug and help > Install update keeps
  your settings, memory, notes, reminders, voices and tuning.json.

IF SOMETHING'S WRONG
  Right-click > Debug and help > Movement status    what she's doing and why (busy, paused...)
  Right-click > Debug and help > Run self-test      checks everything and writes
                                                    logs\selftest-report.txt; send that report.
  Problems also show in her speech bubble, a tray notification and the logs folder.

HER FOLDER
  Capri.exe, README.txt   the app and this guide
  save\     your settings, memory, reminders, notes and chat (kept when updating)
  data\     her data. tuning.json (feel, the hotkey) is yours to edit, then
            Debug and help > Reload tuning.json. Also editable: remarks.json (her lines),
            behaviours.json (animations), games.json (trivia), app-reactions.json, unlocks.json.
  logs\     error logs and the self-test report (safe to delete)
  sprites\  sounds\  voices\   her drawings, sound effects and voice clips
