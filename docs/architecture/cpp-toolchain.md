# C++ toolchain for Hello Modules

Phase 1 validates native tools in standalone fixtures on Windows x64. The probe in `tools/Pitstop.CppProbe` reuses the existing process and LSP libraries. The IDE does not load the fixtures.

## First alpha

Pin **Xmake and Xrepo 3.1.2** with **LLVM-MinGW 20261117 UCRT x86_64**. The bundle includes Clang, clangd, clang-scan-deps, and LLDB 23.1.6, LLD, libc++, and MinGW-w64 headers and import libraries. Use Xmake with `-p mingw --toolchain=llvm --mingw=<bundle> --sdk=<bundle>`. This is the tested open-source build profile for Windows x64, with the platform UCRT runtime. Phase 9 integrates the bundle's `lldb-dap` using the same profile. Linux, macOS, Windows ARM64 and Xrepo package installation remain outside the tested support matrix. [Xmake release](https://github.com/xmake-io/xmake/releases/tag/v3.1.2), [LLVM-MinGW release](https://github.com/mstorsjo/llvm-mingw/releases/tag/20261117), [Native Debugging](native-debugging.md)

## Reproduce

Use the SDK in `global.json`, extract the portable tools locally, and run from the repository root. Paths below are examples.

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$xmake = 'artifacts/cpp-tools/xmake-3.1.2/xmake/xmake.exe'
$llvm = 'artifacts/cpp-tools/llvm-mingw-20261117-ucrt-x86_64'
dotnet run --project tools/Pitstop.CppProbe -- --xmake $xmake --llvm-bin "$llvm/bin"
dotnet run --project tools/Pitstop.CppProbe -- --trust-fixtures --xmake $xmake --llvm-bin "$llvm/bin" --mingw $llvm
```

The first invocation inventories tools in an empty scratch directory. The second evaluates only fixed, checked-out fixtures after explicit opt-in. It copies them under a new `artifacts/cpp-toolchain/` run directory, uses a private Xmake global directory, and records command lines, exit codes, stdout, stderr, generated JSON, and LSP replies. Build, run, configuration, and required metadata errors fail the probe. Experimental LSP and scanner outcomes have their own evidence files. Ctrl+C cancels through the existing process runner.

`tests/CppWorkspaces` has header-based code, named modules across a static library and executable, and `import std`. The module fixture includes a primary interface, implementation unit, re-exported interface partition, and internal partition. Xmake needed both the primary interface and its re-exported partition marked public. Marking just the primary interface public failed the target consumer's scan for `arithmetic:numbers`. The internal partition stays private. The probe selects two language modes in distinct copies. This exercises the fixtures in C++23 and C++26 modes.

## Observed tools and compatibility

Inventory was collected on Windows x64 on 2026-11-26. Xmake and Xrepo were initially absent from PATH. The portable archive provides `xmake.exe` and `xrepo.bat` together. Both identify as `3.1.2+HEAD.5d21e9daf`. Xrepo was inventoried through its launcher backend.

| Tool or platform component | Observed location and result |
| --- | --- |
| Clang, clangd, clang-scan-deps, clang-format, clang-tidy | `<llvm-mingw>/bin`, version 23.1.6. The scan-deps launcher delegates to `clang-scan-deps-real.exe`. |
| LLDB and lldb-dap | `<llvm-mingw>/bin`, from the 23.1.6 bundle. LLDB returned its version and lldb-dap returned help. |
| Clang, clang-cl, clangd, clang-scan-deps, clang-format, clang-tidy | `C:/Program Files/LLVM/bin`, version 22.1.8. |
| Stock LLDB and lldb-dap | `C:/Program Files/LLVM/bin`. LLDB failed to load `python311.dll`. The adapter's help invocation exited with Windows `0xC0000135` (missing DLL). |
| LLVM-MinGW standard library and SDK | libc++ and MinGW-w64 in the bundle. The `x86_64-w64-mingw32/lib/libc++.modules.json` manifest points to `share/libc++/v1/std.cppm`. |
| Existing Windows platform toolchain | VS 2026 Community in `C:/Program Files/Microsoft Visual Studio/18/Community`, VC tools `14.51.36231`, MSVC STL with `modules/std.ixx`, and Windows SDK `10.0.26100.0` in `C:/Program Files (x86)/Windows Kits/10`. |
| WSL Ubuntu 24.04 | libstdc++ 13 headers and an LLVM 18 directory were found. Linux compilation and editor operations were untested. |

| Xmake configuration | C++23 headers / named modules / import std | C++26 headers / named modules / import std |
| --- | --- | --- |
| LLVM-MinGW 23.1.6 with `--toolchain=llvm` | Build and run passed for all three | Build and run passed for all three |
| LLVM-MinGW with `--toolchain=mingw` | Headers passed. Modules fell back to GCC scanning and failed at the internal partition. Xmake could not find `std`. | Untested |
| Stock LLVM 22.1.8, `--toolchain=clang` with installed Windows SDK and STL | Build and run passed for all three | Build and run passed for all three |
| Stock LLVM 22.1.8, `--toolchain=clang-cl` with installed Windows SDK and STL | Build and run passed for all three | Untested |

The [machine-readable matrix](cpp-toolchain-samples/compatibility.json) records the twelve automated cases, effective C++ standard flags, scanner exits, and individual language-service results. The compiler include probe identified MSVC STL with `_MSVC_STL_VERSION 145` and libc++ with `_LIBCPP_VERSION 230106`.

## Xmake introspection

The probe reads Xmake JSON after trust and configuration. The samples are observed LLVM-MinGW C++23 output with machine paths replaced by placeholders. The compilation-database sample retains the consumer row. [Xmake built-in plugins](https://xmake.io/guide/extensions/builtin-plugins.html)

| Command or output | Evidence |
| --- | --- |
| `xmake show -l targets --format=json` and `xmake show -t arithmetic --format=json` | Lists targets, then reports kind, source paths and declaration locations, compiler and linker for the named target. [Target JSON](cpp-toolchain-samples/target-arithmetic.json) |
| `xmake show --info=depgraph --format=json` | Roots and target-to-target edges. [Target graph](cpp-toolchain-samples/target-graph.json) |
| `xmake show --format=json` | Platform, architecture, mode, build directories, Xmake version. Target metadata and compile commands identify the selected compiler. [Configuration](cpp-toolchain-samples/configuration.json) |
| `xmake project -k compile_commands --lsp=clangd .` | Per-file argument arrays with module source commands and BMI mappings. [Consumer row](cpp-toolchain-samples/compile-commands-consumer.json) |
| Generated scan files | P1689-shaped `provides` and `requires`. The observed `build/.gens/.../scans` layout is private build output. [Native scan](cpp-toolchain-samples/xmake-native-module-scan.json) |

## P1689

The independent scanner needs an explicit `output` entry for database mode. The probe copies the GNU driver's `-o` operand into that field, removes explicit `-fmodule-file` BMI mappings for source discovery, and translates GNU-driver `-imsvc` into `-isystem`. LLVM-MinGW's scan passed in both language modes and recorded primary interfaces, exported and internal partitions, and consumer imports. The stock LLVM batch scan rejected a generated `std.ixx` command because it did not resolve to exactly one compiler job. The native and independent scans can have different primary-output paths because they describe different compiler invocations. [Independent P1689 output](cpp-toolchain-samples/p1689.json), [Clang standard modules](https://clang.llvm.org/docs/StandardCPlusPlusModules.html)

Use P1689 to augment the Xmake target graph with compiler-observed providers and import edges. Preserve translation-unit identity when a file appears in multiple target contexts. Xmake owns configuration, compiler arguments, target edges, build order, and produced artifacts.

## clangd observations

The probe uses the real stdio LSP session with `--experimental-modules-support`. It requests completion, hover, definition, and references and records versioned `publishDiagnostics` notifications. It checks the raw Xmake database before and after the explicit build and uses a separate source-oriented database for the BMI experiment. [clangd features](https://clangd.llvm.org/features#experimental-c20-modules-support)

LLVM-MinGW returned clean baseline diagnostics, expected completion and hover, definition, and a reference in the consumer for all three fixture shapes in both modes, before and after the explicit Xmake build. References for the named module's `module_value` included the interface and consumer in the raw database runs. With the built database, a consumer's definition request also reached the re-exported partition and the implementation unit. Implementation navigation was inconsistent with the source-oriented database.

In the edit test the consumer first refers to a missing symbol, then the primary interface exports it. Both unsaved and saved edits left the consumer's error in place in the same LLVM-MinGW session, even with explicit BMI mappings removed. **Restarting clangd with the source-oriented database after saving resolved the new export without an Xmake rebuild** in C++23 and C++26. Clean diagnostics, completion, hover, and definition to the edited interface returned. A live refresh and a restart are distinct behaviors for integration and testing.

Stock LLVM 22.1.8 reported unknown `-imsvc` arguments in Xmake's GNU-driver database. Some cross-module requests returned correct locations after a build despite those diagnostics. The derived source-oriented database yielded clean baseline diagnostics for the same fixtures. Surface compiler-driver compatibility separately from missing-module and stale-BMI behavior.

## Proposed implementation

1. **Pitstop.Xmake** discovers tools and possible `xmake.lua` roots without running scripts. Gate every project operation, including inspection, with `WorkspaceTrust.Check`. Reuse `ChildProcesses.Command` and bounded `RunAsync` for configuration, metadata, build and run. Make Xrepo mutations explicit. Test trust revocation and validate the real provider against these fixtures.
2. **Pitstop.Cpp** owns C++ file recognition, document synchronization, and clangd sessions through `Passenger.Lsp.LanguageServer.StartAsync`. Key sessions by trusted root and compilation context. Keep the original Xmake compilation database and version any derived analysis database separately. Use fallback editing before trust. Prototype a coalesced clangd restart after saving a module interface, replay the open documents, and report stale consumer analysis while the new session starts. Validate this on the fixtures before treating it as an IDE feature.
3. **Pitstop.Modules** joins Xmake target ownership to scanner providers, primary interfaces, partitions, implementation units, and import edges. Key snapshots by target, platform, compiler identity, configuration, and translation-unit command. Track BMI provenance and invalidation. Keep target metadata available when scans fail and surface duplicate providers and unresolved imports.

Start with the verified Windows x64 build, run, and baseline editor flows. Treat live module refresh and native debugging as separately testable milestones.
