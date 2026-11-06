const { contextBridge, ipcRenderer } = require('electron');

const subscribe = (channel) => (callback) => {
  const listener = (_event, data) => callback(data);
  ipcRenderer.on(channel, listener);
  return () => ipcRenderer.removeListener(channel, listener);
};

contextBridge.exposeInMainWorld('reticle', {
  getState: () => ipcRenderer.invoke('state:get'),
  setSettings: (settings) => ipcRenderer.invoke('settings:set', settings),
  copyShareCode: (crosshair) => ipcRenderer.invoke('share:copy', crosshair),
  pasteShareCode: () => ipcRenderer.invoke('share:paste'),
  runningGames: () => ipcRenderer.invoke('games:running'),
  openLog: () => ipcRenderer.invoke('log:open'),
  onSettings: subscribe('settings:changed')
});
