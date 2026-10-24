// Official Electron.NET startup hook; all application data stays on same-origin HTTP.
module.exports.onStartup = function () {
  const { app, BrowserWindow, ipcMain } = require('electron');
  // Preserve Electron.NET's existing package identity for Windows taskbar grouping.
  app.setAppUserModelId('taskmanager-host');
  const origin = process.env.TASKMANAGER_ORIGIN;
  if (!origin || !/^http:\/\/127\.0\.0\.1:\d+$/.test(origin)) return false;
  // Window chrome must keep working when the HTTP collector is unavailable.
  // Business operations continue to use the authenticated same-origin API.
  ipcMain.handle('taskmanager:window-action', (event, action) => {
    const current = BrowserWindow.fromWebContents(event.sender);
    const frame = event.senderFrame;
    // Validate the actual sender. Electron 45 omits preload from preference readback.
    if (!current || current.isDestroyed() || frame !== event.sender.mainFrame || frame?.origin !== origin) {
      throw new Error('窗口请求来源无效。');
    }
    switch (action) {
      case 'minimize': current.minimize(); break;
      case 'maximize': if (current.isMaximized()) current.unmaximize(); else current.maximize(); break;
      case 'close':
        // Let Electron return the IPC acknowledgement before destroying its sender.
        setImmediate(() => { if (!current.isDestroyed()) current.close(); });
        break;
      default: throw new Error('未知窗口操作。');
    }
  });
  const cdpPort = process.env.TASKMANAGER_CDP_PORT;
  if (cdpPort && /^\d+$/.test(cdpPort) && Number(cdpPort) >= 1024 && Number(cdpPort) <= 65535) {
    app.commandLine.appendSwitch('remote-debugging-port', cdpPort);
    app.commandLine.appendSwitch('remote-debugging-address', '127.0.0.1');
  }
  app.on('web-contents-created', (_event, contents) => {
    const check = (event, url) => { try { if (new URL(url).origin === origin) return; } catch {} event.preventDefault(); };
    contents.on('will-navigate', check);
    contents.on('will-redirect', check);
    contents.setWindowOpenHandler(() => ({ action: 'deny' }));
  });
  return true;
};
