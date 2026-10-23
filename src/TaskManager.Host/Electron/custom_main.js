// Official Electron.NET startup hook; all application data stays on same-origin HTTP.
module.exports.onStartup = function () {
  const { app } = require('electron');
  // Preserve Electron.NET's existing package identity for Windows taskbar grouping.
  app.setAppUserModelId('taskmanager-host');
  const origin = process.env.TASKMANAGER_ORIGIN;
  if (!origin || !/^http:\/\/127\.0\.0\.1:\d+$/.test(origin)) return false;
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
