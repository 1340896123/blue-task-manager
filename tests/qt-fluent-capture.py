"""Capture the actual Fluent control tests with WGC, without sending OS input."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import time

parser = argparse.ArgumentParser()
parser.add_argument('--release', required=True)
parser.add_argument('--executable', required=True)
parser.add_argument('--output', required=True)
parser.add_argument('--control-skill', default=str(Path.home() / '.codex/skills/windows-computer-control'))
args = parser.parse_args()
sys.path.insert(0, str(Path(args.control_skill) / 'scripts'))
import computer_control as desktop

release = Path(args.release).resolve()
output = Path(args.output).resolve()
output.mkdir(parents=True, exist_ok=True)
result = output / 'results.json'
result.unlink(missing_ok=True)
acknowledgement = Path(str(result) + '.captured')
acknowledgement.unlink(missing_ok=True)
env = dict(os.environ, TAKAO_FLUENT_RESULT=str(result), QT_SCALE_FACTOR='1',
           QT_SCREEN_SCALE_FACTORS='1', QT_PLUGIN_PATH=str(release),
           QT_QPA_PLATFORM_PLUGIN_PATH=str(release / 'platforms'))
env['PATH'] = str(release) + os.pathsep + env.get('PATH', '')
process = subprocess.Popen([str(Path(args.executable).resolve())], env=env,
                           creationflags=subprocess.CREATE_NO_WINDOW)
seen = set()
sized = False
try:
    deadline = time.monotonic() + 100
    while time.monotonic() < deadline:
        try:
            report = json.loads(result.read_text(encoding='utf-8')) if result.exists() else {}
        except json.JSONDecodeError:
            time.sleep(.05)
            continue
        if report.get('result') in ('PASS', 'FAIL'):
            process.wait(timeout=10)
            if report['result'] != 'PASS' or process.returncode:
                raise AssertionError(report.get('error', report.get('dialogError', 'Control tests failed')))
            expected = {'01-controls', '02-confirm-cancel', '03-confirm-accept', '04-alert',
                        '05-dropdown', '06-dropdown-scroll'}
            if seen != expected:
                raise AssertionError(f'Missing WGC control captures: {expected - seen}')
            print(json.dumps({'result': 'PASS', 'checks': len(report['checks']), 'screenshots': len(seen)}))
            break
        windows = [window for window in desktop.list_windows() if window['pid'] == process.pid]
        main = next((window for window in windows if window['title'] == 'Takao Fluent 组件'), None)
        if main and not sized:
            outer, visible = main['window_rect'], main['capture_bounds']
            desktop.move_resize_window(main['hwnd'], 24, 24,
                1920 + outer[2] - outer[0] - visible[2] + visible[0],
                1080 + outer[3] - outer[1] - visible[3] + visible[1])
            sized = True
        phase = report.get('phase')
        if main and phase and phase not in seen:
            if report.get('popupHwnd'):
                popup = desktop.get_window_info(int(report['popupHwnd']))
                if popup['pid'] != process.pid:
                    raise AssertionError('Popup window does not belong to the component test')
                if not any(window['hwnd'] == popup['hwnd'] for window in windows):
                    windows.append(popup)
            frame = desktop.capture_window(main['hwnd'], client_only=False)
            for modal in windows:
                if modal['hwnd'] == main['hwnd']:
                    continue
                child = desktop.capture_window(modal['hwnd'], client_only=False)
                x = child.origin_screen[0] - frame.origin_screen[0]
                y = child.origin_screen[1] - frame.origin_screen[1]
                left, top = max(0, x), max(0, y)
                right = min(1920, x + child.image.shape[1])
                bottom = min(1080, y + child.image.shape[0])
                if left < right and top < bottom:
                    frame.image[top:bottom, left:right] = child.image[top-y:bottom-y, left-x:right-x]
            if frame.image.shape[:2] != (1080, 1920):
                raise AssertionError(f'Expected actual 1920x1080 frame, got {frame.image.shape[:2]}')
            frame.save(output / (phase + '.png'))
            seen.add(phase)
            (output / 'captures.json').write_text(json.dumps({
                'display': '1920x1080 actual visible physical window, 100%',
                'capture': 'WGC; owned modals composed at their actual window positions',
                'input': 'none; component methods are not physical keyboard or mouse tests',
                'screenshots': sorted(seen)}, ensure_ascii=False, indent=2), encoding='utf-8')
            acknowledgement.write_text(phase, encoding='utf-8')
        if process.poll() is not None:
            raise RuntimeError(f'Control test exited early: {process.returncode}')
        time.sleep(.1)
    else:
        raise TimeoutError('Fluent control tests exceeded 100 seconds')
finally:
    if process.poll() is None:
        for window in desktop.list_windows():
            if window['pid'] == process.pid:
                desktop.close_window(window['hwnd'])
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)
