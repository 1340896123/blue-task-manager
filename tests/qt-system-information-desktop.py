"""Focused production Qt hardware-page checks through real FYLHelper input and WGC."""
import argparse
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
import traceback
import urllib.request


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release', required=True)
    parser.add_argument('--control-skill', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    sys.path.insert(0, str(Path(args.control_skill) / 'scripts'))
    import computer_control as desktop

    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    checks, captures = [], []
    report = {'result': 'RUNNING', 'scope': 'System information, affected performance and settings navigation only',
              'input': 'FYLHelper; real mouse and keyboard; M_ResolutionUsed before opening each input session',
              'capture': 'WGC, 1920x1080 visible window pixels at 100%; no image resizing',
              'checks': checks, 'screenshots': captures, 'primaryResolution': desktop.get_primary_resolution()}
    started = time.monotonic()
    host = hwnd = qt_pid = token = None
    original_clipboard = desktop.get_clipboard_text()
    temporary = tempfile.TemporaryDirectory(prefix='Takao-System-Information-Desktop-')
    log = (output / 'host.log').open('w', encoding='utf-8')

    def save():
        report['elapsedSeconds'] = round(time.monotonic() - started, 3)
        (output / 'results.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')

    def check(value, message):
        if not value:
            raise AssertionError(message)
        checks.append(message)
        save()

    def api(route, body=None):
        headers = {'Origin': origin}
        if body is not None:
            headers.update({'Content-Type': 'application/json', 'X-TaskManager-Token': token})
        data = None if body is None else json.dumps(body).encode()
        with urllib.request.urlopen(urllib.request.Request(origin + '/api/' + route, data=data, headers=headers), timeout=12) as response:
            return json.loads(response.read())

    def until(predicate, timeout=20):
        deadline = time.monotonic() + timeout
        last = None
        while time.monotonic() < deadline:
            try:
                value = predicate()
                if value:
                    return value
            except (OSError, RuntimeError) as error:
                last = error
            time.sleep(.2)
        raise TimeoutError(f'Condition did not become ready: {last}')

    def activate():
        check(desktop.get_window_info(hwnd)['pid'] == qt_pid, 'Input and capture target belongs to this exact test-owned Qt')
        try:
            desktop.activate_window(hwnd)
        except RuntimeError as error:
            if 'ForegroundDenied' not in str(error):
                raise
            # Window management can raise our exact private window. The actual
            # activation click still uses the prescribed FYLHelper device.
            from ctypes import wintypes as W
            import ctypes as C
            win = desktop.win
            position = win.bind(win.u, 'SetWindowPos', W.BOOL, W.HWND, W.HWND,
                                C.c_int, C.c_int, C.c_int, C.c_int, W.UINT)
            if not position(hwnd, W.HWND(-1), 0, 0, 0, 0, 0x13):
                raise RuntimeError('Could not raise the exact owned window')
            left, top, right, bottom = desktop.get_window_info(hwnd)['capture_bounds']
            x, y = left + 120, top + 16
            hit = win.u.WindowFromPoint(W.POINT(x, y))
            if not hit or int(win.u.GetAncestor(hit, 2) or hit) != hwnd:
                raise RuntimeError('Owned titlebar is occluded; no activation input was sent')
            with desktop.Input(global_input=True) as device:
                device.click(x, y, space='screen')
            until(lambda: desktop.get_foreground_window() == hwnd, 3)

    def shot(name):
        frame = desktop.capture_window(hwnd, client_only=False, timeout=15)
        check(frame.image.shape[:2] == (1080, 1920), name + ': WGC is exactly 1920x1080')
        path = output / (name + '.png')
        frame.save(path)
        captures.append(str(path))
        save()
        return frame

    def heading(title):
        until(lambda: desktop.find_text(hwnd, title, match='exact', region=(210, 65, 900, 145)), 15)

    def navigate(device, title, region):
        found = desktop.find_text(hwnd, title, match='exact', region=region)
        if found is None:
            raise AssertionError('The visible native navigation was not found: ' + title)
        left, top, right, bottom = found['bbox_client']
        device.click((left + right) // 2, (top + bottom) // 2)
        heading(title)

    def persisted_query(value):
        return api('preferences')['preferences'].get('pages', {}).get('systemInformation', {}).get('query') == value

    try:
        profile = Path(temporary.name) / 'preferences'
        profile.mkdir()
        # Only UI preferences are seeded; all hardware and telemetry come from
        # the production Host and native collectors. No user preferences change.
        (profile / 'preferences.json').write_text(json.dumps({'version': 1, 'intervalMs': 1000,
            'ui': {'version': 1, 'density': 'compact', 'sidebarCollapsed': False, 'processView': 'flat',
                   'taskManagerPromptDismissed': True, 'searchQuery': '',
                   'window': {'width': 1100, 'height': 720, 'zoomFactor': 1, 'alwaysOnTop': True},
                   'pages': {'performance': {'query': '', 'sortKey': 'name', 'sortDirection': 'ascending',
                                              'resourceType': 'hwinfo', 'resourceId': ''}}}}), encoding='utf-8')
        with socket.socket() as reservation:
            reservation.bind(('127.0.0.1', 0))
            port = reservation.getsockname()[1]
        origin = f'http://127.0.0.1:{port}'
        env = dict(os.environ, TASKMANAGER_DATA_DIR=str(profile), QT_SCALE_FACTOR='1', QT_SCREEN_SCALE_FACTORS='1',
                   TASKMANAGER_QT_1080P='1')
        release = Path(args.release).resolve()
        host = subprocess.Popen([str(release / 'Takao.Host.exe'), f'--port={port}'], cwd=release, env=env,
                                creationflags=subprocess.CREATE_NO_WINDOW, stdout=log, stderr=subprocess.STDOUT)
        bootstrap = until(lambda: api('bootstrap'), 35)
        check(bootstrap['hostPid'] == host.pid, 'Production HTTP belongs to the exact isolated Host')
        token = bootstrap['token']
        bootstrap = until(lambda: (lambda value: value if value.get('desktopProcessId') else None)(api('bootstrap')))
        qt_pid = bootstrap['desktopProcessId']
        report.update(hostPid=host.pid, qtPid=qt_pid, port=port)
        check(bootstrap['desktopFramework'] == 'Qt 6 Widgets', 'Formal production Host starts the native Qt desktop')
        hwnd = until(lambda: next((w['hwnd'] for w in desktop.list_windows() if w['pid'] == qt_pid and w['title'] == 'Takao'), None))
        # The production preference load applies window state asynchronously.
        # Wait for its actual read-back before calibrating or sending input.
        until(lambda: api('desktop/window-state').get('alwaysOnTop') is True)
        activate()
        desktop.set_window_state(hwnd, 'restore')
        info = desktop.get_window_info(hwnd)
        check(info['dpi'] == 96, 'Owned window runs at actual 100% / 96 DPI')
        wr, bounds = info['window_rect'], info['capture_bounds']
        desktop.move_resize_window(hwnd, 24, 24, 1920 + (wr[2]-wr[0])-(bounds[2]-bounds[0]),
                                   1080 + (wr[3]-wr[1])-(bounds[3]-bounds[1]))
        hardware = api('system-information')
        check(hardware['status'] in ('ready', 'partial') and len(hardware['processors']) > 0,
              'Page hardware source is a real completed Windows collection')
        report['hardwareCapturedAtUtc'] = hardware['capturedAtUtc']
        with desktop.Input(hwnd) as device:
            navigate(device, '系统信息', (0, 370, 205, 525))
            time.sleep(.5)
            overview = shot('01-system-information-1080p')
            # OCR the actual category column at native pixel size. Whole-frame
            # OCR downsamples 1920px frames and loses small Chinese glyphs.
            text = ' '.join(item['text'] for item in desktop.ocr_frame(overview.crop((210, 155, 345, 1045))))
            for category in ('处理器', '显卡', '主板', '内存', '硬盘'):
                check(category in text, 'Real native navigation displays category: ' + category)
            device.hotkey('ctrl', 'f')
            device.hotkey('ctrl', 'a')
            device.paste_text('Takao-no-such-hardware-SI')
            until(lambda: persisted_query('Takao-no-such-hardware-SI'))
            time.sleep(.3)
            empty = shot('02-system-information-empty-1080p')
            check(any('没有匹配' in item['text'] for item in desktop.ocr_frame(empty)), 'Unmatched hardware search has an explicit empty state')
            device.press('escape')
            until(lambda: persisted_query(''))
            # Scroll inside the actual hardware content observed in the first
            # WGC frame; wheel input goes through the same FYLHelper session.
            device.move_to(900, 800)
            device.scroll(-12)
            time.sleep(.3)
            lower = shot('02b-system-information-lower-1080p')
            text = ' '.join(item['text'] for item in desktop.ocr_frame(lower.crop((210, 155, 1330, 1045))))
            check('显示器' in text and 'Windows' in text, 'Esc restores the hardware view and physical scrolling exposes display and Windows facts')
            check('HWiNFO' not in text, 'Hardware overview contains no removed HWiNFO sensor content')
            previous = api('system-information')['capturedAtUtc']
            device.press('f5')
            until(lambda: api('system-information')['capturedAtUtc'] != previous)
            check(True, 'Physical F5 refresh advances the real native hardware collection timestamp')
            navigate(device, '性能', (0, 90, 205, 195))
            performance = shot('03-performance-without-hwinfo-1080p')
            text = ' '.join(item['text'] for item in desktop.ocr_frame(performance))
            check('HWiNFO' not in text and 'CPU' in text, 'Legacy HWiNFO preference opens CPU and the sensor entry is absent')
            navigate(device, '设置', (0, 960, 205, 1048))
            shot('04-settings-navigation-1080p')
            check(True, 'Existing settings navigation remains available after inserting the new page')
            navigate(device, '系统信息', (0, 370, 205, 525))
            check(persisted_query(''), 'New-page query remains isolated and restores after visiting other pages')
        report['result'] = 'PASS'
    except BaseException as error:
        report.update(result='FAIL', error=f'{type(error).__name__}: {error}', traceback=traceback.format_exc())
        save()
        if hwnd:
            try:
                report['failureWindow'] = desktop.get_window_info(hwnd)
                shot('failure-1080p')
            except Exception as capture_error:
                report['failureCaptureError'] = str(capture_error)
    finally:
        if host and host.poll() is None:
            try:
                if token and api('bootstrap')['hostPid'] == host.pid:
                    api('window/close', {})
                    host.wait(timeout=15)
            except Exception:
                host.terminate()
                host.wait(timeout=10)
        report['ownedHostExited'] = host is None or host.poll() is not None
        report['ownedQtWindowExited'] = qt_pid is None or not any(w['pid'] == qt_pid for w in desktop.list_windows())
        report['hostExitCode'] = host.returncode if host else None
        if host:
            with socket.socket() as probe:
                report['ownedPortClosed'] = probe.connect_ex(('127.0.0.1', port)) != 0
        if report['result'] == 'PASS' and (not report['ownedHostExited'] or not report['ownedQtWindowExited'] or
                                          not report.get('ownedPortClosed') or report['hostExitCode'] != 0):
            report.update(result='FAIL', error='The exact owned application did not close cleanly')
        if original_clipboard is not None:
            try:
                desktop.set_clipboard_text(original_clipboard)
            except Exception as clipboard_error:
                report['clipboardRestoreError'] = str(clipboard_error)
        log.close()
        temporary.cleanup()
        save()
    if report['result'] != 'PASS':
        raise SystemExit(report.get('error', 'Desktop checks failed'))
    print(f'PASS {len(checks)} focused desktop checks; {output / "results.json"}')


if __name__ == '__main__':
    main()
