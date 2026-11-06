// Turns a windowed game into borderless fullscreen: strips the title bar/frame and sizes it to its monitor.
// Window styles only, like "Borderless Gaming"; no injection. Exclusive-fullscreen windows already have no frame
// and fill the monitor, so they are left alone.
const win = require('./win32.cjs');

const CHECK_INTERVAL_MS = 500;
const MAX_ATTEMPTS_PER_WINDOW = 5; // stop if the game keeps putting its frame back

const FRAME_BITS = win.WS.CAPTION | win.WS.THICKFRAME | win.WS.SYSMENU | win.WS.MINIMIZEBOX | win.WS.MAXIMIZEBOX;
const EDGE_BITS = win.WS_EX.DLGMODALFRAME | win.WS_EX.WINDOWEDGE | win.WS_EX.CLIENTEDGE | win.WS_EX.STATICEDGE;

class BorderlessForcer {
  constructor(log) {
    this.log = log;
    this.attempts = new Map();
    this.lastCheck = 0;
  }

  /** @returns {boolean} true if the window was changed this call */
  tryApply(hwnd) {
    if (!hwnd || win.isMinimized(hwnd)) return false;
    const now = Date.now();
    if (now - this.lastCheck < CHECK_INTERVAL_MS) return false;
    this.lastCheck = now;

    const mon = win.monitorRect(hwnd), rect = win.windowRect(hwnd);
    if (!mon || !rect) return false;
    const style = win.getStyle(hwnd);
    const hasFrame = (style & (win.WS.CAPTION | win.WS.THICKFRAME)) !== 0;
    const fills = rect.left === mon.left && rect.top === mon.top && rect.right === mon.right && rect.bottom === mon.bottom;
    if (!hasFrame && fills) return false;

    const n = this.attempts.get(hwnd) || 0;
    if (n >= MAX_ATTEMPTS_PER_WINDOW) return false;
    this.attempts.set(hwnd, n + 1);
    if (this.attempts.size > 64) this.attempts.clear();

    win.setStyle(hwnd, (style & ~FRAME_BITS) >>> 0);
    win.setExStyle(hwnd, (win.getExStyle(hwnd) & ~EDGE_BITS) >>> 0);
    const w = mon.right - mon.left, h = mon.bottom - mon.top;
    const ok = win.setPos(hwnd, mon.left, mon.top, w, h, win.SWP.FRAMECHANGED | win.SWP.NOOWNERZORDER | win.SWP.NOACTIVATE);
    this.log(`Borderless: ${ok ? 'applied' : 'FAILED (game may run as admin; run SmartReticle as admin too)'} ` +
      `to window ${hwnd} -> ${w}x${h} at ${mon.left},${mon.top} (attempt ${n + 1})`);
    return ok;
  }
}

module.exports = { BorderlessForcer };
