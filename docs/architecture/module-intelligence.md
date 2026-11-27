# C++ Module Intelligence

Pitstop uses the active trusted Xmake workspace and Clang's P1689 dependency scan to populate the Modules view, Module Dependencies tool window, editor actions, and the read-only `module_workspace` MCP operation. The reference profile is Windows x64, Xmake/Xrepo 3.1.2, LLVM-MinGW and clangd 23.1.6, and libc++. Build and semantic compilation databases continue to follow the [Xmake workspace architecture](xmake-workspaces.md), [C++ editing](cpp-language-intelligence.md), and [native build and run](native-build-run.md).

## Evidence and model

`Pitstop.Modules` has no Avalonia dependency. A snapshot holds the evaluated Xmake workspace, compilation units, providers, direct compiler dependencies, and missing, unresolved, or ambiguous results. A provider identity combines its logical name, compiler command context, Xmake target, and active workspace configuration. A source shared between targets keeps separate contexts. Xmake's evaluated file membership identifies single-target commands. Target segments in its observed `.objs` and `.gens` paths disambiguate shared or generated command rows. The context records which evidence supplied its target.

The scanner reads the source-oriented semantic database and creates an isolated scan input with output identities. It keeps separate commands with different preprocessing flags. On the reference profile, a module's BMI and object compilation rows differ in how the same source is compiled. A preprocessing signature normalizes the observed `--precompile`, `-c`, and BMI-specific `-x c++-module` differences. Xmake's own database and files stay intact. [Phase 1 scan sample](cpp-toolchain-samples/p1689.json), [Clang module terminology](https://clang.llvm.org/docs/StandardCPlusPlusModules.html)

`provides` identifies a primary interface, interface partition, or internal partition when the reported name and `is-interface` flag establish that kind. An absent flag leaves the kind unknown. `requires` establishes a compiler dependency and can provide a source path. Resolution first consults the matching authoritative Xmake command for a named `-fmodule-file` binding. Without a binding, the scanner path and the evaluated target dependency graph constrain the candidates. In the duplicate-name fixture Clang's batch scan reported `other.cppm` for the Debug consumer. Xmake's command explicitly bound the module to the BMI built from `different-filename.cppm` in target `first`. Pitstop keeps both pieces of evidence and follows the Xmake binding. The SDK's `share/libc++` location and the reported standard module name identify the reference libc++ standard modules.

P1689 dependencies include implicit dependencies. The Phase 1 implementation source requires `arithmetic`, but the rule does not establish which module the source implements. Pitstop represents it as a translation unit and potential consumer. Re-export, exported symbols, exact declaration ranges, and ownership of non-provider implementation units remain unknown in this index. The editor continues to use clangd for symbol information. The UI recognizes the line being edited for import completion, while the scanner supplies the module facts.

## Lifecycle and IDE behavior

The module workspace starts after trusted Xmake evaluation. Source watcher events coalesce into a background scan. Input and configuration changes clear the current graph and cancel its generation, and only the active generation can publish. Closing the folder or taking back trust ends its scan. Discovery checks the configured compiler's LLVM installation and its reported scanner capabilities. Set `PITSTOP_CLANG_SCAN_DEPS` to override the executable. Tool failures report their cause and configuration. Read-only scanning does not schedule BMI builds or ask clangd to restart.

Use **Show Modules in Explorer** from the Xmake root menu or **View**. Providers are grouped by target and logical primary name, with separate external and standard library groups. Opening a unit opens its actual source file. **Go to Module** and **Reveal Module in Explorer** follow a resolved provider. **Find Module Consumers** opens the dependency graph for that provider. Module completions are offered in recognized import input. Same-target primary providers and cross-target providers confirmed by the target's scanned imports are offered. Relative partitions are scoped to a confirmed module provider source in the active context. The module suggestions merge into the existing clangd completion list.

The graph draws direct edges for a selected compilation unit. Its reverse and transitive modes calculate potential consumers with cycle-safe traversal. A cycle is calculated on resolved unit edges before any grouping by module name. Potential impact describes reachability. Xmake and the compiler decide actual invalidation and recompilation. The graph filters names, targets, and external providers, supports zoom and middle-button pan, and limits rendered nodes in each direction to 150. The Explorer retains its virtualized rows. MCP reads the same snapshot, with optional module and target filters and capped result lists.

**New C++ Module** creates only a fresh source path inside the workspace. It previews the declaration and rejects known collisions. Existing primary interfaces are required for a partition or implementation unit. Xmake reevaluates source membership after creation. If the new path is excluded, the wizard presents an editable `add_files` suggestion for the owning target's script. Applying that suggestion and any required primary-interface re-export remain explicit editor operations. This also works when a project puts its target declarations in included Lua files.

## Reproduce

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:PITSTOP_XMAKE = '<xmake>/xmake.exe'
$env:PITSTOP_TEST_LLVM_MINGW = '<llvm-mingw>'
$env:PITSTOP_CLANGD = "$env:PITSTOP_TEST_LLVM_MINGW/bin/clangd.exe"
$env:PITSTOP_CLANG_SCAN_DEPS = "$env:PITSTOP_TEST_LLVM_MINGW/bin/clang-scan-deps.exe"
$env:Path = "$env:PITSTOP_TEST_LLVM_MINGW/bin;$env:Path"
$env:PITSTOP_TEST_MODULE_SHOTS = "$PWD/artifacts/module-intelligence-shots"
just build
dotnet test tests/Pitstop.UnitTests --no-build --filter FullyQualifiedName~ModuleWorkspaceTests
dotnet test tests/Pitstop.Tests --no-build --filter FullyQualifiedName~ModuleIntelligenceTests
```

For manual experiments, use disposable copies of the fixtures. Configure them with the [reference Xmake command](xmake-workspaces.md). `modules` exercises cross-target requirements and partitions, and `standard-library` imports `std`. `module-contexts` imports a module in Debug and omits the import in Release while keeping same-named providers in distinct targets. `module-cycle` intentionally makes Xmake reject compilation database generation. `module-generated` declares a missing generated source. Run `xmake lua generate.lua` explicitly from its disposable copy to materialize that source before retrying the scan.

To inspect P1689 before a build, run these commands from a configured, trusted fixture copy. Repeat the export and scan after `xmake clean` to inspect the clean-workspace behavior.

```powershell
$probe = New-Item -ItemType Directory -Path (Join-Path ([IO.Path]::GetTempPath()) "pitstop-scan-$([guid]::NewGuid())")
& $env:PITSTOP_XMAKE project -k compile_commands --lsp=clangd $probe.FullName
$commands = Get-Content (Join-Path $probe.FullName 'compile_commands.json') -Raw | ConvertFrom-Json
foreach ($entry in $commands) {
    $flags = @($entry.arguments | Where-Object { $_ -notlike '-fmodule-file=*' } | ForEach-Object {
        if ($_ -eq '-imsvc') { '-isystem' } else { $_ }
    })
    $entry.arguments = $flags
    $entry | Add-Member -NotePropertyName output -NotePropertyValue $flags[([array]::IndexOf($flags, '-o') + 1)] -Force
}
$scanInput = Join-Path $probe.FullName 'scan-commands.json'
[IO.File]::WriteAllText($scanInput, (ConvertTo-Json -InputObject @($commands) -Depth 10))
& $env:PITSTOP_CLANG_SCAN_DEPS -format=p1689 -j 2 "-compilation-database=$scanInput"
```

## Verification

| Check | Windows x64 result |
| --- | --- |
| Complete solution build | Passed with zero warnings and errors |
| Unit tests | 618 passed, 3 skipped by inherited environment checks. Includes captured and partial P1689 records, shared source contexts, ambiguity, cancellation, cycle and impact traversal, and module declarations |
| Native module intelligence | 2 passed. Covers C++23 and C++26 before build, provider and graph navigation, private partition visibility, source creation with a membership suggestion, buffer and database preservation, Debug and Release isolation, trust revocation, and a failed scan following a workspace switch |
| Native tests without toolchain configuration | 2 skipped with the reference profile and environment variables in the skip message |
| Native clangd regressions | 4 passed, including saved interface recovery and `import std` in both language modes |
| Native Build and Run regressions | 3 passed, including incremental module builds, both language modes, `import std`, Clean and Rebuild, interactive execution, and build cancellation on trust revocation |
| Headless workspace and managed regressions | 9 passed across Xmake trust and lifecycle, clangd lifecycle, and managed code assist and Build and Run |
| Light and dark screenshots | Captured and inspected the Modules tree, dependency graph, import completion, and module creation dialog in `artifacts/module-intelligence-shots` |

## Observations and limitations

- On the Windows x64 reference profile, a standalone P1689 scan ran before building and after `xmake clean` for both the named-module and `import std` samples. With two scanner workers, the four measured scans took about 0.99–1.20 seconds on these fixtures. Configuration and compilation database export were outside that measurement.
- Xmake rejected `module-cycle` during compilation database export and described the cycle. The IDE has a failure state for that input. Unit-level traversal separately checks cycle detection when an actual resolved graph is available.
- Xmake's compile database export failed when an `always_added` generated module source was missing. The normal `before_build` hook in the fixture ran too late to supply the source for Xmake's module scan. After the fixture's explicit generator ran, export and scanning succeeded and reported the generated provider and its consumer.
- Saved source edits trigger a coalesced rescan of the current compilation database. Per-file scanner result reuse, header-unit indexing, non-Clang drivers, unknown include suffixes, module visibility across targets without observed import evidence, and unsaved cross-module semantic propagation have no validated support in this phase.
- The graph and completion tests use small fixtures. Large-workspace indexing latency, other toolchain profiles, and the Module Workspace tool over a live MCP connection remain unverified.
