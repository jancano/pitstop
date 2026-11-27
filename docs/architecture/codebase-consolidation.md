# Codebase consolidation

Phase 7.5 starts from the upstream merge `0c234e8fe` and the [Phase 7 native desktop and Windows package verification](native-first-ide.md). The locked restore and full solution build for that merge passed, followed by four unit and three headless tests with one worker at below-normal priority. The clean checkout's build took 14 minutes 36 seconds on Windows x64, .NET SDK 11.0.100 and an AMD Ryzen 9 3900X. The native reference installations are Xmake and Xrepo 3.1.2 and LLVM-MinGW 23.1.6 with libc++.

## Baseline measurements

Count `.csproj` paths in `git ls-files src tests`. Count physical lines across tracked C# and Avalonia XAML files in each tree, excluding files under `Generated` and filenames containing `.g.` or `.generated.`. This counts C# test fixtures alongside the tests that use them and excludes vendored grammars, web assets, native sample sources and historical documentation.

| Tree | Projects | C# and AXAML files | Physical lines |
| --- | ---: | ---: | ---: |
| `src/` | 34 | 2,242 | 571,652 |
| `tests/` | 46 | 1,143 | 271,664 |

The application lockfile's base framework profile has 24 direct packages, 51 transitive packages, nine central transitive pins and 17 project entries. Count by `type` under the base framework in `src/Passenger.App/packages.lock.json`, excluding RID-suffixed profiles. The Phase 7 portable ZIP contains 306 DLL entries and five EXEs, including two variants of OpenConsole, the app, command shim and shared process helper. The portable is 143,597,259 bytes and the installer is 157,516,907 bytes. These are the Phase 7 packages listed in the [release notes](../release-notes/0.3.0-alpha.1.md). There is no clean incremental-build or startup timing for this exact source baseline.

## Feature-preservation matrix

| Capability and consumers | Existing dependency | Consolidation boundary and verification |
| --- | --- | --- |
| Editor, text operations, navigation, Vim, themes, local history, docking and keymaps | App, Editor, Text, Languages, Core | Preserve editor services, permissions and presentation. Keep generic editor, Vim and headless tests. |
| Xmake, clangd, P1689, modules, templates and build/run | Pitstop.ProjectSystem, Xmake, Cpp, Modules and Templates, plus App and Core | Preserve evaluated snapshots and separate build and semantic databases. The existing native tests and reference toolchain remain the verification path. |
| HTTP and OpenAPI | Endpoints, Requests, AppDocuments and HTTP language support. Roslyn supplies some route rows. | Expose the existing HTTP client in the native profile. Retain request parsing, authorization, environment files and schema browsing. Isolate managed route discovery from file-based requests. Verify with request and OpenAPI fixtures. |
| Docker, Compose and Container Explorer | Containers, ContainerTools, process helper, generic Web authoring, plus managed service launch paths | Retain engine and registry operations, logs, statistics and inspection. DAP and process ownership can serve a future LLDB backend. Keep managed deployment paths isolated while extracting them. |
| OpenTelemetry | Container and Aspire telemetry use shared models and views | Preserve OTLP ingestion and generic metrics, logs and traces. Extract the upstream producer from shared presentation before removal. |
| Discord | Discord IPC connection and activity worker, workspace/window lifecycle, settings JSON | Retain reconnect, idle, multiple windows and privacy preferences. Use a configurable application ID and native wording. Verify with the local IPC peer fixture. A production registration remains external. |
| Web languages, file previews and Gherkin | Languages, Lsp, web Node packages, App previews and Step Library | Keep JavaScript, TypeScript, HTML, CSS, JSON, YAML, shell, XML, Markdown, Gherkin and file previews. Step definitions currently come from managed discovery. Preserve generic feature presentation before replacing lookup. |
| Debugger and future LLDB | Dap, App debugger UI, DebugAdapter, Debugger and Debugger.Interop | Preserve protocol, process lifecycle, breakpoint storage, variable trees and fake adapter tests. Retain a legacy verification dependency until callers of the CoreCLR engine are extracted. |
| Profiler | Profiler models and EventPipe readers, App profiler views | Preserve flame graphs, call trees, timelines and selection. Extract the result shapes from managed collection before removing runtime dependencies. |
| Package UI and future Xrepo | App package windows and Passenger.Packages specializations | Keep generic presentation and progress and retain evaluated Xmake package metadata. Managed feeds and mutation require a separate extraction. NuGet still builds Pitstop. |
| Test Explorer and BDD execution | App testing and Step Library, TestFrames, managed test discovery and runners | Preserve result trees, test identities, cancellation, source navigation and generic workflow. Separate managed discovery from presentation. Pitstop's headless and screenshot tooling exercises its own Avalonia UI. |
| Managed project services | Workspace and Host for MSBuild, Lsp and Roslyn.Extension for C# services, App templates and logic | Phase 7 prevents activation in native sessions. Verify linked protocol consumers before dropping workspace evaluation and Roslyn types. Ordinary C# editing and generic LSP remain. |
| WPF and MAUI preview and inspection | Dedicated child previewers and inspector projects, linked preview protocols, App XAML controls, mobile and input utilities | Removed four dedicated child projects, their controllers and packaging. Kept standalone launch for source tests, shared XML editing, Avalonia resources, and device discovery and input utilities. |
| EF migrations, managed certificates, launch profiles and User Secrets | App commands use evaluated managed projects. HTTP, TLS, containers and run configurations also have independent callers. | Remove only verified managed paths. Keep native run state, user credentials and HTTP testing. |
| Godot | GDScript services and editor communication share App integration with managed scene and launch workflows | Preserve auxiliary languages and integration until a product decision on runtime scope. No GDExtension support is established. |
| Android, Apple devices and mobile | Device discovery, SDK and simulator queries, ADB, generic process utilities, managed MAUI run and relays | Preserve reusable discovery and communication. Managed mobile extraction requires isolated coverage. Native mobile remains unverified. |
| Updates and installation | App Updates and Installer hooks; UpdateHandlers belongs to managed Hot Reload | Preserve generic update transport and installer handoff. Keep automatic requests and publication disabled. |
| Core processes, launchers, MCP, state and attribution | Core, Vcs, Terminal, Launcher, Helper, Analyzers and Notices | Retain independent settings, trust, IPC and temp naming. Preserve original MIT and component notices. Verify real MCP and locked builds. |

The first-party dependency graph remains in the solution and project references. Production modules and tests are enumerated directly by those files. Payload ownership is additionally defined by the `Include*` targets in the application project and the web and Roslyn targets in the LSP project. Classify linked sources independently of the executable that owns their file path.

## Build, CI and MCP

Use `dotnet restore Pitstop.slnx --locked-mode` and `just build`. Unit tests are in `tests/Pitstop.UnitTests` and headless integration tests in `tests/Pitstop.Tests`. `just test --gate` runs the Windows-relevant gate, and `just test --report` runs the rest of the supported cases for the current system. Running the test runner without a lane filter exercises both. The workflows can upload CI artifacts and do not publish releases.

The native MCP entry points use `pitstop-ide` or `Pitstop --mcp` and the same native workspace, trust checks and module snapshots as the desktop. `module_workspace` can query the current providers, dependencies and potential impact. The managed MCP verification switch is available in source builds for repository tests. The Windows package disables that profile.

## Upstream integration

Keep Passenger as an upstream reference and preserve the original history and attribution. Select changes for shared editor, terminal, Git, protocol, performance, accessibility and general-purpose tooling. Review dependency and test changes against the retained scope. Keep managed backend developments out of Pitstop after their consumers have been removed, and preserve Pitstop product identifiers, temporary folders and release boundaries when reconciling equivalent code.

## Preview and inspection backends

WPF and MAUI preview and runtime inspection were removed, including the four child projects, their launch wiring, packaging targets and dedicated tests. The shared Avalonia inspector, workspace XAML editing and Pitstop's own Avalonia resources remain. The managed mobile verification paths retain ADB, Apple simulators, device launch, process ownership and the tests for those paths. Generic HTML, Markdown, SVG, image and audio previews are independent of the deleted hosts. The `Avalonia.Controls.Maui` and `Microsoft.Maui.Controls.Core` direct pins were exclusive to the removed projects. The test fixture that uses `Microsoft.Maui.Controls` still restores its own graph.

## Solution identity

`Pitstop.slnx` is the entry point for restore, builds and toolchain probes. Commit `d8e494f14` moved 32 projects to Pitstop directories, project files and namespaces. It updated assembly references, analyzers, test discovery, CI, package scripts and MCP launchers. The managed source verification projects share the Pitstop namespace. `Pitstop.Dap` is the shared debugger protocol and CDP library. The Windows packaging script rejects any `Passenger.*.dll` in the publish tree.

DAP and CDP wire names, settings roots, application IPC identifiers and existing theme keys such as `Passenger.Text`, `Passenger.Editor.Background` and `Passenger.Vcs.Added` retain their values. Godot probe resources retain their inherited logical names. The verification profile also retains private Roslyn protocol names, the mobile debug relay's source directory, and the deliberately named `PassengerCases` managed debugger fixture. The upstream site, historical records, original MIT copyright and attribution retain Passenger references.

Existing Pitstop state keeps its directory, JSON documents and trust decisions. Readers ignore the retired migrations and WPF/MAUI inspection keys in older documents. The native profile still reads ordinary C# and XAML files through the editor. It uses its own app, CLI, IPC, log, registration and temp identities.

## Discord Rich Presence

The built-in activity uses Pitstop's C++ description and defaults to showing Pitstop alone. The project-name and active-file options keep the existing persisted values so older settings retain their privacy preferences. Pitstop needs a numeric application ID to enable the integration.

Create a Pitstop application in Discord's Developer Portal, copy its Application ID, and upload Pitstop's image as the Rich Presence asset `pitstop-1024`. Export the ID as `PITSTOP_DISCORD_APPLICATION_ID` before starting Pitstop. Settings → Discord controls whether activity is shared and which names are included. The local IPC implementation needs only the application ID. [Discord Rich Presence documentation](https://docs.discord.com/developers/discord-social-sdk/development-guides/setting-rich-presence)

## Native HTTP Client

The native Endpoints window serves `.http` and `.rest` files. It uses the existing request editor, environment choices and grants, and response presentation. With an OpenAPI JSON document open in the workspace, **Generate Requests from OpenAPI** appends missing requests to the adjacent `.http` file. Review its `base` variable before sending a request. Generation reads the editor buffer and uses the existing file change and undo mechanism. Managed route discovery and build-based OpenAPI input remain confined to the verification profile.

## EF Core migrations

The Migrations window, managed tool invocation, MCP actions, configuration keys, diagnostics and exclusive regression tests were removed. Older workspace state can still be read, with the retired migration fields ignored. The two remaining restore fixtures pin SQLite for the managed HTTP and SQL tests and the older EF Core base package for launch freshness. The EF Core 11 and PostgreSQL migration graphs and the design package were removed. Managed NuGet's bulk package grouping and shared service connection-string logic remain available.

## Measurements after pruning

Measured at `02edbfe31` with the baseline's counting rules. The Phase 7 Windows profile already excluded the removed preview and inspection hosts.

| Tree | Projects | C# and AXAML files | Physical lines | Change in lines |
| --- | ---: | ---: | ---: | ---: |
| `src/` | 30 | 2,175 | 558,905 | −12,747 |
| `tests/` | 44 | 1,128 | 267,467 | −4,197 |

The default application lockfile retains 24 direct packages, 51 transitive packages, nine central transitive pins and 17 project entries. These figures describe the base framework profile for source verification. Windows native packaging also excludes the project references and source-specific restore downloads for the managed child tools. The solution and bundled-package notice check passed for 502 package names.

## Validation

Locked restore and the complete Debug solution build at `daf2a8427` passed on Windows x64 with .NET SDK 11.0.100, six MSBuild workers, shared compilation disabled and zero warnings or errors. The build step took 2 minutes 57 seconds. The full unit suite passed 618 tests, with five skips for the optional scale capture, unavailable symlink creation and a Unix-only path case. No unit tests failed.

The focused headless run passed 12 cases, including Discord IPC, native OpenAPI generation, HTTP and SQL presentation, managed launch and native MCP trust. Its 24 toolchain-dependent cases skipped before the reference profile was supplied. A separate run with `PITSTOP_XMAKE`, `PITSTOP_CLANGD`, `PITSTOP_CLANG_SCAN_DEPS` and `PITSTOP_TEST_LLVM_MINGW` set passed all 12 native project template cases.

Thirteen integration tests passed with the reference toolchain. They exercised C++23 and C++26 module completion and navigation, `import std`, module interface refresh, Xmake evaluation, rebuild and incremental build, interactive execution, diagnostics, and the real MCP module API. The tests include missing and ambiguous providers, request cancellation, configuration isolation and workspace trust revocation.

The Windows CI gate at `daf2a8427` ran in four shards, with 711 passed, two failed, seven skipped and no timeouts. The managed parallel-loop debugger case stopped at a breakpoint before the expected exception. Its isolated rerun passed, and the same test passed in a clean checkout of the baseline. The cause under parallel load remains unresolved. The UI rule flagged the native template dialog creating item-template rows. The dialog now uses `ReusedRows`, `ListRow.Dim` and the existing `BrowseRow` control.

At `03ae4a8de` the complete solution build passed again with zero warnings or errors. Five targeted tests passed, including the previously failing reusable-row rule, wizard creation and native command isolation across desktop and MCP. Two toolchain cases skipped in that run. A second run with the reference tools passed all three wizard cases, including standards detection and executable overrides.

At `02edbfe31`, locked restore and `just build` in a fresh checkout passed with zero warnings and zero errors. The build step took 4 minutes 36 seconds with six workers under concurrent validation load. Another solution build from the development checkout took 4 minutes 5 seconds before the combined suite.

The Git signing fix associates a generic SSH passphrase prompt with the signing key from Git's configuration when the signing format is SSH. The targeted runs passed five cases, including an actual MCP commit, Git for Windows GPG and SSH signing, signed-log output and cleanup of a held signing file. Standalone GPG and X.509 cases skipped because `gpg` and `gpgsm` were unavailable on PATH.

Light and dark captures were taken from both the headless tests and the Windows package. The captures cover the welcome screen, project creation, toolchain settings, project and module views, graph, editor completion and diagnostics, Build and Run, HTTP Client, Containers, and Discord settings. This machine has no Docker command and no production Discord application registration. Containers' missing-tool presentation and the configuration path for Discord were visible. The Discord IPC tests use a local peer.

## Windows x64 package

`tools/package-win.sh win-x64` created the unsigned `0.3.0-alpha.1` candidate from a clean checkout of `02edbfe31`. The archive contains 306 DLLs and five EXEs, with no `Passenger.*.dll` or managed child-tool folders. The Phase 7 baseline already excluded the removed preview hosts. The portable ZIP is 342,404 bytes smaller and the installer is 353,232 bytes smaller than the Phase 7 packages.

The portable and installed application both opened the isolated Hello Modules fixture, reported the module index through MCP, navigated to a provider, saved an edit, built the project and printed `Hello, modules!` with exit code zero. The portable also produced the missing-Xmake guidance. Installation and uninstall checks covered Open With associations, the user `PATH`, and the existing Pitstop and Passenger settings roots. The native child payload and installed trust checks passed.

| Package | Bytes | SHA-256 |
| --- | ---: | --- |
| `Pitstop-windows-x64-portable.zip` | 143,254,855 | `47a68be9ca95e430834611a4c4509db5c635a9e2461e62c6c2b8daa1871d4f4a` |
| `Pitstop-windows-x64.exe` | 157,163,675 | `4021a7faa6e33be6fe9ddbbde7fdb2e4d932b04411906a44485a661994924560` |

For an off-screen startup comparison, run `Pitstop.exe --screenshot shell.png --theme dark --size 1260x820 --scale 1` in each portable distribution with a fresh `PITSTOP_SETTINGS` directory. Six alternating runs per build, with the first sample discarded, gave medians of 1.879 s for Phase 7 (1.695–2.872 s) and 2.245 s for the consolidated build (1.617–2.447 s). The measurement includes PNG encoding and process exit. Both ran on the same Windows host while build and test jobs were active, so these ranges leave normal interactive startup unmeasured.

## Retained verification and product boundaries

| Boundary | Retained source and reason |
| --- | --- |
| Native projects | `Pitstop.ProjectSystem`, `Pitstop.Xmake`, `Pitstop.Cpp`, `Pitstop.Modules` and `Pitstop.Templates` remain separate. The evaluated snapshot and Xmake compilation database are authoritative. |
| Editor and auxiliary languages | `Pitstop.Core`, `Pitstop.Text`, `Pitstop.Editor`, `Pitstop.Languages`, `Pitstop.Lsp`, `Pitstop.Terminal` and `Pitstop.Vcs` supply shared editor, transport, process and Git capabilities. |
| Managed project and language tests | `Pitstop.Host`, `Pitstop.Roslyn.Extension` and `Pitstop.RoslynPatch` serve the source verification profile. The Windows native build excludes their project references and Roslyn server download. The App and LSP share linked protocol definitions with the verification implementations. |
| Debugger, profiler and inspectors | The debugger views and `Pitstop.Dap` remain reusable. `Pitstop.Debugger`, `Pitstop.Debugger.Interop`, `Pitstop.DebugAdapter`, `Pitstop.UpdateHandlers`, `Pitstop.TestFrames` and Avalonia's `Pitstop.Inspector` remain for source verification or linked shared code. Managed CLR profiling and native-independent views still share `Pitstop.Profiler`. |
| HTTP, Docker, telemetry and packages | The native HTTP client uses file-backed requests. Containers and telemetry retain their shared workflow and views. `Pitstop.ContainerTools` still shares managed launch and profiler code for the verification profile. Managed package behavior remains confined to that profile, with generic presentation to reuse if an Xrepo backend is added. |
| Godot and mobile | Godot's language, parsers and communication remain. Existing ADB, simulator and device features remain in source for verification. Native mobile and GDExtension workflows remain unverified. |

The Windows package excludes the dedicated managed host, debugger, Roslyn, inspector, test and Hot Reload payloads. Some shared assemblies still compile managed features for the repository's legacy verification profile. Removing those implementations requires extracting their remaining shared callers. Linux and macOS native distribution remain unverified.

The future LLDB adapter can reuse `Pitstop.Dap`, the debugger views, breakpoints and child-process ownership. Xrepo can reuse the package presentation and Xmake's evaluated package metadata. Native test discovery can feed the shared result tree and navigation commands. A native profiler first needs a backend-independent capture to feed the flame graph, call tree and timeline. The native project-system provider interface remains available for a future CMake provider.

The native profile preserves standalone container tools, file-based HTTP/OpenAPI, web language servers, file previews and Gherkin editing. Live Docker and Compose operations require an available engine. Managed route discovery, profiling, test execution, package mutation and Gherkin step discovery still use their verification backends where applicable. The observability models and views retain their existing producers. Generic updates are still configured off. The native product offers no managed project commands through the desktop or MCP.
