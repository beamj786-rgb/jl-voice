"""Opt-in Windows full-process-family monitor; no microphone or text access."""
import argparse
import ctypes
import json
import pathlib
import platform
import subprocess
import tempfile
import time
import psutil

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--app', required=True, type=pathlib.Path)
    parser.add_argument('--seconds', type=int, default=30)
    parser.add_argument('--scenario', choices=['idle', 'dictation-with-desktop-activity'], default='idle')
    parser.add_argument('--output', required=True, type=pathlib.Path)
    args = parser.parse_args()
    if platform.system() != 'Windows': parser.error('Real Windows execution is required')
    if args.seconds < 10: parser.error('Monitor for at least 10 seconds')
    app = args.app.resolve(strict=True)
    with tempfile.TemporaryDirectory(prefix='jl-voice-benchmark-') as temp:
        log = pathlib.Path(temp) / 'events.jsonl'
        start = time.perf_counter()
        p = subprocess.Popen([str(app), '--diagnostics', str(log)], cwd=app.parent)
        owned = psutil.Process(p.pid)
        rows, seen_cpu = [], {}
        ready_ms = None
        try:
            while time.perf_counter() - start < args.seconds:
                if p.poll() is not None: raise RuntimeError('App exited early (another instance, startup failure or manual exit)')
                family = [owned] + owned.children(recursive=True)
                rss = 0
                for process in family:
                    try:
                        rss += process.memory_info().rss
                        seen_cpu[(process.pid, process.create_time())] = sum(process.cpu_times()[:2])
                    except psutil.Error:
                        pass
                elapsed = time.perf_counter() - start
                rows.append(dict(seconds=elapsed, rss_bytes=rss, cpu_seconds=sum(seen_cpu.values())))
                if log.exists() and ready_ms is None:
                    for line in log.read_text(encoding='utf-8').splitlines():
                        try:
                            if json.loads(line)['name'] == 'ready': ready_ms = elapsed * 1000
                        except json.JSONDecodeError: pass
                time.sleep(0.05)
            events = [json.loads(line) for line in log.read_text(encoding='utf-8').splitlines()]
            if ready_ms is None: raise RuntimeError('No startup readiness event observed')
            pending, insertion_times = None, []
            for event in events:
                if event['name'] == 'recording_stop_requested': pending = event['elapsed_ms']
                elif event['name'] == 'text_insertion_sent' and pending is not None:
                    insertion_times.append(event['elapsed_ms'] - pending); pending = None
                elif event['name'] in ('failed', 'cancelled'): pending = None
            steady = [r for r in rows if r['seconds'] > max(5, ready_ms / 1000)]
            delta = steady[-1]['seconds'] - steady[0]['seconds'] if len(steady) > 1 else 0
            report = dict(platform=platform.platform(), logical_cpus=psutil.cpu_count(),
                          physical_ram_bytes=psutil.virtual_memory().total, scenario=args.scenario,
                          observed_launch_to_ready_ms=ready_ms,
                          peak_sampled_family_rss_bytes=max(r['rss_bytes'] for r in rows),
                          steady_mean_family_rss_bytes=sum(r['rss_bytes'] for r in steady) / len(steady) if steady else None,
                          steady_cpu_percent_one_core=100*(steady[-1]['cpu_seconds']-steady[0]['cpu_seconds'])/delta if delta else None,
                          end_recording_to_input_dispatch_ms=insertion_times,
                          events=events, measurements=rows, minimum_hardware_accepted=False,
                          limitations=['RSS sums working sets and may double-count shared pages; all owned processes included.',
                                       '50ms sampling can miss short peaks and process exits.',
                                       'Startup includes readiness polling error up to about 50ms; OS cache state is uncontrolled.',
                                       'Input dispatch does not prove the target application accepted every character.',
                                       'Desktop responsiveness and transcript accuracy require separate review; this does not measure them.'])
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(json.dumps(report, indent=2), encoding='utf-8')
        finally:
            # Owned process only; WM_CLOSE allows normal worker/audio cleanup first.
            callback = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
            def close_window(hwnd, _):
                pid = ctypes.c_ulong()
                ctypes.windll.user32.GetWindowThreadProcessId(ctypes.c_void_p(hwnd), ctypes.byref(pid))
                if pid.value == p.pid: ctypes.windll.user32.PostMessageW(ctypes.c_void_p(hwnd), 0x10, 0, 0)
                return True
            ctypes.windll.user32.EnumWindows(callback(close_window), 0)
            try: p.wait(timeout=10)
            except subprocess.TimeoutExpired:
                for child in owned.children(recursive=True): child.kill()
                p.kill(); p.wait()

if __name__ == '__main__':
    main()
