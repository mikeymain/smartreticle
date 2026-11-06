# SmartReticle

A crosshair overlay for Windows that shows only when you need it. Preset for **The Division 2** (`TheDivision2.exe`).

## When it shows

The crosshair appears only when all of these are true:

1. The game is the focused window. It hides when you alt-tab, minimise, or click another monitor.
2. The mouse cursor is not visible. It hides in menus that use the Windows cursor.
3. Optional, off by default: the hidden cursor is not roaming freely. This catches menus where the game draws its own cursor.
4. The aim rule allows it. Options: `Always`, `HideWhileAiming`, `OnlyWhileAiming`. The aim key is `RButton` by default.

It hides instantly and shows again after 120 ms of stable gameplay, so menus don't flash it. `F8` toggles it on or off at any time. The key still reaches the game.

## Use

1. Set Division 2 to **Windowed** at your monitor's native resolution. Division 2 has no borderless option, so **Force borderless** (on by default) does it instead: it removes the title bar and stretches the window over the monitor. No overlay can draw over exclusive fullscreen. If the log says `Borderless: FAILED`, the game is running as administrator; run SmartReticle as administrator too.
2. Run `bin\SmartReticle.exe`. Settings open, and a tray icon appears.
3. Tune the look on the left. Changes save to `%APPDATA%\SmartReticle\config.ini` immediately.
4. Play. If it shows or hides at the wrong moment, check the **Live detection** panel in Settings.
5. If needed, tick **Write debug log** and open the log. Each state change is logged with its reason.
6. If the crosshair still shows in the map or inventory, tick **Hide when game-drawn cursor moves**. Watch the live panel while you play: during mouse-look, `from ctr` must stay small (under 24 px) or `clip` must be tiny. Otherwise this option also hides the crosshair during gameplay, so leave it off and use `F8`.

To share a crosshair, use **Copy share code** and **Paste share code**. Codes start with `SR1-`.

## Safety

SmartReticle is an external, click-through window. It never injects into or reads the game's memory. It only queries the foreground window, the cursor state, and key state through public Windows APIs. Check the game's rules anyway; some competitive titles forbid any crosshair overlay.

## Build

```
powershell -ExecutionPolicy Bypass -File build.ps1 -Tests
bin\ReticleProbe.exe unit
```

The build uses the C# compiler that ships with Windows (.NET Framework 4.x), so nothing needs installing. `CursorLab.exe game|menu|softmenu` simulates each cursor state. Run `ReticleProbe.exe watch 3 CursorLab` alongside it to see the detector's decisions.
