const { app, BrowserWindow, ipcMain, screen, Tray, Menu, nativeImage, clipboard, shell } = require('electron');
const path = require('node:path');
const fs = require('node:fs');
const store = require('./engine/settings.cjs');
const { VisibilityGate, parseCombo, ComboWatcher } = require('./engine/logic.cjs');
const { createLog } = require('./engine/log.cjs');

// The overlay must keep painting while the game, not us, has focus.
app.commandLine.appendSwitch('disable-features', 'CalculateNativeWinOcclusion');
app.commandLine.appendSwitch('disable-renderer-backgrounding');
app.commandLine.appendSwitch('disable-backgrounding-occluded-windows');

const TICK_MS = 16;
const STATUS_MS = 200;
const OVERLAY_DIP = 400;        // overlay window edge; the crosshair is drawn inside it at the exact device pixel
const TOPMOST_EVERY_MS = 1000;  // borderless games can be topmost too

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  // Win32 is loaded only in the primary instance.
  const win = require('./engine/win32.cjs');
  const { Detector, describe } = require('./engine/detector.cjs');
  const { BorderlessForcer } = require('./engine/borderless.cjs');

  const settingsFile = () => path.join(app.getPath('userData'), 'settings.json');
  const logFile = () => path.join(app.getPath('userData'), 'debug.log');

  let settings;
  let log;
  let mainWindow = null;
  let overlay = null;
  let overlayHwnd = 0;
  let tray = null;
  let quitting = false;
  let saveTimer = null;
  let toggleKey = null;
  let aimCodes = null;
  let lastExternal = null;
  let lastLogged = '';
  let overlayState = { visible: false, key: '' };
  let lastTopmost = 0;

  const detector = new Detector(process.pid);
  const gate = new VisibilityGate();
  let borderless;

  const alive = (w) => Boolean(w && !w.isDestroyed());

  // ---- windows ----------------------------------------------------------

  function createMainWindow(show) {
    mainWindow = new BrowserWindow({
      width: 1120, height: 760, minWidth: 940, minHeight: 640, show: false,
      backgroundColor: '#0b0d0e', title: 'SmartReticle', icon: appIcon(64), autoHideMenuBar: true,
      webPreferences: { preload: path.join(__dirname, 'preload.cjs'), contextIsolation: true, nodeIntegration: false }
    });
    mainWindow.setMenu(null);
    mainWindow.loadFile(path.join(__dirname, 'renderer', 'index.html'));
    if (show) mainWindow.once('ready-to-show', () => mainWindow.show());
    // Closing keeps the overlay running from the tray; Quit is in the tray menu.
    mainWindow.on('close', (e) => { if (!quitting) { e.preventDefault(); mainWindow.hide(); } });
  }

  function showMainWindow() {
    if (!alive(mainWindow)) createMainWindow(true);
    else { mainWindow.show(); if (mainWindow.isMinimized()) mainWindow.restore(); mainWindow.focus(); }
  }

  function createOverlay() {
    overlay = new BrowserWindow({
      width: OVERLAY_DIP, height: OVERLAY_DIP, x: 0, y: 0,
      frame: false, transparent: true, resizable: false, movable: false, hasShadow: false,
      skipTaskbar: true, alwaysOnTop: true, focusable: false, show: false,
      webPreferences: {
        preload: path.join(__dirname, 'overlay-preload.cjs'), contextIsolation: true, nodeIntegration: false,
        backgroundThrottling: false
      }
    });
    overlay.setAlwaysOnTop(true, 'screen-saver');
    overlay.setIgnoreMouseEvents(true);
    overlay.loadFile(path.join(__dirname, 'renderer', 'overlay.html'));
    overlay.webContents.once('did-finish-load', () => { sendCrosshair(); overlay.showInactive(); hardenOverlay(); });
  }

  // If the overlay could take focus, the detector would read that as "tabbed out". Make sure it never can.
  function hardenOverlay() {
    const handle = overlay.getNativeWindowHandle();
    overlayHwnd = Number(handle.length >= 8 ? handle.readBigUInt64LE(0) : handle.readUInt32LE(0));
    const want = win.WS_EX.NOACTIVATE | win.WS_EX.TRANSPARENT | win.WS_EX.TOOLWINDOW | win.WS_EX.LAYERED;
    const ex = win.getExStyle(overlayHwnd);
    if ((ex & want) !== want) {
      win.setExStyle(overlayHwnd, (ex | want) >>> 0);
      log(`Overlay ex-style fixed: ${ex.toString(16)} -> ${win.getExStyle(overlayHwnd).toString(16)}`);
    }
  }

  function sendCrosshair() {
    if (!alive(overlay)) return;
    overlay.webContents.send('overlay:crosshair', store.activeCrosshair(settings));
    overlayState.key = ''; // force a re-place with the new drawing
  }

  // Win32 gives physical pixels; Electron places windows in DIP. Place the window near the aim point, then tell the
  // page exactly which device pixel inside it is the aim point, so rounding never shifts the crosshair.
  function placeOverlay(center, visible) {
    if (!alive(overlay)) return;
    const c = store.activeCrosshair(settings);
    const aim = center ? { x: center.x + c.offsetX, y: center.y + c.offsetY } : null;
    const key = visible && aim ? `${aim.x},${aim.y}` : 'hidden';
    if (key === overlayState.key && visible === overlayState.visible) return keepOnTop(visible);
    if (visible && aim) {
      const dip = screen.screenToDipPoint(aim);
      const bounds = { x: Math.round(dip.x - OVERLAY_DIP / 2), y: Math.round(dip.y - OVERLAY_DIP / 2), width: OVERLAY_DIP, height: OVERLAY_DIP };
      overlay.setBounds(bounds);
      const origin = screen.dipToScreenPoint({ x: bounds.x, y: bounds.y });
      overlay.webContents.send('overlay:place', { x: aim.x - origin.x, y: aim.y - origin.y });
    }
    if (visible !== overlayState.visible) overlay.webContents.send('overlay:visible', visible);
    overlayState = { visible, key };
    keepOnTop(visible);
  }

  function keepOnTop(visible) {
    if (!visible || !overlayHwnd || Date.now() - lastTopmost < TOPMOST_EVERY_MS) return;
    win.setPos(overlayHwnd, 0, 0, 0, 0, win.SWP.NOMOVE | win.SWP.NOSIZE | win.SWP.NOACTIVATE, -1);
    lastTopmost = Date.now();
  }

  // ---- tray -------------------------------------------------------------

  // A small lime reticle, drawn into a BGRA buffer so the app ships no image files.
  function appIcon(size) {
    const buf = Buffer.alloc(size * size * 4);
    const c = (size - 1) / 2, r = size * 0.34, w = Math.max(1.5, size / 16);
    for (let y = 0; y < size; y++) {
      for (let x = 0; x < size; x++) {
        const dx = x - c, dy = y - c, d = Math.hypot(dx, dy);
        const ring = Math.abs(d - r) <= w;
        const arm = (Math.abs(dx) <= w / 1.3 || Math.abs(dy) <= w / 1.3) && d > r * 0.35 && d < size * 0.48;
        const dot = d <= w;
        if (ring || arm || dot) {
          const i = (y * size + x) * 4;
          buf[i] = 0x3b; buf[i + 1] = 0xff; buf[i + 2] = 0xb6; buf[i + 3] = 0xff; // #b6ff3b as BGRA
        }
      }
    }
    return nativeImage.createFromBitmap(buf, { width: size, height: size });
  }

  function refreshTray() {
    if (!tray) return;
    const d = settings.detection;
    tray.setToolTip(`SmartReticle - ${settings.enabled ? 'on' : 'off'} (${d.toggleHotkey} toggles)`);
    tray.setContextMenu(Menu.buildFromTemplate([
      { label: 'Open SmartReticle', click: showMainWindow },
      { label: 'Overlay enabled', type: 'checkbox', checked: settings.enabled, click: () => setEnabled(!settings.enabled) },
      { type: 'separator' },
      { label: 'Quit', click: () => { quitting = true; app.quit(); } }
    ]));
  }

  // ---- settings ---------------------------------------------------------

  function applySettings() {
    toggleKey = new ComboWatcher(parseCombo(settings.detection.toggleHotkey));
    aimCodes = parseCombo(settings.detection.aimKey);
    sendCrosshair();
    refreshTray();
    if (app.isPackaged) {
      // In development the login item would point at electron.exe, so only packaged builds register it.
      app.setLoginItemSettings({ openAtLogin: settings.app.launchAtLogin, args: ['--minimized'] });
    }
  }

  function saveSoon() {
    clearTimeout(saveTimer);
    saveTimer = setTimeout(() => {
      try { store.save(settingsFile(), settings); }
      catch (error) { log('Settings save failed: ' + error.message); }
    }, 250);
  }

  // `echo` tells the settings window about changes it did not make itself (hotkey, tray). Echoing its own edits
  // back would fight a slider mid-drag.
  function setSettings(next, echo) {
    settings = store.clean(next);
    applySettings();
    saveSoon();
    if (echo && alive(mainWindow)) mainWindow.webContents.send('settings:changed', settings);
    return settings;
  }

  function setEnabled(on) { setSettings({ ...settings, enabled: on }, true); }

  // ---- loop -------------------------------------------------------------

  function tick() {
    const readKey = win.keyDown;
    if (toggleKey && toggleKey.pressed(readKey)) setEnabled(!settings.enabled);
    const aiming = !!aimCodes && aimCodes.every(readKey);

    const r = detector.evaluate(settings.detection, aiming);
    if (!r.own) lastExternal = r;
    if (settings.detection.forceBorderless && r.window) borderless.tryApply(r.window);

    const visible = gate.update(settings.enabled && r.show, settings.detection.showDelayMs, Date.now());
    placeOverlay(r.center, visible);

    if (settings.detection.debugLog) {
      const key = [r.show, r.reason, r.process, r.cursorShown, r.softMenu, r.aiming, settings.enabled].join('|');
      if (key !== lastLogged) { log((settings.enabled ? '' : '[disabled] ') + describe(r)); lastLogged = key; }
    }
  }

  function pushStatus() {
    if (!alive(mainWindow) || !mainWindow.isVisible()) return;
    mainWindow.webContents.send('status', { enabled: settings.enabled, visible: overlayState.visible, detection: lastExternal });
  }

  // ---- IPC --------------------------------------------------------------

  function registerIpc() {
    ipcMain.handle('state:get', () => ({
      settings, presets: store.PRESETS, styles: store.STYLES, version: require('./package.json').version, logFile: logFile(),
      packaged: app.isPackaged
    }));
    ipcMain.handle('settings:set', (_e, next) => setSettings(next, false));
    ipcMain.handle('share:copy', (_e, crosshair) => {
      const code = store.toShareCode(crosshair);
      clipboard.writeText(code);
      return code;
    });
    ipcMain.handle('share:paste', () => store.fromShareCode(clipboard.readText()));
    ipcMain.handle('games:running', () => {
      try { return win.windowedProcessNames(); } catch (error) { log('Window list failed: ' + error.message); return []; }
    });
    ipcMain.handle('log:open', async () => {
      if (!fs.existsSync(logFile())) log('Log opened. Turn on "Write debug log" to record detection changes.');
      return shell.openPath(logFile());
    });
  }

  // ---- start ------------------------------------------------------------

  app.on('second-instance', showMainWindow);
  app.on('before-quit', () => {
    quitting = true;
    clearTimeout(saveTimer);
    try { store.save(settingsFile(), settings); } catch { /* best effort on the way out */ }
  });
  app.on('window-all-closed', (e) => e.preventDefault());

  app.whenReady().then(() => {
    log = createLog(logFile());
    borderless = new BorderlessForcer(log);
    settings = store.load(settingsFile());
    registerIpc();
    tray = new Tray(appIcon(32));
    tray.on('click', showMainWindow);
    applySettings();
    createOverlay();
    const startHidden = process.argv.includes('--minimized') || settings.app.startMinimized;
    createMainWindow(!startHidden);
    setInterval(() => {
      try { tick(); } catch (error) { log('Tick failed: ' + (error.stack || error.message)); }
    }, TICK_MS);
    setInterval(pushStatus, STATUS_MS);
  });
}
