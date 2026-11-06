// Dev tool: starts the app, screenshots every tab of the settings window, then quits.
// Usage: electron scripts/snapshot.cjs <output-dir>
const { app, BrowserWindow } = require('electron');
const path = require('node:path');
const fs = require('node:fs');

const outDir = path.resolve(process.argv.find((a, i) => i > 1 && !a.startsWith('-') && !a.endsWith('.cjs')) || 'snapshots');
require('../main.cjs');

const wait = (ms) => new Promise(r => setTimeout(r, ms));

app.whenReady().then(async () => {
  await wait(4000);
  const win = BrowserWindow.getAllWindows().find(w => w.getTitle() === 'SmartReticle');
  if (!win) { console.error('Settings window not found'); app.exit(1); return; }
  win.show();
  // SNAP_WIDTH=940 checks the narrowest allowed window.
  if (process.env.SNAP_WIDTH) win.setContentSize(Number(process.env.SNAP_WIDTH), win.getContentSize()[1]);
  fs.mkdirSync(outDir, { recursive: true });
  for (const tab of ['crosshair', 'settings']) {
    await win.webContents.executeJavaScript(`document.querySelector('[data-tab="${tab}"]').click()`);
    await wait(500);
    const img = await win.webContents.capturePage();
    fs.writeFileSync(path.join(outDir, tab + '.png'), img.toPNG());
    // Full page: grow the window to the content height, capture, then restore.
    const [w, h] = win.getContentSize();
    const full = await win.webContents.executeJavaScript('document.documentElement.scrollHeight');
    win.setContentSize(w, Math.min(full, 2600));
    await wait(700);
    const tall = await win.webContents.capturePage();
    fs.writeFileSync(path.join(outDir, tab + '-full.png'), tall.toPNG());
    win.setContentSize(w, h);
    await wait(300);
  }
  // Keyboard focus check: tab to the first few controls and capture the ring.
  await win.webContents.executeJavaScript(`document.querySelector('[data-tab="crosshair"]').click()`);
  await wait(300);
  win.focus();
  for (let i = 0; i < 3; i++) { win.webContents.sendInputEvent({ type: 'keyDown', keyCode: 'Tab' }); win.webContents.sendInputEvent({ type: 'keyUp', keyCode: 'Tab' }); await wait(120); }
  await wait(300);
  fs.writeFileSync(path.join(outDir, 'focus.png'), (await win.webContents.capturePage()).toPNG());
  console.log('Saved to ' + outDir);
  app.exit(0);
});
