# Pitstop 0.2.0-alpha.1 release readiness

The alpha candidate targets Windows x64 with Xmake and Xrepo 3.1.2, LLVM-MinGW 23.1.6 with libc++, clangd and clang-scan-deps 23.1.6. Publication remains blocked by unresolved regressions and candidate-wide headless verification. The [release notes](../release-notes/0.2.0-alpha.1.md) describe the candidate packages and limitations.

## Repository audit

Phase 5 was reviewed and published as `869090ee0`. The diff preserved the MIT license and third-party notices. The notice checker found all 511 packages accounted for. The changed files were checked for workstation paths, credentials and generated output. The remaining Passenger identifiers in the solution, assemblies, inherited test fixtures and historical notes are intentional. Source tools run through `ChildProcesses` after trust checks, with cancellation on obsolete module generations and cleanup of their Pitstop temporary inputs. A replacement scan waits for the canceled scanner to finish cleanup. Scanning uses the evaluated configuration and the private compilation database copy. The user's compilation database is preserved.

The repository has two workflows. CI is started manually with read-only repository contents permissions. It can upload workflow artifacts. Automatic update lookup requires an explicit `PITSTOP_RELEASES` override at runtime. The default in `ReleaseFeed.Default` is disabled, and the packager invokes Velopack with `--skip-updates`. GitHub Issues is publicly visible and the Releases page had no published releases during the audit. [Issues](https://github.com/jancano/pitstop/issues) · [Releases](https://github.com/jancano/pitstop/releases)

## Regression validation

Run the following with the [reference toolchain environment](module-intelligence.md#reproduce). The runner fixes VSTest's output language because discovery and hang reporting read its English output.

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet restore Passenger.slnx --locked-mode
just build
python -I tools/test-run.py --processes 6
dotnet test tests/Passenger.Tests --no-build --filter 'FullyQualifiedName~CppNativeLanguageTests|FullyQualifiedName~ModuleIntelligenceTests|FullyQualifiedName~XmakeNativeBuildRunTests|FullyQualifiedName~XmakeNativeExplorerTests|FullyQualifiedName~McpModuleWorkspaceTests'
```

Locked restore succeeded, and the complete solution built with zero warnings and errors. The UI and unit reruns used the source revision packaged below.

The full unfiltered run discovers unit and headless tests and writes per-process logs and TRX reports in `artifacts/test-results/local/`. The Phase 5 baseline run discovered 1,639 test entries in a detached checkout. The first attempt stopped during discovery because it was looking for the English VSTest heading on a Spanish installation. The rerun reached the tests, then the coordinator terminated while printing a Unicode failure message through a Windows legacy code page. The runner now fixes VSTest's diagnostic language and uses UTF-8 for redirected diagnostics. Source fixes keep assertions independent of localized MSBuild wording, make debugger fixtures format decimals invariantly, and address the Explorer view toggle by its control. NuGet and GPG tests use the package IDs and key identities carried by their fixtures. The MCP action inventory accounts for whether release notes are carried by the build.

| Run | Passed | Failed | Skipped | Timed out | Scope |
| --- | ---: | ---: | ---: | ---: | --- |
| Unit tests | 618 | 0 | 4 | 0 | Complete unit project. Three environment-related omissions and one opt-in benchmark |
| Native reference suites | 10 | 0 | 0 | 0 | All four existing Xmake Explorer, clangd, Build and Run, and module intelligence classes with the reference tools on the packaged source |
| Real MCP client | 3 | 0 | 0 | 0 | Attached client through the actual MCP transport on the packaged source, including distinct ambiguous and unresolved edges |
| Focused managed and Explorer checks | 11 | 0 | 1 | 0 | Debugger rules, MCP action discovery, project loading and editing, logs, Compose, and Solution views. The filesystem case-sensitivity check skipped on this volume |
| UI and docking regression rerun | 8 | 0 | 0 | 0 | Minimum-width layout, distinct tool-window icons, Xmake availability and saved layout, dialog widths, shared context menus and typography |
| NuGet and GPG fixture rerun | 2 | 0 | 0 | 2 | Both NuGet tests passed. VSTest reported both 90-second Git for Windows GPG prompts as failures |
| Scale probes | 8 | 0 | 0 | 0 | Four headless editor runs and four captured-graph runs. The 500- and 1,000-module runs used warm Xmake caches |
| Unfiltered Phase 5 baseline through test-host cleanup | 1,233 | 30 | 108 | 1 | Matched results by test method. 1,372 of 1,612 distinct methods recorded. 240 methods were missing at the recovery checkpoint |
| Recovery on the same Phase 5 revision | 141 | 0 | 105 | 0 | Targeted 246 methods, including the 240 lacking a result and six repeated methods |
| Phase 5 baseline and recovery combined | 1,371 | 30 | 210 | 1 | 1,612 of 1,612 distinct methods, retaining the baseline outcome for repeats |
| Candidate-wide rerun before cancellation | 90 | 1 | 22 | 0 | Partial console results from parallel shards on `1d6e68f50`, after packaging `c786c232f` |

The baseline's logs and TRX reports are retained under `artifacts/alpha-regression-checkout/artifacts/test-results/` in `baseline-partial` and `local`. Two orphaned test hosts kept assemblies locked after the coordinator stopped. Their owned process trees were stopped before retrying. The test runner now stops the full owned process tree on Windows, writes diagnostics as UTF-8 and flushes each serial output line. Separate probes verified the encoding, process-tree cleanup and visibility of redirected output before a child exited. Recovery completed in 80.8 minutes, including 72.7 minutes from classes that run serially, and wrote to `resume`. The merged report is `artifacts/alpha-baseline-report.json`. It counts distinct methods with parameters normalized and therefore does not give per-theory-case coverage of all 1,639 discovery entries. The stabilized candidate has focused headless results. The full baseline and recovery ran on `869090ee0`. Some rows overlap across runs. The scale tool's 500- and 1,000-module export timeouts are command timeouts and are recorded below.

The candidate-wide rerun built the solution and discovered 1,644 test entries. It was canceled at the user's request before the serial stage. The signed Git commit test reproduced its failure in the parallel stage. The runner and test hosts exited, and 24 remaining build processes were stopped after verifying the run's MSBuild ownership marker. The partial logs are retained in `artifacts/alpha-candidate-full.log` and `artifacts/test-results/local/`.

The module graph now wraps its controls at the minimum window width and uses the dock's shared action registration. The focused UI and docking checks passed after the fixes. The release screenshots show the example's module interface, partition, implementation source and cross-target consumer in both themes. [Dark](../screenshots/module-graph-dark.png) · [Light](../screenshots/module-graph-light.png)

Restoring the bundled GPG key identities moved both signing tests past key lookup. They timed out later waiting for Pitstop to display the passphrase dialog. An SSH signing test also observed `Enter passphrase:` from this machine's Git, which the application treated as a generic Git prompt. The EF migrations failure involved the existing global `dotnet-ef` 10.0.10 and a fixture expecting a missing tool for EF Core 10.0.15. The baseline also reported paths denied while moving fixture directories in the folder watcher and language-server watcher tests, and an allocation threshold in the Git branch menu. These issues remain open in the regression report.

## Real MCP connection

The integration tests start an SDK client through `--mcp-attach` and talk to the same headless IDE that renders the Modules tree. The reported configuration and provider count match its current snapshot. The tests query a target, follow a module's dependencies, consumers and potential impact, and verify `limit=1` truncation. A second client asked the portable package for 1,000 results against the populated 1,000-module workspace. It received 500 results and `truncated: true`. Switching to Release changes the snapshot. Revoking trust or closing the workspace removes the previous providers from the response. Sending the MCP cancellation notification dismisses the pending admission prompt. The response is assembled off the UI thread and checked against the still-current trusted snapshot before returning.

An attached client also receives distinct unresolved and ambiguous results from a real compiler scan. This test supplies evaluated metadata with two targets sharing one module source, a consumer that sees both, and an import with no provider. The cancellation test covers the attached transport and admission path. Cancellation midway through traversal of a large graph remains unverified.

## Scalability

Run the generator with a new directory. It accepts the primary interface count, library count, optional exported partitions per module, and `fanout`, `chain` or `independent` dependencies. It writes generated sources and scripts inside that directory. The optional measurement arguments evaluate only the project the generator just created and retain actual command output and timings in a separate directory.

```powershell
python -I tools/module-scale.py artifacts/scale-100 --modules 100 --targets 2 --topology fanout `
    --xmake $env:PITSTOP_XMAKE --llvm $env:PITSTOP_TEST_LLVM_MINGW --results artifacts/scale-100-results
$env:PITSTOP_TEST_MODULE_SCALE = "$PWD/artifacts/scale-100"
dotnet test tests/Passenger.Tests --no-build --filter FullyQualifiedName~ModuleScaleTests
$env:PITSTOP_TEST_MODULE_SCALE_RESULTS = "$PWD/artifacts/scale-100-results"
dotnet test tests/Passenger.UnitTests --no-build --filter FullyQualifiedName~Measures_graph_construction_from_a_scale_capture
```

Measurements were taken on a Ryzen 9 3900X with 12 cores and 24 logical processors, 64 GiB installed RAM, and Windows 11 Pro build 26300. Each initial project used Debug, C++23, two library targets plus one executable, fanout imports, and no partitions. The P1689 command uses two workers. Single samples were collected while builds and regression tests were active, so differences between small cases can reflect contention.

| Primary modules | Xmake configure | Xmake metadata commands | Compilation database export | Standalone P1689 | Graph construction |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 10 | 14.49 s | 15.16 s | 15.97 s | 1.66 s | 35 ms |
| 100 | 4.78 s | 5.34 s | 13.27 s | 1.94 s | 32 ms |
| 500 | 12.60 s | 42.20 s | Timed out at 122 s | Unreached | Unreached |
| 1,000 | 12.60 s | 36.92 s | Timed out at 120 s | Unreached | Unreached |

Metadata timing covers sequential Xmake `show` calls. The real IDE's opening time also includes membership, export and UI initialization. Graph construction replays the compiler capture through the production resolver after input parsing and command selection. The 500- and 1,000-module exports left hundreds of Xmake dependency scan cache records before timing out. Retrying with those partial caches completed both exports. This locates the cold bottleneck inside the authoritative export, before independent P1689 scanning or graph rendering. A separate chained fixture with four primary modules and eight partitions passed export and scanning.

| Primary modules | Export on retry | Standalone P1689 | Graph construction | Resolved edges |
| ---: | ---: | ---: | ---: | ---: |
| 500 | 49.88 s | 3.39 s | 121 ms | 500 |
| 1,000 | 90.55 s | 6.73 s | 199 ms | 1,000 |

| Primary modules | Open to Xmake snapshot | Scan and graph | Open consumer to first clangd diagnostics | Caret dispatch | Import completion | Saved interface to new graph | Saved interface to clangd replay |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 10 | 39.81 s | 5.70 s | 4.99 s | 6.45 ms | 350 ms | 4.64 s | 6.20 s |
| 100 | 25.64 s | 4.28 s | 3.72 s | 4.92 ms | 170 ms | 4.76 s | 7.22 s |
| 500 | 29.53 s | 5.13 s | 5.53 s | 6.83 ms | 197 ms | 7.45 s | 9.85 s |
| 1,000 | 26.91 s | 6.37 s | 8.90 s | 5.04 ms | 192 ms | 6.71 s | 10.08 s |

| Primary modules | Editor/test host working set | Editor/test host peak | clangd working set | clangd peak |
| ---: | ---: | ---: | ---: | ---: |
| 10 | 228 MiB | 236 MiB | 66 MiB | 80 MiB |
| 100 | 230 MiB | 238 MiB | 82 MiB | 88 MiB |
| 500 | 234 MiB | 242 MiB | 51 MiB | 69 MiB |
| 1,000 | 255 MiB | 264 MiB | 134 MiB | 136 MiB |

The memory figures above are per-process snapshots and peaks up to first diagnostics, excluding other tools. The editor timings use prepared Xmake caches.

The packaged IDE at `045f81a01` also measured background indexing with a real MCP client, a new state directory and the prepared workspace. Opening `main.cpp` starts the timer. Clangd's log records its initialization and each generated workspace source it indexed. The memory readings capture the working set at the last indexed project source and process peaks up to that point.

```powershell
python -I tools/alpha-package-smoke.py $exe artifacts/scale-1000 artifacts/alpha-index-1000 `
    --version 0.2.0-alpha.1 --scale-index 1000
```

| Primary modules | Open to initialized | Open to all project sources indexed | App working set / peak | clangd working set / peak |
| ---: | ---: | ---: | ---: | ---: |
| 10 | 0.97 s | 1.22 s | 260 / 310 MiB | 54 / 54 MiB |
| 100 | 1.55 s | 3.06 s | 263 / 312 MiB | 114 / 121 MiB |
| 500 | 0.98 s | 10.22 s | 275 / 334 MiB | 222 / 306 MiB |
| 1,000 | 1.00 s | 16.92 s | 341 / 346 MiB | 220 / 295 MiB |

The last generated workspace source excludes background indexing of external headers or the standard library. In the portable Hello Modules run, clangd indexed its final `std.cppm` command 36.84 s after enqueueing the six compilation commands. Long-running steady-state memory and display frame latency remain unmeasured.

## Windows packaging

```powershell
$env:PITSTOP_RELEASE_VERSION = '0.2.0-alpha.1'
just package-win win-x64
Get-FileHash artifacts/package/Pitstop-windows-x64.exe,artifacts/package/Pitstop-windows-x64-portable.zip -Algorithm SHA256
```

The package script produced both unsigned artifacts from `c786c232f` and ran its portable-state verification. The MCP packaging probe ran the installer and portable app against separate disposable [Hello Modules](../../examples/hello-modules) copies with fresh settings directories. It verified the reported application version, module scan, Explorer and graph actions, provider navigation, editor saves, native build, run output and exit, and termination after the client closed. The installed test used PATH discovery. The portable test used explicit tool overrides. A portable launch without Xmake on PATH returned the missing-tool guidance.

```powershell
python -I tools/alpha-package-smoke.py $exe $configuredExampleCopy $newStateDirectory --version 0.2.0-alpha.1
```

The Windows installed-apps listing reports the numeric `0.2.0` version. The application reports `0.2.0-alpha.1`. The install/uninstall test checked the license, notices, Start menu entry and removal of installation registrations. The existing Passenger state directory and user PATH were preserved in the round trip. Both package probes recorded clangd initializing, opening the sample files and shutting down with status zero. Clangd rejected a folding-range request carrying a non-file URI in each package session. A clean Windows VM, simultaneous operation with an installed Passenger process, packet-level update capture and non-Windows toolchains remain unverified.
