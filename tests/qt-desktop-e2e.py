"""Real Qt -> HTTP -> JSON.NET -> CsWin32 acceptance, exclusively at 1080P.

Screenshots: WGC. Mouse/keyboard: the user's FYLHelper skill bridge.
No DOM, UIA invocation, mock server, snapshot fixtures or process-name cleanup.
"""
import argparse
import ctypes as C
import csv
import hashlib
import json
import os
import re
from pathlib import Path
import subprocess
import socket
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
parser.add_argument('--workload', required=True)
parser.add_argument('--skip-pages', action='store_true', help='Focused diagnosis; final acceptance omits this option')
parser.add_argument('--extended', action='store_true')
parser.add_argument('--file-dialogs', action='store_true', help='Also exercise operating-system program/save dialogs')
args = parser.parse_args()
sys.path.insert(0, str(Path(args.control_skill) / 'scripts'))
import computer_control as desktop

root = Path(__file__).resolve().parents[1]
output = Path(args.output).resolve()
output.mkdir(parents=True, exist_ok=True)
with socket.socket() as reservation:
    reservation.bind(('127.0.0.1', 0))
    port = reservation.getsockname()[1]
origin = f'http://127.0.0.1:{port}'
checks, screenshots = [], []
report = {'result': 'RUNNING', 'display': '1920x1080 physical pixels',
          'capture': 'WGC', 'input': 'FYLHelper', 'checks': checks,
          'screenshots': screenshots, 'input_move': 'M_MoveTo3',
          'primary_resolution': desktop.get_primary_resolution(), 'events': [],
          'text_input': 'Explicit clipboard paste through FYLHelper Ctrl+V'}
report['extended'] = args.extended
report['file_dialogs'] = args.file_dialogs
report['focused_diagnosis_only'] = args.skip_pages
report['artifacts'] = {}
for label, artifact in {
    'qt': Path(args.release)/'Takao.Qt.exe',
    'host': Path(args.release)/'Takao.Host.dll',
    'workload': Path(args.workload),
    'control_skill': Path(args.control_skill)/'SKILL.md',
    'input_bridge': Path(args.control_skill)/'.runtime/bridge/FylBridge.dll',
}.items():
    report['artifacts'][label]={'path':str(artifact.resolve()),'sha256':hashlib.sha256(artifact.read_bytes()).hexdigest()}
original_clipboard = desktop.get_clipboard_text()
token = None
host = None
host_log = None
hwnd = qt_pid = None
target = None
workload = Path(args.workload).resolve()


def progress(message):
    report['progress'] = message
    (output / 'results.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')


def check(condition, message):
    if not condition:
        raise AssertionError(message)
    checks.append(message)
    progress(message)


def event(message):
    value = {'action': message}
    if hwnd is not None:
        info = desktop.get_window_info(hwnd)
        value.update(window_rect=info['window_rect'], capture_bounds=info['capture_bounds'],
                     foreground=desktop.get_foreground_window())
    report['events'].append(value)
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


def activate(handle):
    if qt_pid and desktop.get_window_info(handle)['pid'] != qt_pid:
        raise RuntimeError('Window management target is not owned by this test Qt process')
    try:
        desktop.activate_window(handle)
    except RuntimeError as error:
        if 'ForegroundDenied' not in str(error):
            raise
        # Window management only: temporarily raise this exact owned window.
        # All mouse/keyboard events still go through the FYLHelper device.
        event('Raise the owned window after Windows denied foreground activation')
        from ctypes import wintypes as W
        win = desktop.win
        current_thread = win.bind(win.k, 'GetCurrentThreadId', W.DWORD)()
        attach = win.bind(win.u, 'AttachThreadInput', W.BOOL, W.DWORD, W.DWORD, W.BOOL)
        bring_top = win.bind(win.u, 'BringWindowToTop', W.BOOL, W.HWND)
        # AttachThreadInput requires a message queue for the Python thread.
        peek = win.bind(win.u, 'PeekMessageW', W.BOOL, C.POINTER(W.MSG), W.HWND, W.UINT, W.UINT, W.UINT)
        message = W.MSG()
        peek(C.byref(message), None, 0, 0, 0)
        threads = {win.u.GetWindowThreadProcessId(desktop.get_foreground_window(), None),
                   win.u.GetWindowThreadProcessId(handle, None)} - {current_thread, 0}
        attached = []
        try:
            for thread in threads:
                C.set_last_error(0)
                ok = attach(current_thread, thread, True)
                report.setdefault('activation_diagnostics',[]).append({'thread':thread,'attached':bool(ok),'error':C.get_last_error() if not ok else 0})
                if ok:
                    attached.append(thread)
            bring_top(handle)
            desktop.activate_window(handle)
        finally:
            for thread in reversed(attached):
                attach(current_thread, thread, False)


def key(*keys):
    active = desktop.get_foreground_window()
    info = desktop.get_window_info(active)
    if info['pid'] != qt_pid:
        owned = [w for w in desktop.list_windows() if w['pid'] == qt_pid]
        modal = next((w for w in owned if w['title'] != 'Takao'), None)
        activate(modal['hwnd'] if modal else hwnd)
        active = desktop.get_foreground_window()
        if desktop.get_window_info(active)['pid'] != qt_pid:
            raise RuntimeError('Input target changed away from the test-owned Qt process')
    with desktop.Input(active) as device:
        if len(keys) == 1:
            device.press(keys[0])
        else:
            device.hotkey(*keys)
    time.sleep(.12)


def text(value):
    active = desktop.get_foreground_window()
    if desktop.get_window_info(active)['pid'] != qt_pid:
        raise RuntimeError('Text target changed away from the test-owned Qt process')
    with desktop.Input(active) as device:
        # The user's Chinese IME may compose ASCII/digits. An explicit paste
        # preserves literal test paths; Ctrl+V is still a real FYLHelper input.
        device.paste_text(value)
    time.sleep(.12)


def click_label(label, region=None, match='exact'):
    active = desktop.get_foreground_window()
    if desktop.get_window_info(active)['pid'] != qt_pid:
        activate(hwnd)
        active = hwnd
    for attempt in range(2):
        found = desktop.find_text(active, label, match=match, region=region)
        if found is None:
            raise AssertionError('Visible control not found: ' + label)
        activate(active)
        left, top, right, bottom = found['bbox_client']
        try:
            with desktop.Input(active) as device:
                device.click((left+right)//2, (top+bottom)//2)
            break
        except RuntimeError as error:
            if attempt or 'InputPositionMismatch' not in str(error):
                raise
            # The bridge raises before the mouse button is sent. One fresh
            # localization/move is safe; submitted actions are never repeated.
            event('Movement-only retry before any click: '+str(error))
            time.sleep(.25)
    time.sleep(.2)


def shot(name):
    # Capture the visible app frame; native shadows/invisible resize borders are excluded.
    bounds = desktop.get_window_info(hwnd)['capture_bounds']
    frame = desktop.capture_screen(1, region=bounds)
    height, width = frame.image.shape[:2]
    if (width, height) != (1920, 1080):
        report['capture_error'] = {'name':name, 'size':[width,height], 'requested_bounds':bounds,
                                   'window':desktop.get_window_info(hwnd)}
    check((width, height) == (1920, 1080), f'{name}: screenshot is exactly 1920x1080; actual={width}x{height}')
    frame.save(output / (name + '.png'))
    screenshots.append(name + '.png')
    return frame


def search(value):
    event('Search through Ctrl+F and Ctrl+A: '+value)
    activate(hwnd)
    key('ctrl','f')
    key('ctrl','a')
    if value:
        text(value)
    else:
        key('backspace')
    until(lambda: api('preferences')['preferences'].get('searchQuery') == value)
    event('Real persisted search matches the physical keyboard input')


def modal(title):
    return next((w for w in desktop.list_windows() if w['pid']==qt_pid and w['title'].startswith(title)), None)


def popup_checks():
    event('Inspect every shared ComboBox popup through actual mouse and keyboard input')
    key('alt','8')
    for label, region, name, expected in [
        (r'^每\s*1\s*秒$',(800,120,1350,360),'23-interval-popup',('每1秒','每2秒','每5秒')),
        (r'.*32\s*px.*',(900,300,1350,700),'24-density-popup',('紧凑','标准')),
        (r'^100\s*%$',(900,380,1350,750),'25-text-size-popup',('75%','100%','125%','150%','175%','200%')),
    ]:
        click_label(label,region=region,match='regex')
        frame=shot(name)
        words=re.sub(r'\s+','', ' '.join(row['text'] for row in desktop.ocr_frame(frame))).replace('％','%')
        check(all(value in words for value in expected),name+': all real choices are visible in the opened list')
        key('escape')
    key('alt','2')
    click_label('总体使用率',region=(440,100,1000,320))
    frame=shot('26-cpu-mode-popup')
    words=' '.join(row['text'] for row in desktop.ocr_frame(frame.crop((440,190,1020,430))))
    check('总体使用率' in words and '逻辑处理器' in words,'CPU selector exposes both real graph modes')
    key('escape')
    key('alt','1')
    click_label('更多',region=(1550,50,1915,150))
    key('home')
    for _ in range(3):key('down')
    key('enter')
    until(lambda:modal('筛选进程'))
    click_label('全部状态')
    frame=shot('27-filter-state-popup')
    words=' '.join(row['text'] for row in desktop.ocr_frame(frame))
    check(all(value in words for value in ('全部状态','运行中','受限')),'Filter selector exposes all three actual process states')
    key('escape');key('escape')
    until(lambda:not modal('筛选进程'))


def extended_checks():
    event('Extended real keyboard, native menu and settings checks')
    # Open column settings from the real command menu and edit the numeric
    # field as the keyboard alternative to dragging a header boundary.
    click_label('更多', region=(1550,50,1915,150))
    key('home');key('down');key('enter')
    columns = until(lambda:modal('显示列与列宽'))
    activate(columns['hwnd'])
    click_label(r'^250\s*px$', match='regex')
    key('ctrl','a');text('320');key('tab')
    click_label('应用')
    until(lambda:not modal('显示列与列宽'))
    until(lambda:api('preferences')['preferences'].get('pages',{}).get('processes',{}).get('columnWidths',{}).get('name')==320)
    check(True,'Native menu and keyboard numeric editing persist the actual name-column width')
    shot('13-keyboard-column-width')
    # Performance mode has an explicit keyboard-operable selector.
    key('alt','2')
    click_label('总体使用率', region=(440,100,1000,320))
    key('end');key('enter')
    until(lambda:api('preferences')['preferences'].get('pages',{}).get('performance',{}).get('cpuGraphMode')=='logicalProcessors')
    check(True,'Physical keyboard selects logical CPU charts and saves the actual mode')
    shot('14-keyboard-logical-cpu')
    key('home')
    until(lambda:api('preferences')['preferences'].get('pages',{}).get('performance',{}).get('cpuGraphMode')=='total')
    # Performance has exactly Freeze / Collapse sidebar / Source commands.
    click_label('更多', region=(1550,50,1915,150));key('home');key('down');key('enter')
    until(lambda:api('preferences')['preferences'].get('sidebarCollapsed') is True)
    check(True,'The real More menu collapses the sidebar without changing the 1080P window')
    shot('15-sidebar-collapsed')
    click_label('更多', region=(1550,50,1915,150));key('home');key('down');key('enter')
    until(lambda:api('preferences')['preferences'].get('sidebarCollapsed') is False)
    key('alt','8')
    click_label(r'^每\s*1\s*秒$',region=(800,120,1350,360),match='regex')
    key('home');key('down');key('enter')
    until(lambda:api('bootstrap')['settings']['intervalMs']==2000)
    check(True,'Physical settings selector applies a real two-second sample interval')
    shot('16-settings-interval')
    key('home')
    until(lambda:api('bootstrap')['settings']['intervalMs']==1000)
    key('tab');key('space')
    entries=desktop.ocr_window(hwnd,region=(210,120,1850,260))
    check(any('冻结' in row['text'] for row in entries),'Space toggles freeze and exposes the visible frozen-state banner')
    sample=api('snapshot')['sampleId']
    until(lambda:api('snapshot')['sampleId']>sample)
    check(True,'Real sampling continues while physical keyboard freeze is enabled')
    shot('17-keyboard-freeze')
    key('space')
    click_label(r'.*32\s*px.*',region=(900,300,1350,700),match='regex')
    key('end');key('enter')
    until(lambda:api('preferences')['preferences'].get('density')=='standard')
    check(True,'Physical density selector persists standard table density')
    # Scroll within the actual settings content, then cancel the reset dialog.
    heading=desktop.find_text(hwnd,'任务管理器入口',match='exact',region=(210,450,800,950))
    if heading is None:raise AssertionError('Settings scroll target was not visible')
    l,t,r,b=heading['bbox_client']
    with desktop.Input(hwnd) as device:
        device.move_to((l+r)//2,(t+b)//2);device.scroll(-10)
    time.sleep(.4)
    shot('18-settings-bottom')
    click_label(r'^重置界面偏好[.…]*$',match='regex')
    until(lambda:modal('重置界面偏好'))
    shot('19-reset-cancel')
    key('escape')
    until(lambda:not modal('重置界面偏好'))
    check(api('preferences')['preferences'].get('density')=='standard','Esc cancels reset and preserves the actual saved preferences')
    key('alt','1')


temporary = tempfile.TemporaryDirectory(prefix='Takao-Qt-UI-')
try:
    env = dict(os.environ, TASKMANAGER_DATA_DIR=str(Path(temporary.name) / 'preferences'),
               QT_SCALE_FACTOR='1', QT_SCREEN_SCALE_FACTORS='1', TASKMANAGER_QT_1080P='1')
    host_log = (output/'host.log').open('w',encoding='utf-8')
    host = subprocess.Popen([str(Path(args.release) / 'Takao.Host.exe'), f'--port={port}'],
        env=env, cwd=args.release, creationflags=subprocess.CREATE_NO_WINDOW,
        stdout=host_log, stderr=subprocess.STDOUT)
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
        first_prompt = next((w for w in desktop.list_windows() if w['pid'] == qt_pid and w['title'] == '替换标准任务管理器？'), None)
        if first_prompt:
            activate(first_prompt['hwnd'])
            key('escape')
            until(lambda: not any(w['hwnd'] == first_prompt['hwnd'] for w in desktop.list_windows()))
            break
        if api('preferences')['preferences'].get('taskManagerPromptDismissed'):
            break
        time.sleep(.2)
    activate(hwnd)
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
    for index, title in ([] if args.skip_pages else enumerate(titles, 1)):
        activate(hwnd)
        if index > 1:
            key('tab')
            key('space')
        time.sleep(.65)
        shot(f'{index:02d}-page')
        entries = desktop.ocr_window(hwnd, region=(210, 35, 1400, 190))
        check(any(title in item['text'] for item in entries), f'{title}: keyboard navigation renders the page heading')
    # Subsequent interactions are implemented with the same visible native controls.
    progress('Eight pages captured; running task/selection/modal/setting checks')
    activate(hwnd)
    click_label('进程', region=(0, 40, 205, 180))
    search('Takao-no-such-process-1080P')
    time.sleep(.5)
    shot('09-empty-search')
    search('')
    if args.extended:
        popup_checks()
    # A dedicated workload is created through the real Qt run dialog, never through a test RPC.
    before_run_windows = {window['hwnd'] for window in desktop.list_windows()}
    click_label('运行新任务', region=(1000, 50, 1750, 140))
    until(lambda: desktop.get_foreground_window() != hwnd)
    active = desktop.get_foreground_window()
    check(desktop.get_window_info(active)['pid'] == qt_pid, 'run dialog is a native Qt modal')
    if args.file_dialogs:
        click_label(r'^浏览[.…]*$',match='regex')
        until(lambda:modal('选择要运行的程序'))
        shot('21-native-program-picker')
        key('escape')
        until(lambda:not modal('选择要运行的程序'))
        check(bool(modal('运行新任务')),'Esc cancels the native program picker and returns to the Qt run dialog')
        key('alt','p')
    text(str(workload))
    key('tab')
    text('"' + str(Path(temporary.name) / 'workload') + '" 120')
    shot('10-run-dialog')
    key('enter')
    until(lambda: not modal('运行新任务'))
    def owned_target():
        matches = [row for row in api('snapshot')['processes'] if row.get('name') == 'Takao.TestWorkload.exe'
                   and row.get('parentPid') == host.pid and row.get('creationTimeFileTime')]
        return matches[0] if len(matches) == 1 else None
    target = until(owned_target)
    report['owned_target'] = {'pid':target['pid'], 'creationTimeFileTime':target['creationTimeFileTime'],
                              'identity':target['identity']}
    check(target['canTerminate'], 'Qt run action creates an identity-verified dedicated workload')
    # The dedicated console workload may cover the row being tested. Minimize
    # only its newly created, exact executable-caption window; leave it alive.
    for owned_window in desktop.list_windows():
        if owned_window['hwnd'] not in before_run_windows and owned_window['title'] == str(workload):
            desktop.set_window_state(owned_window['hwnd'], 'minimize')
            event('Minimized the newly created dedicated workload console')
    activate(hwnd)
    # The complete unique identity guarantees the UI action cannot select another process.
    search(target['identity'])
    time.sleep(.5)
    click_label(target['name'], region=(210, 130, 900, 320))
    if args.file_dialogs:
        click_label('更多',region=(1550,50,1915,150))
        key('home');key('enter')
        until(lambda:modal('导出当前筛选'))
        export_path=Path(temporary.name)/'owned-export.csv'
        key('alt','n');key('ctrl','a');text(str(export_path))
        shot('22-native-csv-save')
        key('enter')
        until(lambda:export_path.exists() and not modal('导出当前筛选'))
        with export_path.open(encoding='utf-8-sig',newline='') as exported:
            rows=list(csv.reader(exported))
        check(len(rows)==2 and rows[1][0]==target['name'] and
              rows[1][rows[0].index('PID')].replace(',','')==str(target['pid']),
              'The native save dialog exports exactly the identity-filtered real process row')
        report['owned_csv']={'sha256':hashlib.sha256(export_path.read_bytes()).hexdigest(),'dataRows':len(rows)-1}
        click_label(target['name'],region=(210,130,900,350))
    if args.extended:
        key('shift','f10')
        frame=shot('20-process-keyboard-menu')
        check(any('查看详情' in row['text'] for row in desktop.ocr_frame(frame)),
              'Shift+F10 opens the real selected-process menu')
        key('escape')
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
    if args.extended:
        extended_checks()
    key('alt','f4')
    host.wait(timeout=20)
    check(host.returncode == 0, 'closing the Qt window stops its Host cleanly')
    check(not any(window['pid'] == qt_pid for window in desktop.list_windows()), 'no test Qt window remains')
    report['result'] = 'PASS'
except BaseException as error:
    report['result'] = 'FAIL'
    report['error'] = f'{type(error).__name__}: {error}'
    report['traceback'] = traceback.format_exc()
    report['host_exit_code_at_failure'] = host.poll() if host else None
    report['qt_windows_at_failure'] = [w for w in desktop.list_windows() if w['pid']==qt_pid]
    if hwnd:
        try:
            report['failure_window'] = desktop.get_window_info(hwnd)
            bounds = report['failure_window']['capture_bounds']
            if (bounds[2]-bounds[0],bounds[3]-bounds[1]) == (1920,1080):
                frame = desktop.capture_screen(1, region=bounds)
                if frame.image.shape[:2] == (1080,1920):
                    frame.save(output / 'failure.png')
            report['failure_search_query'] = api('preferences')['preferences'].get('searchQuery')
        except Exception:
            pass
    raise
finally:
    if token and host and host.poll() is None:
        try:
            owned = [row for row in api('snapshot')['processes'] if row.get('parentPid')==host.pid
                     and row.get('path','').casefold()==str(workload).casefold() and row.get('creationTimeFileTime')]
            for row in owned:
                api(f'processes/{row["pid"]}/terminate',{'creationTimeFileTime':row['creationTimeFileTime']})
            if owned:
                identities={row['identity'] for row in owned}
                until(lambda:not any(row['identity'] in identities for row in api('snapshot')['processes']),10)
            report['owned_workload_cleanup']='All exact test-Host children at the dedicated workload path have exited'
        except Exception as cleanup_error:
            report['workload_cleanup_error']=str(cleanup_error)
    if host and host.poll() is None:
        try:
            if hwnd:
                desktop.close_window(hwnd)
            host.wait(timeout=15)
        except Exception:
            host.kill()  # Popen's original handle owns only this exact test Host.
            host.wait(timeout=10)
    try:
        temporary.cleanup()
        desktop.set_clipboard_text(original_clipboard or '')
    except Exception as cleanup_error:
        report['cleanup_error']=str(cleanup_error)
        report['result']='FAIL'
    finally:
        if host_log:
            host_log.close()
        report['assertions'] = len(checks)
        progress('Acceptance completed')
if report['result'] != 'PASS':
    raise SystemExit(1)
