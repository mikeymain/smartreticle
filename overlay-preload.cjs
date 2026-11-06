const { contextBridge, ipcRenderer } = require('electron');

// The overlay only ever listens; it cannot ask the main process for anything.
contextBridge.exposeInMainWorld('overlayBridge', {
  onCrosshair: (cb) => ipcRenderer.on('overlay:crosshair', (_e, c) => cb(c)),
  onPlace: (cb) => ipcRenderer.on('overlay:place', (_e, p) => cb(p)),
  onVisible: (cb) => ipcRenderer.on('overlay:visible', (_e, v) => cb(v))
});
