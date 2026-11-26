#!/usr/bin/env python3
"""Exercise a packaged Pitstop directly over its stdio MCP transport on a disposable sample copy."""

import argparse
import json
import os
from pathlib import Path
from queue import Empty, Queue
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


def exercise(client, missing_tool, version):
    initialized = client.send('initialize', {'protocolVersion': '2025-11-25', 'capabilities': {},
                                             'clientInfo': {'name': 'pitstop-alpha-smoke', 'version': '1'}})
    actual = initialized['serverInfo']['version']
    if version and actual != version:
        raise RuntimeError(f'Expected Pitstop {version}, got {actual}')
    print(f'Pitstop {actual}', flush=True)
    client.send('notifications/initialized', {}, request=False)
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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('executable', type=Path)
    parser.add_argument('sample', type=Path, help='disposable or preconfigured Hello Modules copy')
    parser.add_argument('state', type=Path, help='new state folder')
    parser.add_argument('--version', help='expected version from MCP initialization')
    parser.add_argument('--missing-tool', action='store_true', help='expect an invalid PITSTOP_XMAKE executable override')
    args = parser.parse_args()
    state = args.state.resolve()
    state.mkdir(parents=True)
    with (state / 'smoke-stderr.log').open('w', encoding='utf-8') as errors:
        client = Client(args.executable.resolve(), args.sample.resolve(), state, errors)
        try:
            exercise(client, args.missing_tool, args.version)
        finally:
            client.close()


if __name__ == '__main__':
    main()
