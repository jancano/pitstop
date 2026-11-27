# Native-first migration

## Migration plan

Keep the .NET and Avalonia application runtime, text engine, editor, file operations, terminal, Git, workspace trust, process ownership, LSP and DAP transports, docking, settings, and MCP. The existing native validation fixtures and the alpha package report establish the baseline. The Git signing changes have a focused Windows verification run. Full regression coverage on the newer upstream merge remains outstanding.

| Area | Migration | Verification |
| --- | --- | --- |
| Xmake, clangd, modules | Extract the evaluated workspace contract and command ownership rules from Xmake-backed consumers. Keep Xmake evaluation, compilation database derivation and build scheduling in the provider. | Multi-target compilation, module visibility, configuration switching, saved-interface recovery, and trust |
| Explorer, build, run and MCP | Consume common evaluated data and keep project-editing and runtime hooks behind provider capabilities. Retain the existing virtualized views and process controls. | Headless project views, native diagnostics, build and run, and MCP snapshot queries |
| Project creation | Replace managed onboarding with a manifest-driven native catalog, validated toolchain selection and a single generation plan for preview and writes. | Manifest and path safety, generated projects built with Xmake, and light and dark headless screenshots |
| Managed product integrations | Map entry points and shared dependencies before removing MSBuild workspace loading, Roslyn, managed templates, NuGet UI, managed debugging and other development integrations. Extract the generic testing, package and debugger infrastructure before removing callers. | Generic editor, Git, terminal, diagnostics, LSP and DAP, settings, MCP and workspace lifecycle tests |
| Distribution | Compare the installed inventory and package sizes after managed runtime payloads are removed. Preserve source licensing, state paths and the Windows installer workflow. | Locked restore, full applicable regression suites, Windows installer and portable smoke tests |

Implement the project-system boundary first. Add the template engine and buildable catalog before replacing welcome and project creation. Add project editing and toolchain settings before removing managed product entry points. Perform mechanical consolidation after dependency removal, then verify and package the result.

## Native onboarding

The welcome screen uses the existing window and project-opening flow for a generated project, an existing `xmake.lua`, a folder, a file or a clone. Creating a project applies the declarative preview before asking the normal workspace trust question. The wizard inspects installed tools from a directory outside the selected project and does not launch the generated workspace's program.

The wizard offers the registered Xmake templates and loads their parameters from the manifest. On the Windows reference profile, it checks Xmake and LLVM versions, probes the compiler's supported standard flags and checks for libc++'s standard module source. Templates without an available compiler capability explain their requirements. The empty workspace template remains available without a detected compiler. Xmake evaluates the project's active build toolchain after the workspace opens. The detected template profile does not change a user's Xmake configuration.

The native desktop uses **New Project** and **Open Project**. The inherited managed project commands remain available in the legacy verification window.

## Xmake project editing

Project Explorer opens Xmake workspaces in Targets and also offers Modules and Files. Add Target, Add C Source File, Add C++ Source File, Add Header and Manage Target Dependencies use the active evaluated configuration. Module interfaces, partitions and implementation units use the module creation dialog. New source and header files are kept even if their Xmake target cannot be refreshed. After reevaluation, Pitstop checks actual target membership and proposes a script edit only if the file is absent.

For supported edits, Pitstop previews the Lua to append to the provider's existing script. The user can edit or cancel it. Applying the text requires trust, the same evaluated configuration, an unchanged script on disk and no unsaved script editor changes. Xmake 3.1.2 was verified to preserve existing source membership and target kind when a later target block adds a source. Dependency addition checks the evaluated graph for duplicate edges and cycles. Removing a dependency and changing custom declarations remain manual Xmake script edits.

## Toolchain inventory

**Settings → Native Toolchains** persists absolute path overrides and detects installed tools on request. Xmake, clangd and clang-scan-deps use the configured executables on the next workspace refresh. Empty settings fall back to the existing `PITSTOP_*` variables and discovery rules. Clang selection checks project template compatibility. Xrepo, clang-format and clang-tidy are shown for discovery and version inspection. The active build compiler, standard library and SDK remain controlled by the evaluated Xmake configuration. The report shows the detected MinGW-w64 SDK and the libc++ standard module source when present.

## Managed dependency audit

| Existing integration | Product entry point and background owner | Infrastructure to retain |
| --- | --- | --- |
| MSBuild projects and Roslyn | `MainWindow.Session`, `ProjectSession`, `ProjectHosts`, `CSharpService`, managed project templates | Folder indexes, file watching, open documents, LSP and reusable syntax coloring |
| Managed debugger and profiler | `DebugController`, `DebugSession`, the managed adapter executables, the profiler launch path | DAP, process and transport ownership, breakpoint state, debugger tool windows, stack and variable views |
| NuGet and managed tests | `PackageManager`, NuGet commands, managed test discovery and launch | Package list and version views, test identity, results, filters and navigation |
| XAML, mobile and Godot | Preview and inspection hosts, Godot project and editor integration, the managed settings and run commands | Generic text editing, terminal, process and panel components |
| Aspire and Entity Framework | Evaluation from managed projects and dedicated package and run actions | Generic HTTP requests and Docker tooling |

The application project currently stages Roslyn and managed project, debug, test, inspector and preview payloads. Some of its notice-generation inputs are collected from those same targets. The packaging migration must update the payload and its notices together. Keep CI's repository build projects and native validation fixtures when isolating distributable executables.

Desktop windows use native project loading. A folder with both `xmake.lua` and a solution opens its evaluated Xmake targets. The desktop leaves C# and solution files available as documents, and skips SDK discovery, managed workspace discovery, the MSBuild project host and Roslyn startup. Child desktop windows inherit this mode. The legacy window constructor remains for regression coverage.

Native MCP connections advertise workspace, module, editor, build and run operations. The managed debugger, NuGet, profiler, Hot Reload, Godot and test operations are reserved for the inherited verification profile. Native standalone and headless modes follow saved workspace trust. Trust changes during an attached connection are handled by the desktop's session. The remaining managed UI command surfaces and bundled tooling are still part of the migration.

## Validation status

The Windows x64 reference checks covered twelve generated template cases with Xmake 3.1.2 and LLVM-MinGW 23.1.6, including building and running the applicable targets. Selected tests also exercised the compilation workspace boundary, template safety, preview and generation, trust, Xmake project editing, native tool detection, and the mixed-folder desktop workflow. The welcome screen, wizard and Explorer were rendered in both themes. Local captures are under `artifacts/phase7-welcome-shots`, `artifacts/phase7-wizard-shots` and `artifacts/phase7-explorer-shots`.

Four real MCP tests covered the native tool list, trust grant and revocation in a standalone process, the attached module snapshot and its limits, unresolved and ambiguous imports, and cancellation. The package smoke script now records trust for its disposable sample in the isolated state directory. Running it against the source-built executable confirmed that the missing-Xmake response includes the configured path and the override needed to resolve it.

Phase 7 release verification still needs the remaining managed UI and packaging isolation, full applicable regression, the rest of the visual checklist, and clean Windows installer and portable verification. Check the Windows reference compatibility and package inventory before changing the release version.
