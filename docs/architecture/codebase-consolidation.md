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
| WPF and MAUI preview and inspection | Dedicated child previewers and inspector projects, linked preview protocols, App XAML controls, mobile and input utilities | Prune controllers, packaging and linked code with the child projects. Keep Pitstop's Avalonia resources, generic XML and preview support. MAUI/WPF inspection also serves runtime modes, which need separate ownership analysis. |
| EF migrations, managed certificates, launch profiles and User Secrets | App commands use evaluated managed projects. HTTP, TLS, containers and run configurations also have independent callers. | Remove only verified managed paths. Keep native run state, user credentials and HTTP testing. |
| Godot | GDScript services and editor communication share App integration with managed scene and launch workflows | Preserve auxiliary languages and integration until a product decision on runtime scope. No GDExtension support is established. |
| Android, Apple devices and mobile | Device discovery, SDK and simulator queries, ADB, generic process utilities, managed MAUI run and relays | Preserve reusable discovery and communication. Managed mobile extraction requires isolated coverage. Native mobile remains unverified. |
| Updates and installation | App Updates and Installer hooks; UpdateHandlers belongs to managed Hot Reload | Preserve generic update transport and installer handoff. Keep automatic requests and publication disabled. |
| Core processes, launchers, MCP, state and attribution | Core, Vcs, Terminal, Launcher, Helper, Analyzers and Notices | Retain independent settings, trust, IPC and temp naming. Preserve original MIT and component notices. Verify real MCP and locked builds. |

The first-party dependency graph remains in the solution and project references. Production modules and tests are enumerated directly by those files. Payload ownership is additionally defined by the `Include*` targets in the application project and the web and Roslyn targets in the LSP project. Classify linked sources independently of the executable that owns their file path.

## Upstream integration

Keep Passenger as an upstream reference and preserve the original history and attribution. Select changes for shared editor, terminal, Git, protocol, performance, accessibility and general-purpose tooling. Review dependency and test changes against the retained scope. Keep managed backend developments out of Pitstop after their consumers have been removed, and preserve Pitstop product identifiers, temporary folders and release boundaries when reconciling equivalent code.

## Preview backends

The WPF and MAUI preview projects, child hosts, linked request protocols and their exclusive tests have been removed. Windows packages already excluded those binaries in Phase 7. Avalonia preview in the repository's managed verification profile still uses Avalonia's previewer. WPF and MAUI runtime inspection, XAML code intelligence and the fixtures that exercise those separate implementations remain. Generic HTML, Markdown, SVG, image and audio preview code is independent of the removed hosts. The `Avalonia.Controls.Maui` central package pin was exclusive to the MAUI previewer. MAUI dependencies used by retained inspection and fixtures remain.

## Solution identity

`Pitstop.slnx` is the entry point for restore, builds and toolchain probes. CI and repository-root discovery use that name. `Pitstop.Dap` contains the shared debugger protocol and CDP types, including `HitCondition`, which the retained inspector projects compile from the same source. DAP and CDP wire names, persisted JSON fields and application IPC identifiers keep their existing values. Other inherited assembly names and namespaces remain pending dedicated migrations. Stable theme keys such as `Passenger.Text` still appear in the theme format and its mappings. Godot probe resources retain their inherited logical names. `site/` is the upstream Passenger website source and remains outside Pitstop publication.

## Discord Rich Presence

The built-in activity uses Pitstop's C++ description and defaults to showing Pitstop alone. The project-name and active-file options keep the existing persisted values so older settings retain their privacy preferences. Pitstop needs a numeric application ID to enable the integration.

Create a Pitstop application in Discord's Developer Portal, copy its Application ID, and upload Pitstop's image as the Rich Presence asset `pitstop-1024`. Export the ID as `PITSTOP_DISCORD_APPLICATION_ID` before starting Pitstop. Settings → Discord controls whether activity is shared and which names are included. The local IPC implementation needs only the application ID. [Discord Rich Presence documentation](https://docs.discord.com/developers/discord-social-sdk/development-guides/setting-rich-presence)

## Native HTTP Client

The native Endpoints window serves `.http` and `.rest` files. It uses the existing request editor, environment choices and grants, and response presentation. With an OpenAPI JSON document open in the workspace, **Generate Requests from OpenAPI** appends missing requests to the adjacent `.http` file. Review its `base` variable before sending a request. Generation reads the editor buffer and uses the existing file change and undo mechanism. Managed route discovery and build-based OpenAPI input remain confined to the verification profile.

## EF Core migrations

The Migrations window, managed tool invocation, MCP actions, configuration keys, diagnostics and exclusive regression tests were removed. Older workspace state can still be read, with the retired migration fields ignored. NuGet grouping for EF Core package-family upgrades and the managed launch, HTTP and SQL tests still use EF Core test dependencies. The HTTP Client's data and permissions, runtime console and shared service connection-string logic remain in place.

## Measurements after pruning

Measured at `03ae4a8de` with the baseline's counting rules. The removed preview projects were already excluded from the Windows native packaging profile. Source reductions and package size changes measure different effects.

| Tree | Projects | C# and AXAML files | Physical lines | Change in lines |
| --- | ---: | ---: | ---: | ---: |
| `src/` | 32 | 2,200 | 563,698 | −7,954 |
| `tests/` | 46 | 1,133 | 268,576 | −3,088 |

## Validation

Locked restore and the complete Debug solution build at `daf2a8427` passed on Windows x64 with .NET SDK 11.0.100, six MSBuild workers, shared compilation disabled and zero warnings or errors. The build step took 2 minutes 57 seconds. The full unit suite passed 618 tests, with five skips for the optional scale capture, unavailable symlink creation and a Unix-only path case. No unit tests failed.

The focused headless run passed 12 cases, including Discord IPC, native OpenAPI generation, HTTP and SQL presentation, managed launch and native MCP trust. Its 24 toolchain-dependent cases skipped before the reference profile was supplied. A separate run with `PITSTOP_XMAKE`, `PITSTOP_CLANGD`, `PITSTOP_CLANG_SCAN_DEPS` and `PITSTOP_TEST_LLVM_MINGW` set passed all 12 native project template cases.

Thirteen integration tests passed with the reference toolchain. They exercised C++23 and C++26 module completion and navigation, `import std`, module interface refresh, Xmake evaluation, rebuild and incremental build, interactive execution, diagnostics, and the real MCP module API. The tests include missing and ambiguous providers, request cancellation, configuration isolation and workspace trust revocation.

The complete Windows CI gate at `daf2a8427` ran in four shards, with 711 passed, two failed, seven skipped and no timeouts. The managed parallel-loop debugger case stopped at a breakpoint before the expected exception. Its isolated rerun passed, and the same test passed in a clean checkout of the baseline. The cause under parallel load remains unresolved. The UI rule flagged the native template dialog creating item-template rows. The dialog now uses the shared `ReusedRows` implementation and resets each row's icon, label, opacity and tooltip when it receives another template. The separate CI report lane remains unverified.

At `03ae4a8de` the complete solution build passed again with zero warnings or errors. Five targeted tests passed, including the previously failing reusable-row rule, wizard creation and native command isolation across desktop and MCP. Two toolchain cases skipped in that run. A second run with the reference tools passed all three wizard cases, including standards detection and executable overrides.

Light and dark captures were taken from both the headless tests and the Windows package. The captures cover the welcome screen, project creation, toolchain settings, project and module views, graph, editor completion and diagnostics, Build and Run, HTTP Client, Containers, and Discord settings. This machine has no Docker command and no production Discord application registration. Containers' missing-tool presentation and the configuration path for Discord were visible. The Discord IPC tests use a local peer.

## Windows x64 package

`tools/package-win.sh win-x64` created the unsigned `0.3.0-alpha.1` candidate from a clean checkout of `03ae4a8de`. The archive contains 306 DLLs and five EXEs. `Pitstop.Dap.dll` replaces `Passenger.Dap.dll` and the native payload has no managed child-tool folders. The Phase 7 baseline already excluded the removed preview hosts. The portable ZIP and installer are each about 0.34 MB smaller than the respective Phase 7 packages.

The portable and installed application both opened the isolated Hello Modules fixture, reported the module index through MCP, navigated to a provider, saved an edit, built the project and printed `Hello, modules!` with exit code zero. The portable also produced the missing-Xmake guidance. Installation and uninstall checks covered Open With associations, the user `PATH`, and the existing Pitstop and Passenger settings roots. The native child payload and installed trust checks passed.

| Package | Bytes | SHA-256 |
| --- | ---: | --- |
| `Pitstop-windows-x64-portable.zip` | 143,252,905 | `0728030f71d182ff015a5569224dd35d9f3f82422f3d71388c7295c19e213c25` |
| `Pitstop-windows-x64.exe` | 157,174,269 | `50c871d3f29c189ff36dd8e72fce508d637a351862f17658a47971988e17eb1f` |

## Remaining extraction

The source still compiles managed MSBuild, Roslyn, CoreCLR debugging, NuGet, Aspire, mobile and runtime XAML inspection services for the repository verification profile. The native package excludes their dedicated executables and retains assemblies that still contain shared services and legacy code. The next extraction boundaries are the generic debugger views from managed launch and Hot Reload, Compose and telemetry from Aspire resource discovery, and template and package presentation from managed project discovery. Linux and macOS native distribution remain unverified.
