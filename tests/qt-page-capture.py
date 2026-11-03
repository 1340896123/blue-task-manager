"""Fast, acknowledged WGC page captures. Never opens an input device or uses OCR."""
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
import uuid


def atomic_json(path, value):
    temporary = path.with_name(path.name + '.' + uuid.uuid4().hex + '.tmp')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')
    temporary.replace(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release', required=True)
    parser.add_argument('--executable', required=True)
    parser.add_argument('--control-skill', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--timeout', type=float, default=100)
    args = parser.parse_args()
    if not 10 <= args.timeout <= 600:
        parser.error('--timeout must be between 10 and 600 seconds')
    started = time.monotonic()
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    run_id = time.strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:8]
    run_output = output / run_id
    run_output.mkdir()
    captures = []
    report = {'result': 'RUNNING', 'runId': run_id, 'runDirectory': str(run_output),
              'scope': 'Native Qt component navigation + real Host/HTTP/JSON.NET/CsWin32',
              'capture': 'WGC; actual window frame, no stretching',
              'input': 'none; QPushButton.click() in the native test driver, no mouse/keyboard/OCR',
              'screenshots': captures}
    host = None
    native = {}
    desktop = None

    def save():
        report['elapsedSeconds'] = round(time.monotonic() - started, 3)
        atomic_json(run_output / 'results.json', report)
        atomic_json(output / 'results.json', report)
        manifest = {'runId': run_id, 'result': report['result'], 'capture': report['capture'],
                    'input': report['input'], 'screenshots': captures}
        atomic_json(run_output / 'captures.json', manifest)
        atomic_json(output / 'captures.json', manifest)

    save()
    try:
        sys.path.insert(0, str(Path(args.control_skill).resolve() / 'scripts'))
        import computer_control as desktop
        release = Path(args.release).resolve()
        executable = Path(args.executable).resolve()
        if not (release / 'Takao.Host.exe').is_file() or not executable.is_file():
            raise FileNotFoundError('Build the published Host and native page-capture driver first')
        report['primaryResolution'] = desktop.get_primary_resolution()
        with tempfile.TemporaryDirectory(prefix='Takao-Qt-Pages-') as temporary:
            temporary = Path(temporary)
            fixture = temporary / 'application'
            shutil.copytree(release, fixture)
            shutil.copy2(executable, fixture / 'Takao.Qt.exe')
            profile = temporary / 'preferences'
            profile.mkdir()
            # Test-only UI preferences suppress the integration offer. No
            # business data, user preferences, IFEO or OS settings are changed.
            atomic_json(profile / 'preferences.json', {
                'version': 1, 'intervalMs': 1000,
                'ui': {'version': 1, 'taskManagerPromptDismissed': True}})
            progress = temporary / 'native-progress.json'
            acknowledgements = temporary / 'acknowledged-pngs'
            acknowledgements.mkdir()
            env = dict(os.environ, TASKMANAGER_DATA_DIR=str(profile),
                       TASKMANAGER_QT_PAGE_PROGRESS=str(progress),
                       TASKMANAGER_QT_PAGE_ACK_DIRECTORY=str(acknowledgements),
                       QT_SCALE_FACTOR='1', QT_SCREEN_SCALE_FACTORS='1')
            env.pop('TASKMANAGER_QT_1080P', None)
            with socket.socket() as reservation:
                reservation.bind(('127.0.0.1', 0))
                port = reservation.getsockname()[1]
            try:
                host = subprocess.Popen([str(fixture / 'Takao.Host.exe'), f'--port={port}'],
                                        cwd=fixture, env=env, stdout=subprocess.DEVNULL,
                                        stderr=subprocess.DEVNULL, creationflags=subprocess.CREATE_NO_WINDOW)
                report['ownedHostPid'] = host.pid
                save()
                deadline = time.monotonic() + args.timeout
                seen = set()
                while time.monotonic() < deadline:
                    if progress.is_file():
                        native = json.loads(progress.read_text(encoding='utf-8'))
                        atomic_json(run_output / 'native-results.json', native)
                    phase = native.get('phase', '')
                    if native.get('result') == 'FAIL':
                        raise RuntimeError(native.get('error', 'Native page-capture driver failed'))
                    if native.get('result') == 'PASS':
                        host.wait(timeout=15)
                        report['hostExitCode'] = host.returncode
                        if host.returncode or len(captures) != 8:
                            raise RuntimeError('Host exit or the required eight captures failed')
                        report['nativeElapsedMs'] = native['elapsedMs']
                        report['nativeChecks'] = len(native['checks'])
                        report['result'] = 'PASS'
                        break
                    if phase not in ('', 'starting', 'completed') and phase not in seen:
                        capture_started = time.monotonic()
                        hwnd = int(native['hwnd'])
                        info = desktop.get_window_info(hwnd)
                        if info['pid'] != native['qtPid'] or Path(info['process']).parent != fixture:
                            raise RuntimeError('Capture target does not belong to this private test application')
                        if info['dpi'] != 96 or native.get('devicePixelRatio') != 1:
                            raise RuntimeError('Fast page capture requires actual 100% / 96 DPI')
                        frame = desktop.capture_window(hwnd, client_only=False, timeout=15)
                        image = run_output / (phase + '.png')
                        pending_image = run_output / (phase + '.partial.png')
                        frame.save(pending_image)
                        pending_image.with_suffix('.json').replace(image.with_suffix('.json'))
                        pending_image.replace(image)
                        captures.append({'phase': phase, 'title': native['title'], 'path': str(image),
                                         'width': frame.image.shape[1], 'height': frame.image.shape[0],
                                         'clientWidth': native['clientWidth'], 'clientHeight': native['clientHeight'],
                                         'windowRect': info['window_rect'], 'captureBounds': info['capture_bounds'],
                                         'dpi': info['dpi'], 'elapsedSeconds': round(time.monotonic() - capture_started, 3)})
                        seen.add(phase)
                        save()
                        # The unique private PNG is the completion acknowledgement.
                        # Publish it only after the actual output PNG and metadata.
                        pending_ack = acknowledgements / (phase + '.partial.png')
                        shutil.copy2(image, pending_ack)
                        pending_ack.replace(acknowledgements / (phase + '.png'))
                    if host.poll() is not None:
                        # The child may finish between the progress read and
                        # this exit observation; read its final atomic result.
                        if progress.is_file():
                            native = json.loads(progress.read_text(encoding='utf-8'))
                            if native.get('result') in ('PASS', 'FAIL'):
                                continue
                        raise RuntimeError(f'Owned Host exited before completion: {host.returncode}')
                    time.sleep(.025)
                else:
                    raise TimeoutError(f'Eight-page capture exceeded {args.timeout:g} seconds')
            finally:
                if host and host.poll() is None:
                    if native.get('hwnd'):
                        try:
                            info = desktop.get_window_info(int(native['hwnd']))
                            if info['pid'] == native.get('qtPid') and Path(info['process']).parent == fixture:
                                desktop.close_window(info['hwnd'])
                        except (OSError, RuntimeError):
                            pass
                    try:
                        host.wait(timeout=8)
                    except subprocess.TimeoutExpired:
                        # The production Host job owns its exact Qt child.
                        # Never kill processes by name or touch another window.
                        host.kill()
                        host.wait(timeout=10)
                        report['forcedOwnedHostCleanup'] = True
                    report['hostExitCode'] = host.returncode
    except Exception as error:
        report['result'] = 'FAIL'
        report['error'] = f'{type(error).__name__}: {error}'
    finally:
        if report['result'] == 'RUNNING':
            report['result'] = 'FAIL'
            report['error'] = 'Capture was interrupted before completion'
        save()
    print(json.dumps({'result': report['result'], 'screenshots': len(captures),
                      'elapsedSeconds': report['elapsedSeconds'], 'output': str(run_output),
                      'error': report.get('error')}, ensure_ascii=False))
    return 0 if report['result'] == 'PASS' else 1


if __name__ == '__main__':
    raise SystemExit(main())
