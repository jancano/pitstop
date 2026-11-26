#!/usr/bin/env python3
"""Generate a disposable Xmake module workspace and optionally measure its native tools."""

import argparse
import json
import os
from pathlib import Path
import platform
import re
import subprocess
import time


def generate(folder, count, targets, partitions, topology):
    folder.mkdir(parents=True)
    project = ['set_project("pitstop-module-scale")', 'set_languages("cxx23")',
               'set_policy("build.c++.modules", true)']
    for target in range(targets):
        project.extend([f'target("group{target}")', '    set_kind("static")'])
        if target > 0 and topology != 'independent':
            project.append(f'    add_deps("group{target - 1}")')
        for module in range(target * count // targets, (target + 1) * count // targets):
            name = f'scale.m{module}'
            contents = [f'export module {name};']
            for part in range(partitions):
                filename = f'm{module}-p{part}.cppm'
                (folder / filename).write_text(f'export module {name}:p{part};\n', encoding='utf-8')
                contents.append(f'export import :p{part};')
                project.append(f'    add_files("{filename}", {{public = true}})')
            if module and topology != 'independent':
                dependency = module - 1 if topology == 'chain' else 0
                contents.append(f'import scale.m{dependency};')
            contents.append(f'export constexpr int value{module}() {{ return {module}; }}')
            filename = f'm{module}.cppm'
            (folder / filename).write_text('\n'.join(contents) + '\n', encoding='utf-8')
            project.append(f'    add_files("{filename}", {{public = true}})')
    project.extend(['target("scale-app")', '    set_kind("binary")', f'    add_deps("group{targets - 1}")',
                    '    add_files("main.cpp")'])
    (folder / 'xmake.lua').write_text('\n'.join(project) + '\n', encoding='utf-8')
    (folder / 'main.cpp').write_text(f'import scale.m{count - 1};\nint main() {{ return value{count - 1}() != {count - 1}; }}\n', encoding='utf-8')


def measure(folder, results, xmake, llvm, targets, timeout):
    results.mkdir()
    bin_dir = llvm / 'bin'
    suffix = '.exe' if platform.system() == 'Windows' else ''
    scanner = bin_dir / ('clang-scan-deps' + suffix)
    env = dict(os.environ, XMAKE_STATS='false', XMAKE_COLORTERM='nocolor',
               PATH=f'{bin_dir}{os.pathsep}{os.environ["PATH"]}')
    stages = {}

    def run(stage, exe, *args):
        start = time.perf_counter()
        try:
            with (results / f'{stage}.out').open('wb') as output, (results / f'{stage}.err').open('wb') as errors:
                done = subprocess.run([str(exe), *map(str, args)], cwd=folder, env=env, stdout=output, stderr=errors,
                                      timeout=timeout, check=False)
            exit_code = done.returncode
        except subprocess.TimeoutExpired:
            exit_code = 'timed out'
        stages[stage] = {'seconds': round(time.perf_counter() - start, 3), 'exit': exit_code}
        print(stage, stages[stage], flush=True)
        return exit_code == 0

    def evaluate():
        commands = [('configuration', 'show', '--format=json'), ('targets', 'show', '-l', 'targets', '--format=json'),
                    ('target-graph', 'show', '--info=depgraph', '--format=json')]
        commands.extend((f'target-{target}', 'show', f'--target={target}', '--format=json')
                        for target in [*(f'group{n}' for n in range(targets)), 'scale-app'])
        start = time.perf_counter()
        ok = all(run(label, xmake, *args) for label, *args in commands)
        stages['metadata-total'] = {'seconds': round(time.perf_counter() - start, 3), 'exit': 0 if ok else 1}
        return ok

    supported = run('configure', xmake, 'f', '-m', 'debug', '-p', 'mingw', '--toolchain=llvm', f'--mingw={llvm}',
                    f'--sdk={llvm}', '--require=no', '-y')
    if supported and evaluate() and run('database', xmake, 'project', '-k', 'compile_commands', '--lsp=clangd', results):
        rows = json.loads((results / 'compile_commands.json').read_text(encoding='utf-8-sig'))
        for row in rows:
            flags = row['arguments']
            row['output'] = flags[flags.index('-o') + 1]
            row['arguments'] = ['-isystem' if flag == '-imsvc' else flag for flag in flags if not flag.startswith('-fmodule-file=')]
        derived = results / 'scan-commands.json'
        derived.write_text(json.dumps(rows), encoding='utf-8')
        run('p1689', scanner, '-format=p1689', '-j', '2', f'-compilation-database={derived}')
    (results / 'timings.json').write_text(json.dumps(stages, indent=2) + '\n', encoding='utf-8')
    return all(stage['exit'] == 0 for stage in stages.values())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder', type=Path, help='new workspace directory')
    parser.add_argument('--modules', type=int, default=10, help='primary interface count')
    parser.add_argument('--targets', type=int, default=2, help='static library count, plus one executable')
    parser.add_argument('--partitions', type=int, default=0, help='exported partitions per module')
    parser.add_argument('--topology', choices=['independent', 'chain', 'fanout'], default='fanout')
    parser.add_argument('--xmake', type=Path, help='automatically configure and measure only the newly generated project')
    parser.add_argument('--llvm', type=Path, help='LLVM-MinGW SDK directory for --xmake')
    parser.add_argument('--results', type=Path, help='new results directory for --xmake')
    parser.add_argument('--timeout', type=float, default=120, help='maximum seconds per native command')
    args = parser.parse_args()
    if not 1 <= args.targets <= args.modules or args.partitions < 0 or args.timeout <= 0:
        parser.error('set at least one module per target, nonnegative partitions and a positive timeout')
    if any((args.xmake, args.llvm, args.results)) and not all((args.xmake, args.llvm, args.results)):
        parser.error('--xmake, --llvm and --results are required together')
    folder = args.folder.resolve()
    generate(folder, args.modules, args.targets, args.partitions, args.topology)
    if args.xmake and not measure(folder, args.results.resolve(), args.xmake.resolve(), args.llvm.resolve(), args.targets, args.timeout):
        return 1
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
