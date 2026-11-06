// One evaluation per tick: is the crosshair wanted right now, and where is the game's centre?
// Uses only public window/cursor state (see win32.cjs).
const win = require('./win32.cjs');
const { SoftCursorTracker, decide, isTinyClip, isListedGame, NON_GAMES } = require('./logic.cjs');

const NAME_CACHE_MS = 30000;
const DESKTOP_CLASSES = new Set(['Progman', 'WorkerW', 'Shell_TrayWnd']);

class Detector {
  constructor(ownPid) {
    this.ownPid = ownPid;
    this.names = new Map();
    this.namesAt = 0;
    this.soft = new SoftCursorTracker();
  }

  processName(pid) {
    const now = Date.now();
    if (now - this.namesAt > NAME_CACHE_MS || !this.names.has(pid)) {
      // A miss usually means a newly started game, so refresh the whole snapshot once.
      this.names = win.processNames();
      this.namesAt = now;
    }
    return this.names.get(pid) || '?';
  }

  isTargetGame(hwnd, name, detection) {
    if (isListedGame(name, detection.games)) return true;
    if (!detection.autoFullscreen || NON_GAMES.has(name.toLowerCase())) return false;
    if (hwnd === win.shellWindow() || DESKTOP_CLASSES.has(win.className(hwnd))) return false;
    const w = win.windowRect(hwnd), m = win.monitorRect(hwnd);
    return !!(w && m && w.left <= m.left && w.top <= m.top && w.right >= m.right && w.bottom >= m.bottom);
  }

  /** @returns {{show:boolean, reason:string, process:string, window:number, center:{x,y}|null, ...signals}} */
  evaluate(detection, aiming) {
    const r = {
      show: false, reason: '', process: '', window: 0, center: null,
      cursorShown: false, clipped: false, clipSize: '', distance: 0, softMenu: false, aiming: !!aiming
    };
    const hwnd = win.foregroundWindow();
    if (!hwnd) return { ...r, reason: 'no focused window' };
    const pid = win.windowPid(hwnd);
    if (pid === this.ownPid) return { ...r, reason: 'SmartReticle focused', own: true };
    const name = this.processName(pid);
    if (win.isMinimized(hwnd)) return { ...r, process: name, reason: 'game minimised' };
    if (!this.isTargetGame(hwnd, name, detection)) return { ...r, process: name, reason: 'tabbed out / not a game' };

    const center = win.clientCenter(hwnd);
    if (!center) return { ...r, process: name, window: hwnd, reason: 'game window has no client area' };

    const c = win.cursor() || { shown: false, pos: this.soft.last };
    const clip = win.clipRect();
    const clipped = !!clip && isTinyClip(clip);
    const softMenu = this.soft.update({ window: hwnd, pos: c.pos, center, cursorShown: c.shown, clipped });
    const verdict = decide({ detection, cursorShown: c.shown, softMenu, aiming, clipped });
    return {
      ...r, ...verdict, process: name, window: hwnd, center,
      cursorShown: c.shown, clipped, softMenu,
      clipSize: clip ? `${clip.right - clip.left}x${clip.bottom - clip.top}` : '',
      distance: Math.round(Math.hypot(c.pos.x - center.x, c.pos.y - center.y))
    };
  }
}

function describe(r) {
  return `${r.show ? 'SHOW' : 'HIDE'} (${r.reason}) fg=${r.process} cursorShown=${r.cursorShown} clipped=${r.clipped} ` +
    `clip=${r.clipSize} distFromCentre=${r.distance} softMenu=${r.softMenu} aiming=${r.aiming}` +
    (r.center ? ` center=${r.center.x},${r.center.y}` : '');
}

module.exports = { Detector, describe };
