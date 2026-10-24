const { contextBridge, ipcRenderer } = require('electron');

// Expose only fixed window chrome actions, never ipcRenderer or arbitrary channels.
if (process.isMainFrame) {
  contextBridge.exposeInMainWorld('taskManagerWindow', {
    minimize: () => ipcRenderer.invoke('taskmanager:window-action', 'minimize'),
    maximize: () => ipcRenderer.invoke('taskmanager:window-action', 'maximize'),
    close: () => ipcRenderer.invoke('taskmanager:window-action', 'close'),
  });
}
