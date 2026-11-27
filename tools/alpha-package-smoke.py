#!/usr/bin/env python3
"""Exercise a packaged Pitstop directly over its stdio MCP transport on a disposable sample copy."""

import argparse
import ctypes
import json
import os
from pathlib import Path
from queue import Empty, Queue
import re
import subprocess
from threading import Thread
import time


class Client:
    def __init__(self, executable, folder, state, errors):
        self.process = subprocess.Popen([str(executable), '--mcp', str(folder)], cwd=folder,
                                        env=dict(os.environ, PITSTOP_SETTINGS=str(state)),
                                        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=errors,
                                        text=True, encoding='utf-8', creationflags=subprocess.CREATE_NO_WINDOW)
        self.messages = Queue()
        self.number = 0

        def receive():
            for line in self.process.stdout:
                self.messages.put(json.loads(line))
            self.messages.put(None)

        Thread(target=receive, daemon=True).start()

    def send(self, method, params, request=True):
        message = {'jsonrpc': '2.0', 'method': method, 'params': params}
        if request:
            self.number += 1
            message['id'] = self.number
        self.process.stdin.write(json.dumps(message) + '\n')
        self.process.stdin.flush()
        if request:
            until = time.monotonic() + 180
            while until > time.monotonic():
                try:
                    reply = self.messages.get(timeout=max(0, until - time.monotonic()))
                except Empty:
                    break
                if reply is None:
                    raise RuntimeError(f'Pitstop exited while answering {method}')
                if reply.get('id') == self.number:
                    if 'error' in reply:
                        raise RuntimeError(reply['error'])
                    return reply['result']
            raise TimeoutError(method)

    def call(self, name, **arguments):
        answer = self.send('tools/call', {'name': name, 'arguments': arguments})
        text = '\n'.join(item['text'] for item in answer['content'] if item['type'] == 'text')
        if answer.get('isError'):
            raise RuntimeError(f'{name}: {text}')
        try:
            return json.loads(text)
        except ValueError:
            return text

    def close(self):
        self.process.stdin.close()
        try:
            if self.process.wait(30):
                raise RuntimeError(f'Pitstop exited {self.process.returncode}')
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait()
            raise


def exercise(client, missing_tool, version, native_product):
    initialized = client.send('initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
                                             'clientInfo': {'name': 'pitstop-alpha-smoke', 'version': '1'}})
    actual = initialized['serverInfo']['version']
    if version and actual != version:
        raise RuntimeError(f'Expected Pitstop {version}, got {actual}')
    print(f'Pitstop {actual}', flush=True)
    client.send('notifications/initialized', {}, request=False)
    if native_product:
        available_action = 'app.native.newProject'
        if available_action not in str(client.call('ide_actions', query=available_action)):
            raise RuntimeError('The native New Project action is missing')
        for action in ('app.restartCSharpServer', 'app.debug.applyHotReloadChanges', 'app.tests.run',
                       'app.explorer.manageUserSecrets', 'app.dotnet.installWorkloads', 'app.android.install',
                       'app.mobile.makeEdits', 'app.mobile.keepAssembliesWhole'):
            if action in str(client.call('ide_actions', query=action)):
                raise RuntimeError(f'The native product exposes the managed action {action}')
        print('Native action isolation verified', flush=True)
    until = time.monotonic() + 150
    while time.monotonic() < until:
        workspace = client.call('module_workspace', target='greetings')
        if 'modules' in workspace or (missing_tool and 'PITSTOP_XMAKE' in workspace.get('status', '')):
            break
        time.sleep(0.5)
    else:
        raise TimeoutError(f'Expected module workspace. Last response: {workspace}')
    print(json.dumps(workspace), flush=True)
    if missing_tool:
        if 'modules' in workspace:
            raise RuntimeError('The missing-tool probe resolved a workspace')
        return
    if not any(module['name'] == 'greetings' for module in workspace['modules']):
        raise RuntimeError('The expected greetings module was not indexed')
    for action in ('app.modules.explorer', 'app.modules.graph'):
        print(client.call('ide_action', action=action), flush=True)
    print(client.call('open', path='main.cpp', line=1, column=10), flush=True)
    print(client.call('ide_action', action='app.modules.navigate'), flush=True)
    print(client.call('edit', path='main.cpp', oldText='std::cout << greeting()', newText='std::cout << (greeting())'), flush=True)
    build = client.call('build')
    print(json.dumps(build), flush=True)
    if not build['succeeded']:
        raise RuntimeError('Sample build failed')
    run = client.call('run', configuration='Xmake: hello-modules', waitSeconds=30)
    print(json.dumps(run), flush=True)
    if run.get('exitCode') != 0 or 'Hello, modules!' not in str(run):
        raise RuntimeError('Sample run did not print the greeting and exit zero')


def debug_modules(client):
    inspect = 'native_debug_inspect'
    control = 'native_debug_control'
    available = client.call(control, operation='validate')
    if available.get('status') != 'available':
        raise RuntimeError(f'Packaged LLDB discovery failed: {available}')
    placed = client.call(control, operation='set_breakpoint', path='greetings-implementation.cpp', line=4)
    if placed.get('status') != 'set':
        raise RuntimeError('The packaged debugger did not accept the source breakpoint')
    started_at = time.perf_counter()
    started = client.call(control, operation='start', configuration='Xmake: hello-modules')
    session = started.get('sessionId')
    if not session:
        raise RuntimeError(f'The packaged debugger did not start: {started}')
    stopped_at = None
    try:
        stopped = client.call(inspect, operation='wait', sessionId=session, waitSeconds=30)
        if stopped.get('state') != 'Paused':
            raise RuntimeError(f'The packaged debugger did not reach the source breakpoint: {stopped}')
        first_stop_ms = round((time.perf_counter() - started_at) * 1000)
        frames = client.call(inspect, operation='frames', sessionId=session, limit=20).get('frames', [])
        if not any(frame.get('source') == 'greetings-implementation.cpp' for frame in frames):
            raise RuntimeError('The packaged debugger did not report the module implementation source')
        current = client.call(inspect, operation='state', sessionId=session)
        scopes = client.call(inspect, operation='scopes', sessionId=session, stop=current['stop'], reference=frames[0]['id']).get('scopes', [])
        if not scopes:
            raise RuntimeError('The packaged debugger did not report scopes')
        stopped_at = time.perf_counter()
    finally:
        current = client.call(inspect, operation='state', sessionId=session)
        if current.get('state') != 'Ended':
            ended = client.call(control, operation='stop', sessionId=session)
            if stopped_at is not None and ended.get('state') != 'Ended':
                raise RuntimeError(f'The packaged debugger did not release its session: {ended}')
    print(json.dumps({'nativeDebug': 'passed', 'adapter': available['adapter'], 'firstStopMs': first_stop_ms,
                      'shutdownMs': round((time.perf_counter() - stopped_at) * 1000)}), flush=True)


def measure_index(client, state, count, version):
    hello = client.send('initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
                                       'clientInfo': {'name': 'pitstop-alpha-index', 'version': '1'}})
    actual = hello['serverInfo']['version']
    if version and actual != version:
        raise RuntimeError(f'Expected Pitstop {version}, got {actual}')
    client.send('notifications/initialized', {}, request=False)
    until = time.monotonic() + 150
    while time.monotonic() < until:
        if client.call('module_workspace', limit=1).get('complete'):
            break
        time.sleep(0.25)
    else:
        raise TimeoutError('No complete module snapshot for the indexed scale workspace')

    sources = {'main.cpp', *(f'm{index}.cppm' for index in range(count))}
    opened = time.perf_counter()
    client.call('open', path='main.cpp', line=1, column=1)
    initialized = None
    times = {}
    until = time.monotonic() + 240
    while time.monotonic() < until:
        for log in (state / 'logs/clangd').glob('server-*.log'):
            for line in log.read_text(encoding='utf-8', errors='replace').splitlines():
                if '<-- initialized' in line and initialized is None:
                    initialized = time.perf_counter() - opened
                if match := re.search(r'^I\[(\d\d):(\d\d):(\d\d\.\d+)\] Indexed (.+?) \(\d+ symbols', line):
                    stamp = int(match[1]) * 3600 + int(match[2]) * 60 + float(match[3])
                    source = Path(match[4]).name
                    if source in sources:
                        times.setdefault(source, stamp)
            if times.keys() == sources:
                pid = int(log.stem.rsplit('-', 1)[1])
                return {'sourcesIndexed': len(times), 'openToInitializeSeconds': initialized,
                        'openToAllIndexedSeconds': time.perf_counter() - opened,
                        'indexLogSpanSeconds': (times[next(reversed(times))] - times[next(iter(times))]) % 86400,
                        'ideMemory': working_set(client.process.pid), 'clangdMemory': working_set(pid)}
        time.sleep(0.25)
    raise TimeoutError(f'clangd logged indexing {len(times)} of {len(sources)} workspace sources')


def working_set(pid):
    class Counters(ctypes.Structure):
        _fields_ = [('size', ctypes.c_uint32), ('faults', ctypes.c_uint32)] + [
            (name, ctypes.c_size_t) for name in ('peak', 'working', 'quota_peak_paged', 'quota_paged',
                                                'quota_peak_nonpaged', 'quota_nonpaged', 'pagefile', 'pagefile_peak')]

    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.argtypes = [ctypes.c_uint32, ctypes.c_int, ctypes.c_uint32]
    kernel.OpenProcess.restype = ctypes.c_void_p
    kernel.CloseHandle.argtypes = [ctypes.c_void_p]
    query = ctypes.WinDLL('psapi', use_last_error=True).GetProcessMemoryInfo
    query.argtypes = [ctypes.c_void_p, ctypes.POINTER(Counters), ctypes.c_uint32]
    handle = kernel.OpenProcess(0x410, False, pid)
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        counters = Counters(ctypes.sizeof(Counters))
        if not query(handle, ctypes.byref(counters), counters.size):
            raise ctypes.WinError(ctypes.get_last_error())
        return {'workingSetBytes': counters.working, 'peakWorkingSetBytes': counters.peak}
    finally:
        kernel.CloseHandle(handle)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('executable', type=Path)
    parser.add_argument('sample', type=Path, help='disposable, configured Hello Modules copy outside Git worktrees')
    parser.add_argument('state', type=Path, help='new state folder')
    parser.add_argument('--version', help='expected version from MCP initialization')
    parser.add_argument('--native-product', action='store_true', help='verify the native package action inventory')
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--missing-tool', action='store_true', help='expect an unavailable Xmake executable')
    mode.add_argument('--scale-index', type=int, help='measure clangd indexing of a generated scale workspace with this many modules and no partitions')
    mode.add_argument('--native-debug', action='store_true', help='debug the configured Hello Modules copy using the installed reference lldb-dap')
    args = parser.parse_args()
    if args.scale_index is not None and args.scale_index < 1:
        parser.error('--scale-index requires a positive module count')
    state = args.state.resolve()
    sample = args.sample.resolve()
    if any((directory / '.git').exists() for directory in (sample, *sample.parents)):
        parser.error('configure a disposable sample copy outside Git worktrees so the smoke test trusts only that copy')
    state.mkdir(parents=True)
    (state / 'trust.json').write_text(json.dumps({'version': 1, 'trusted': [str(sample)]}), encoding='utf-8')
    with (state / 'smoke-stderr.log').open('w', encoding='utf-8') as errors:
        client = Client(args.executable.resolve(), sample, state, errors)
        try:
            if args.scale_index is not None:
                print(json.dumps(measure_index(client, state, args.scale_index, args.version), indent=2), flush=True)
            else:
                exercise(client, args.missing_tool, args.version, args.native_product)
                if args.native_debug:
                    debug_modules(client)
        finally:
            client.close()


if __name__ == '__main__':
    main()
