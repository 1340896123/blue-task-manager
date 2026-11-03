"""Real Qt -> HTTP -> JSON.NET -> CsWin32 acceptance, exclusively at 1080P.

Screenshots: WGC. Mouse/keyboard: the user's FYLHelper skill bridge.
No DOM, UIA invocation, mock server, snapshot fixtures or process-name cleanup.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import traceback
import urllib.error
import urllib.request

parser = argparse.ArgumentParser()
parser.add_argument('--release', required=True)
parser.add_argument('--control-skill', required=True)
parser.add_argument('--output', required=True)
args = parser.parse_args()
sys.path.insert(0, str(Path(args.control_skill) / 'scripts'))
import computer_control as desktop

root = Path(__file__).resolve().parents[1]
output = Path(args.output).resolve()
output.mkdir(parents=True, exist_ok=True)
origin = 'http://127.0.0.1:5215'
checks, screenshots = [], []
report = {'result': 'RUNNING', 'display': '1920x1080 physical pixels',
          'capture': 'WGC', 'input': 'FYLHelper', 'checks': checks,
          'screenshots': screenshots}
token = None
host = None
hwnd = qt_pid = None
target = None


def progress(message):
    report['progress'] = message
    (output / 'results.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')


def check(condition, message):
    if not condition:
        raise AssertionError(message)
    checks.append(message)
    progress(message)


def api(route, body=None, expected=200):
    headers = {'Origin': origin}
    if body is not None:
        headers.update({'Content-Type': 'application/json', 'X-TaskManager-Token': token or ''})
    request = urllib.request.Request(origin + '/api/' + route,
        data=None if body is None else json.dumps(body).encode(), headers=headers)
    try:
        response = urllib.request.urlopen(request, timeout=15)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        value = json.loads(response.read())
        if response.status != expected:
            raise AssertionError(f'{route}: HTTP {response.status}; {value.get("error", {}).get("message", "unexpected response")}')
        return value


def until(predicate, timeout=20):
    deadline = time.monotonic() + timeout
    last_error = None
    while time.monotonic() < deadline:
        try:
            value = predicate()
            if value:
                return value
        except (OSError, RuntimeError, urllib.error.URLError) as error:
            last_error = error
        time.sleep(.2)
    raise TimeoutError(f'Condition timed out: {last_error}')


def key(*keys):
    active = desktop.get_foreground_window()
    info = desktop.get_window_info(active)
    if info['pid'] != qt_pid:
        owned = [w for w in desktop.list_windows() if w['pid'] == qt_pid]
        modal = next((w for w in owned if w['title'] != 'Takao'), None)
        desktop.activate_window(modal['hwnd'] if modal else hwnd)
        active = desktop.get_foreground_window()
        if desktop.get_window_info(active)['pid'] != qt_pid:
            raise RuntimeError('Input target changed away from the test-owned Qt process')
    with desktop.Input(active) as device:
        if len(keys) == 1:
            device.press(keys[0])
        else:
            try:
                for modifier in keys[:-1]:
                    device.key_down(modifier)
                time.sleep(.1)
                device.press(keys[-1])
                time.sleep(.1)
            finally:
                for modifier in reversed(keys[:-1]):
                    device.key_up(modifier)
    time.sleep(.12)


def text(value):
    active = desktop.get_foreground_window()
    if desktop.get_window_info(active)['pid'] != qt_pid:
        raise RuntimeError('Text target changed away from the test-owned Qt process')
    with desktop.Input(active) as device:
        if value.isascii():
            device.type_text(value)
        else:
            device.paste_text(value)
    time.sleep(.12)


def click_label(label, region=None):
    active = desktop.get_foreground_window()
    if desktop.get_window_info(active)['pid'] != qt_pid:
        desktop.activate_window(hwnd)
        active = hwnd
    match = desktop.find_text(active, label, match='exact', region=region)
    if match is None:
        raise AssertionError('Visible control not found: ' + label)
    # OCR may take time; re-establish the exact owned input target before clicking.
    desktop.activate_window(active)
    left, top, right, bottom = match['bbox_client']
    with desktop.Input(active) as device:
        device.click((left+right)//2, (top+bottom)//2)
    time.sleep(.2)


def shot(name):
    # Capture the visible app frame; native shadows/invisible resize borders are excluded.
    bounds = desktop.get_window_info(hwnd)['capture_bounds']
    frame = desktop.capture_screen(1, region=bounds)
    height, width = frame.image.shape[:2]
    check((width, height) == (1920, 1080), f'{name}: screenshot is exactly 1920x1080')
    frame.save(output / (name + '.png'))
    screenshots.append(name + '.png')
    return frame


def search(value):
    match = desktop.find_text(hwnd, '搜索', match='exact', region=(185, 0, 800, 65))
    if match is None:
        raise AssertionError('Shared search label is visible')
    desktop.activate_window(hwnd)
    left, top, right, bottom = match['bbox_client']
    with desktop.Input(hwnd) as device:
        # The adjacent input was verified in the current WGC frame.
        device.click(right + 80, (top+bottom)//2)
        device.press('end')
        device.press('backspace', 256)
    if value:
        text(value)


temporary = tempfile.TemporaryDirectory(prefix='Takao-Qt-UI-')
try:
    env = dict(os.environ, TASKMANAGER_DATA_DIR=str(Path(temporary.name) / 'preferences'),
               QT_SCALE_FACTOR='1', QT_SCREEN_SCALE_FACTORS='1', TASKMANAGER_QT_1080P='1')
    host = subprocess.Popen([str(Path(args.release) / 'Takao.Host.exe'), '--port=5215'],
        env=env, cwd=args.release, creationflags=subprocess.CREATE_NO_WINDOW,
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    bootstrap = until(lambda: api('bootstrap') if host.poll() is None else None, 40)
    token = bootstrap['token']
    check(bootstrap['hostPid'] == host.pid, 'bootstrap belongs to the exact test Host')
    until(lambda: api('desktop/window-state')['available'], 35)
    bootstrap = api('bootstrap')
    qt_pid = bootstrap['desktopProcessId']
    check(bootstrap['desktopFramework'] == 'Qt 6 Widgets', 'production desktop uses Qt 6 Widgets')
    window = until(lambda: next((w for w in desktop.list_windows() if w['pid'] == qt_pid and w['title'] == 'Takao'), None), 15)
    hwnd = window['hwnd']
    # Wait for the asynchronous real first-launch integration read before focusing main.
    for _ in range(30):
        modal = next((w for w in desktop.list_windows() if w['pid'] == qt_pid and w['title'] == '替换标准任务管理器？'), None)
        if modal:
            desktop.activate_window(modal['hwnd'])
            key('escape')
            until(lambda: not any(w['hwnd'] == modal['hwnd'] for w in desktop.list_windows()))
            break
        if api('preferences')['preferences'].get('taskManagerPromptDismissed'):
            break
        time.sleep(.2)
    desktop.activate_window(hwnd)
    desktop.set_window_state(hwnd, 'restore')
    # Account for Windows invisible resize margins without testing a second viewport.
    info = desktop.get_window_info(hwnd)
    wr, bounds = info['window_rect'], info['capture_bounds']
    desktop.move_resize_window(hwnd, 24, 24,
        1920 + (wr[2]-wr[0]) - (bounds[2]-bounds[0]),
        1080 + (wr[3]-wr[1]) - (bounds[3]-bounds[1]))
    time.sleep(.8)
    snapshot = until(lambda: (lambda s: s if any(row['pid'] == qt_pid for row in s['processes']) else None)(api('snapshot')))
    check(snapshot['system']['totalPhysicalBytes'] > 0, 'production snapshot has real physical memory')
    qt_row = next(row for row in snapshot['processes'] if row['pid'] == qt_pid)
    check(qt_row['canTerminate'] is False, 'the native Qt child is protected against termination')
    # First-launch integration uses an explicit choice. Test profiles never change IFEO.
    if desktop.get_foreground_window() != hwnd:
        active = desktop.get_foreground_window()
        check(desktop.get_window_info(active)['pid'] == qt_pid, 'first-launch dialog belongs to Qt')
        key('escape')
    titles = ['进程', '性能', '应用历史记录', '启动应用', '用户', '详细信息', '服务', '设置']
    click_label('进程', region=(0, 40, 205, 180))
    for index, title in enumerate(titles, 1):
        desktop.activate_window(hwnd)
        if index > 1:
            key('tab')
            key('space')
        time.sleep(.65)
        shot(f'{index:02d}-page')
        entries = desktop.ocr_window(hwnd, region=(210, 35, 1400, 190))
        check(any(title in item['text'] for item in entries), f'{title}: keyboard navigation renders the page heading')
    # Subsequent interactions are implemented with the same visible native controls.
    progress('Eight pages captured; running task/selection/modal/setting checks')
    desktop.activate_window(hwnd)
    click_label('进程', region=(0, 40, 205, 180))
    search('Takao-no-such-process-1080P')
    time.sleep(.5)
    shot('09-empty-search')
    search('')
    # A dedicated workload is created through the real Qt run dialog, never through a test RPC.
    click_label('运行新任务', region=(1000, 50, 1750, 140))
    until(lambda: desktop.get_foreground_window() != hwnd)
    active = desktop.get_foreground_window()
    check(desktop.get_window_info(active)['pid'] == qt_pid, 'run dialog is a native Qt modal')
    workload = root / 'tests/Takao.TestWorkload/bin/Release/net10.0-windows10.0.19041.0/Takao.TestWorkload.exe'
    text(str(workload))
    key('tab')
    text('"' + str(Path(temporary.name) / 'workload') + '" 120')
    shot('10-run-dialog')
    key('enter')
    until(lambda: desktop.get_foreground_window() == hwnd)
    def owned_target():
        matches = [row for row in api('snapshot')['processes'] if row.get('name') == 'Takao.TestWorkload.exe'
                   and row.get('parentPid') == host.pid and row.get('creationTimeFileTime')]
        return matches[0] if len(matches) == 1 else None
    target = until(owned_target)
    check(target['canTerminate'], 'Qt run action creates an identity-verified dedicated workload')
    # The complete unique identity guarantees the UI action cannot select another process.
    search(target['identity'])
    time.sleep(.5)
    click_label(target['name'], region=(210, 130, 900, 320))
    key('delete')
    until(lambda: desktop.get_foreground_window() != hwnd)
    shot('11-terminate-confirmation')
    key('escape')
    check(any(row['identity'] == target['identity'] for row in api('snapshot')['processes']), 'Esc cancellation leaves the dedicated process alive')
    key('delete')
    until(lambda: desktop.get_foreground_window() != hwnd)
    # Destructive confirmation initially focuses Cancel. Tab selects the explicit action.
    key('tab')
    key('space')
    until(lambda: desktop.get_foreground_window() == hwnd)
    until(lambda: not any(row['identity'] == target['identity'] for row in api('snapshot')['processes']))
    target = None
    check(True, 'confirmed Qt termination closes the dialog and the real process disappears')
    shot('12-termination-feedback')
    search('')
    api('window/close', {})
    host.wait(timeout=20)
    check(host.returncode == 0, 'closing the Qt window stops its Host cleanly')
    check(not any(window['pid'] == qt_pid for window in desktop.list_windows()), 'no test Qt window remains')
    report['result'] = 'PASS'
except BaseException as error:
    report['result'] = 'FAIL'
    report['error'] = f'{type(error).__name__}: {error}'
    report['traceback'] = traceback.format_exc()
    raise
finally:
    if target and token:
        try:
            api(f'processes/{target["pid"]}/terminate', {'creationTimeFileTime': target['creationTimeFileTime']})
        except Exception:
            pass
    if host and host.poll() is None:
        try:
            if hwnd:
                desktop.close_window(hwnd)
            host.wait(timeout=15)
        except Exception:
            host.kill()  # Popen's original handle owns only this exact test Host.
            host.wait(timeout=10)
    temporary.cleanup()
    report['assertions'] = len(checks)
    progress('Acceptance completed')
