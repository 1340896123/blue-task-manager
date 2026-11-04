"""Observe the native component harness through WGC; no OS input is sent."""
import argparse
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import tempfile
import time

parser = argparse.ArgumentParser()
parser.add_argument('--release', required=True)
parser.add_argument('--control-skill', required=True)
parser.add_argument('--output')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(args.control_skill) / 'scripts'))
import computer_control as desktop

output = Path(args.output).resolve() if args.output else root / 'output/qt-acceptance/components'
output.mkdir(parents=True, exist_ok=True)
result_path = output / 'native-results.json'
if result_path.exists():
    result_path.unlink()
captures = []
host = None
with tempfile.TemporaryDirectory(prefix='Takao-Qt-Components-') as temporary:
    fixture = Path(temporary) / 'application'
    shutil.copytree(args.release, fixture)
    shutil.copy2(root / 'output/qt-tests-build/Takao.Qt.Integration.exe', fixture / 'Takao.Qt.exe')
    env = dict(os.environ, TASKMANAGER_DATA_DIR=str(Path(temporary) / 'preferences'),
        TASKMANAGER_QT_COMPONENT_RESULT=str(result_path), TASKMANAGER_QT_1080P='1',
        TASKMANAGER_QT_WORKLOAD=str(root / 'tests/Takao.TestWorkload/bin/Release/net10.0-windows10.0.19041.0/Takao.TestWorkload.exe'),
        TASKMANAGER_QT_TEST_DIRECTORY=str(Path(temporary) / 'owned-data'),
        QT_SCALE_FACTOR='1', QT_SCREEN_SCALE_FACTORS='1')
    try:
        with socket.socket() as reservation:
            reservation.bind(('127.0.0.1', 0))
            port = reservation.getsockname()[1]
        host = subprocess.Popen([str(fixture / 'Takao.Host.exe'), f'--port={port}'],
            cwd=fixture, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
            creationflags=subprocess.CREATE_NO_WINDOW)
        deadline = time.monotonic()+240
        seen = set()
        sized = False
        while time.monotonic() < deadline:
            try:
                report = json.loads(result_path.read_text(encoding='utf-8')) if result_path.exists() else {}
            except (PermissionError, FileNotFoundError, json.JSONDecodeError):
                # QSaveFile atomically replaces this Windows file. Retry only
                # this read if the rename or an antivirus reader holds it briefly.
                time.sleep(.05)
                continue
            phase = report.get('phase', '')
            windows = [w for w in desktop.list_windows() if w['title']=='Takao'
                       and w['process'] and Path(w['process']).parent == fixture]
            if len(windows) == 1 and not sized:
                info = windows[0]
                outer, visible = info['window_rect'], info['capture_bounds']
                desktop.move_resize_window(info['hwnd'],24,24,
                    1920+(outer[2]-outer[0])-(visible[2]-visible[0]),
                    1080+(outer[3]-outer[1])-(visible[3]-visible[1]))
                sized = True
            if phase and phase not in seen and phase != 'completed':
                if len(windows) == 1:
                    hwnd = windows[0]['hwnd']
                    modals = [w for w in desktop.list_windows() if w['pid'] == windows[0]['pid'] and w['title'] != 'Takao']
                    if phase in ('12-run-dialog', '13-process-connections', '14-terminate-confirm') and modals:
                        # WGC excludes owned top-level windows from the parent
                        # capture. Preserve their actual screen coordinates in
                        # an explicit composition of real WGC window frames.
                        # No foreground changes or Windows input are needed.
                        frame = desktop.capture_window(hwnd, client_only=False)
                        for modal in modals:
                            child = desktop.capture_window(modal['hwnd'], client_only=False)
                            x = child.origin_screen[0]-frame.origin_screen[0]
                            y = child.origin_screen[1]-frame.origin_screen[1]
                            left, top = max(0,x), max(0,y)
                            right, bottom = min(1920,x+child.image.shape[1]), min(1080,y+child.image.shape[0])
                            if left < right and top < bottom:
                                frame.image[top:bottom,left:right] = child.image[top-y:bottom-y,left-x:right-x]
                    else:
                        frame = desktop.capture_window(hwnd, client_only=False)
                    if frame.image.shape[:2] != (1080, 1920):
                        raise AssertionError(f'Unexpected 1080P window frame: {frame.image.shape[:2]}')
                    frame.save(output / (phase+'.png'))
                    captures.append(phase+'.png')
                    seen.add(phase)
                    (output / 'captures.json').write_text(json.dumps({'display':'1920x1080 visible window physical pixels, including native title bar','backend':'WGC; owned dialogs explicitly composed at their actual window coordinates','input':'none; internal Qt component methods','screenshots':captures},ensure_ascii=False,indent=2),encoding='utf-8')
            if report.get('result') in ('PASS','FAIL'):
                host.wait(timeout=20)
                if report['result'] != 'PASS' or host.returncode:
                    raise AssertionError(report.get('error','Native component harness failed'))
                expected = {f'{i:02d}-page.png' for i in range(1,9)}
                if not expected.issubset(captures):
                    raise AssertionError('A required 1080P page capture is missing')
                print(json.dumps({'result':'PASS','assertions':len(report['checks']),'screenshots':len(captures),'scope':report['scope']},ensure_ascii=False))
                break
            if host.poll() is not None:
                raise RuntimeError('The component Host exited before producing a result')
            time.sleep(.1)
        else:
            raise TimeoutError('Native component acceptance exceeded four minutes')
    finally:
        if host and host.poll() is None:
            # Close only the exact test application through normal window management.
            for window in desktop.list_windows():
                if window['process'] and Path(window['process']).parent == fixture:
                    desktop.close_window(window['hwnd'])
            try:
                host.wait(timeout=15)
            except subprocess.TimeoutExpired:
                host.kill()
                host.wait(timeout=10)
