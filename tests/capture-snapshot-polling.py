"""Capture the real snapshot harness at its explicit checkpoints using WGC."""
import argparse
import json
from pathlib import Path
import sys
import time
import traceback


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', required=True)
    parser.add_argument('--acks', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--control-skill', required=True)
    args = parser.parse_args()
    sys.path.insert(0, str(Path(args.control_skill) / 'scripts'))
    import computer_control as desktop

    report_path, acks, output = Path(args.report), Path(args.acks), Path(args.output)
    acks.mkdir(parents=True, exist_ok=True)
    output.mkdir(parents=True, exist_ok=True)
    result = {'result': 'RUNNING', 'capture': 'WGC, original 1920x1080 visible window pixels',
              'input': 'none; observes native component checkpoints', 'screenshots': []}
    phases = ('processes', 'history', 'performance')
    started = time.monotonic()

    def save():
        result['elapsedSeconds'] = round(time.monotonic() - started, 3)
        (output / 'captures.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')

    try:
        while time.monotonic() - started < 600:
            try:
                report = json.loads(report_path.read_text(encoding='utf-8-sig'))
            except (FileNotFoundError, json.JSONDecodeError):
                time.sleep(.2)
                continue
            phase = report.get('phase')
            if phase in phases and not (acks / (phase + '.ack')).exists():
                qt_pid = report['qtPid']
                matches = [item for item in desktop.list_windows() if item['pid'] == qt_pid and item['title'] == 'Takao']
                if len(matches) != 1:
                    raise AssertionError(f'Expected one exact owned Qt window, found {len(matches)}')
                hwnd = matches[0]['hwnd']
                desktop.set_window_state(hwnd, 'restore')
                info = desktop.get_window_info(hwnd)
                if info['pid'] != qt_pid or info['dpi'] != 96:
                    raise AssertionError('Capture requires the exact owned Qt window at 96 DPI')
                outer, visible = info['window_rect'], info['capture_bounds']
                desktop.move_resize_window(hwnd, 24, 24,
                    1920 + outer[2] - outer[0] - (visible[2] - visible[0]),
                    1080 + outer[3] - outer[1] - (visible[3] - visible[1]))
                # Let the native view process its resize before WGC starts.
                time.sleep(.3)
                frame = desktop.capture_window(hwnd, client_only=False, timeout=15)
                if frame.image.shape[:2] != (1080, 1920):
                    raise AssertionError(f'Unexpected WGC dimensions: {frame.image.shape[:2]}')
                image_path = output / (phase + '-1080p.png')
                frame.save(image_path)
                result['screenshots'].append({'phase': phase, 'path': str(image_path.resolve()), 'qtPid': qt_pid})
                save()
                (acks / (phase + '.ack')).write_text('WGC 1920x1080 captured', encoding='utf-8')
            if report.get('result') in ('PASS', 'FAIL'):
                if report['result'] != 'PASS':
                    raise AssertionError('Native harness failed: ' + report.get('error', 'unknown error'))
                if {item['phase'] for item in result['screenshots']} != set(phases):
                    raise AssertionError('A required WGC checkpoint was not captured')
                result['result'] = 'PASS'
                save()
                print(json.dumps(result, ensure_ascii=False))
                return
            time.sleep(.2)
        raise TimeoutError('The native snapshot harness did not complete within 600 seconds')
    except BaseException as error:
        result.update(result='FAIL', error=f'{type(error).__name__}: {error}', traceback=traceback.format_exc())
        save()
        raise


if __name__ == '__main__':
    main()
