// Dev tool: records the detector's raw signals while a game is focused, one JSON line per 50 ms.
// Usage: node scripts/probe-signals.cjs <seconds> <out.jsonl> [game]
const fs = require('node:fs');
const win = require('../engine/win32.cjs');

const seconds = Number(process.argv[2] || 60);
const out = process.argv[3] || 'probe.jsonl';
const game = (process.argv[4] || 'TheDivision2').toLowerCase();
const KEYS = { M: 0x4d, Esc: 0x1b, Tab: 0x09, I: 0x49, RMB: 0x02, LMB: 0x01 };

const names = win.processNames();
const stream = fs.createWriteStream(out);
const end = Date.now() + seconds * 1000;
const timer = setInterval(() => {
  if (Date.now() > end) { clearInterval(timer); stream.end(); return; }
  const hwnd = win.foregroundWindow();
  const pid = win.windowPid(hwnd);
  if (!names.has(pid)) for (const [k, v] of win.processNames()) names.set(k, v);
  const proc = names.get(pid) || '?';
  if (proc.toLowerCase() !== game) return;
  const c = win.cursor(), clip = win.clipRect(), center = win.clientCenter(hwnd);
  const keys = Object.entries(KEYS).filter(([, vk]) => win.keyDown(vk)).map(([k]) => k);
  stream.write(JSON.stringify({
    t: Date.now(), shown: c && c.shown, x: c && c.pos.x, y: c && c.pos.y, cx: center && center.x, cy: center && center.y,
    clip: clip && [clip.left, clip.top, clip.right, clip.bottom], keys
  }) + '\n');
}, 50);
