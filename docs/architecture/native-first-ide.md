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

The Windows package publishes with `PitstopManagedPayload=false`. This excludes Roslyn and its extension, the managed project host and debugger, managed test frames, update handlers, and XAML and mobile preview and inspector payloads. Notice generation uses the same staging targets. The package retains the .NET runtime, shared process helpers, language assets, native templates, and third-party notices. Repository builds keep the managed payloads so existing tests can still exercise them. Managed implementation and shared assembly dependencies remain in the source tree and need further dependency extraction before they can be removed from the application assembly.

Desktop windows use native project loading. A folder with both `xmake.lua` and a solution opens its evaluated Xmake targets. The desktop leaves C# and solution files available as documents, and skips SDK discovery, managed workspace discovery, the MSBuild project host and Roslyn startup. Child desktop windows inherit this mode. The legacy window constructor remains for regression coverage.

The native desktop filters the action palette, menus, keymap rows, managed settings, and managed dock windows. Direct invocations of filtered actions report that they require the managed development profile. Native build and run, plain-text editing, Git, the terminal, and the common editor controls remain available. Windows registers C, C++, module and Lua extensions under Open With, preserving the user's chosen defaults. Package uninstall removes Pitstop's registrations and keeps the user's Pitstop state.

The native trust dialog explains that granting trust permits Xmake evaluation, builds, executable launches, compiler and language tooling, and configured Git programs. For a folder inside a Git repository, it names the repository scope. Both profiles use the same underlying trust decisions.

Native MCP connections advertise workspace, module, editor, build and run operations. The managed debugger, NuGet, profiler, Hot Reload, Godot and test operations remain available in the repository verification profile. The Windows package disables that profile. Native standalone and headless modes follow saved workspace trust. Trust changes during an attached connection are handled by the desktop's session.

The initial package's MCP smoke test timed out opening a source file after successfully indexing the modules. Both the MCP open tool and the run configuration selector wait for workspace discoveries. Native sessions treat Aspire discovery as complete because they do not schedule it. Managed sessions still wait for their actual discovery task.

## Repository organization

The evaluated compilation and template boundaries live in `Pitstop.ProjectSystem` and `Pitstop.Templates`. Retain the inherited solution, project, assembly and namespace names while those projects share code and build fixtures with Passenger. A dedicated rename is the appropriate point to update embedded resources, XAML, scripts, tests and CI together. Keep the current Pitstop settings, IPC and project-state identities through that change.

## Validation status

The Windows x64 reference checks covered twelve generated template cases with Xmake 3.1.2 and LLVM-MinGW 23.1.6, including building and running the applicable targets. Selected tests also exercised the compilation workspace boundary, template safety, preview and generation, trust, Xmake project editing, native tool detection, and the mixed-folder desktop workflow. The welcome screen, wizard and Explorer were rendered in both themes. Local captures are under `artifacts/phase7-welcome-shots`, `artifacts/phase7-wizard-shots` and `artifacts/phase7-explorer-shots`.

Four real MCP tests covered the native tool list, trust grant and revocation in a standalone process, the attached module snapshot and its limits, unresolved and ambiguous imports, and cancellation. The package smoke script now records trust for its disposable sample in the isolated state directory. Running it against the source-built executable confirmed that the missing-Xmake response includes the configured path and the override needed to resolve it.

The full solution build and locked restore passed. During managed isolation, a focused test exposed commands still listed under the inherited `Unit Tests` action group. After the filter was corrected, the targeted test build completed with zero warnings and errors. All four selected tests passed, with zero failures and skips. They checked native action, dock and settings isolation, Xmake editing and refresh, switching to a managed workspace in the verification profile, and native stdio MCP trust grant and revocation. The test runner used one worker at below-normal priority.

## Windows packaging

`tools/package-win.sh win-x64` produces the per-user installer and portable ZIP from the same publish. The script checks the published and packed inventories and starts a fresh portable copy with a separate data directory. Set `PITSTOP_RELEASE_VERSION=0.3.0-alpha.1` to build the candidate without a Git tag. Set `PITSTOP_PACKAGE_DIRECTORY=package-phase7-release-candidate` to keep the previous packages, and `PITSTOP_SERIAL_BUILD=1` to publish with one build worker. The Windows installer CI job checks native Open With registrations, the installed template catalog and MCP trust behavior. Application updates and release publication stay disabled.

| Windows x64 package | 0.2.0-alpha.1 bytes | 0.3.0-alpha.1 bytes | Reduction |
| --- | ---: | ---: | ---: |
| Installer | 242,380,873 | 157,516,907 | 35.0% |
| Portable ZIP | 225,817,294 | 143,597,259 | 36.4% |

The [candidate release notes](../release-notes/0.3.0-alpha.1.md) record hashes and build provenance.

The corrected portable and installed executables both passed the native MCP smoke. They indexed the `greetings` module, its exported and internal partitions, and libc++'s `std` module. Each opened the consumer, navigated to the module, saved a test edit, built without compiler errors or warnings, then ran and printed `Hello, modules!` with exit code 0. The portable probe also checked the missing-Xmake guidance and isolated state.

The Windows per-user install registered native C, C++, module and Lua Open With entries, the Start menu shortcut, and its installed template catalog. Existing file defaults stayed in place. One installed-executable regression test passed with zero failures and skips. It checked native MCP tool exposure, trust grant and revocation. Uninstall removed the application, shortcut and associations and preserved the user's PATH and existing Pitstop and Passenger state. This run used the workstation's Windows x64 account with no prior installed Pitstop package.

The packaged executable was rendered off-screen in both themes. The captured views cover welcome, the project menu, the template catalog, a C++23 module generation preview, the standards selector, Native Toolchains settings, the trust prompt, missing-Xmake guidance, Project Explorer, the Modules view, Build and Run. The editor captures show the evaluated Xmake project and clangd status. All captures are in `artifacts/package-phase7-release-candidate/screenshots/`. [Welcome in dark](../screenshots/native-welcome-dark.png) · [Welcome in light](../screenshots/native-welcome-light.png) · [Wizard in dark](../screenshots/native-wizard-dark.png) · [Wizard in light](../screenshots/native-wizard-light.png) · [Targets in dark](../screenshots/native-explorer-dark.png) · [Targets in light](../screenshots/native-explorer-light.png) · [Modules in dark](../screenshots/native-modules-dark.png) · [Modules in light](../screenshots/native-modules-light.png)

Configure a disposable copy of the [Hello Modules example](../../examples/hello-modules) outside Git worktrees to check an installed or extracted executable. Set the toolchain paths as in the example and provide a fresh state path. The smoke tool trusts only that disposable copy, queries its modules over MCP, edits it and checks its build and run output. For a missing-tool probe, set `PITSTOP_XMAKE` to a nonexistent executable before passing `--missing-tool`.

```powershell
python -I tools/alpha-package-smoke.py $exe $sample $newState --version 0.3.0-alpha.1 --native-product
```

## Remaining release checks

The complete inherited suites were outside the focused low-priority test run. Broader regression coverage and a complete review of keyboard and accessibility behavior across display sizes and scale factors remain open. The generic MCP workspace response still reports its inherited managed language-server status. The editor and module index show native status separately.

Managed-only source code and some of its dependencies still compile into the application. The tested native reference profile remains Windows x64. The packages are local, unsigned candidates. No release or tag has been published.
