# Pitstop — Phase 7.5: Codebase Pruning & Identity Consolidation

## 1. Context and mission

Phase 7 — Native-first IDE Transformation — is complete.

Pitstop is an independent, open-source, modules-first C and C++ IDE derived from Passenger.

Its current capabilities include:

- Independent application identity, branding, packaging and state.
- Native project-system architecture.
- First-class Xmake integration.
- C and C++ language intelligence powered by clangd.
- C++23 and C++26 support on the validated reference toolchain.
- Native Build, Rebuild, Clean, Cancel, Run, Stop and Rerun.
- Compiler and linker diagnostics.
- Native run configurations.
- P1689 module dependency indexing.
- Module Explorer.
- Interactive module dependency graph.
- Contextual module import completion.
- Module navigation and potential impact analysis.
- Module creation.
- Declarative project templates.
- Extensible language-standard catalog.
- Native project creation.
- Native-first welcome experience and settings.
- MCP integration.

The repository still contains inherited Passenger projects, namespaces, dependencies, resources and potentially obsolete managed-development infrastructure.

The objective of this phase is to consolidate Pitstop as a genuinely independent codebase before introducing native debugging, Xrepo package management and future CMake support.

Pitstop remains implemented in C#, .NET and Avalonia.

Removing managed-development functionality from the product does not mean removing .NET as its implementation platform.

---

## 2. Fundamental pruning policy

The objective is to remove obsolete implementations, not valuable product capabilities.

Pitstop should become a professional, feature-rich native IDE comparable to CLion and Rider in overall development experience.

Native-first does not mean minimal-feature.

C++-first does not mean that the editor should only support C and C++ files.

Xmake-first does not mean that other native project systems should never be supported.

A feature must not be removed solely because:

- It originated in Passenger.
- It is implemented in C#.
- It uses .NET internally.
- Its current backend is managed.
- It supports languages other than C or C++.
- It is not required for basic C++ compilation.
- It is located in a directory containing managed-development code.
- It has no immediate use in the current milestone.

Many inherited components provide general-purpose capabilities valuable to native developers.

Distinguish between:

- Product functionality.
- User-facing presentation.
- Generic infrastructure.
- Language-specific implementation.
- Build-system-specific implementation.
- Runtime-specific backend.

Removing a managed backend must not automatically imply removing the corresponding user-facing capability or reusable infrastructure.

Repository size reduction is desirable, but must never be the primary measure of success.

Do not sacrifice useful functionality to achieve an arbitrary project count, line count or distribution size.

---

## 3. Establish the actual repository baseline

Before making changes:

- Read AGENTS.md and CLAUDE.md.
- Read the Phase 7 implementation report.
- Read all relevant architecture documents.
- Inspect the current repository and working tree.
- Inspect current branches and commits.
- Preserve existing user changes.
- Identify the actual current implementation of every relevant subsystem.
- Establish the current build and test baseline.

Do not assume that the repository still matches Passenger's original structure.

Some projects may already have been renamed, removed, replaced or refactored during Phase 7.

Record baseline measurements where practical:

- Number of production projects.
- Number of test projects.
- Number of tracked source files.
- Lines of production code.
- Lines of test code.
- Direct NuGet dependencies.
- Transitive NuGet dependencies.
- Bundled assemblies.
- Bundled executables.
- Clean build duration.
- Incremental build duration.
- Application startup duration.
- Portable distribution size.
- Installer size.

Use reproducible measurement methods.

Exclude generated files, build artifacts, third-party vendored code and historical documentation from source-code metrics, or report them separately.

Record the machine, configuration and toolchain used for performance measurements.

Do not fabricate measurements when a baseline cannot be obtained.

---

## 4. Feature-preservation audit

Create a complete inventory of inherited projects and major product capabilities.

For every subsystem, identify:

- User-facing functionality.
- Current consumers.
- Generic components.
- Managed-specific implementation.
- Native-specific implementation.
- Runtime dependencies.
- Build dependencies.
- Packaging dependencies.
- Existing tests.
- Potential native-development use cases.
- Replacement requirements.
- Removal risks.

Classify each subsystem as:

1. Preserve unchanged.
2. Preserve and rename.
3. Preserve and adapt.
4. Preserve reusable infrastructure while removing an obsolete backend.
5. Remove completely because it is exclusively managed and has no intended native use.
6. Defer because removal requires a product decision.

Do not classify a component as necessary simply because another component currently references it.

Determine whether the consumer is itself necessary.

Likewise, do not classify a component as unnecessary simply because its current consumer is managed.

Produce a feature-preservation matrix before beginning substantial removal work.

If an existing general-purpose user-facing feature would disappear, stop that specific removal, document the consequences and request a product decision.

Continue with unrelated safe removals.

---

## 5. Explicitly protected general-purpose IDE capabilities

The following capabilities must be preserved, renamed or adapted as appropriate.

### Core development experience

- Text editor.
- Text document infrastructure.
- Undo and redo.
- Syntax highlighting.
- Semantic highlighting.
- Code completion infrastructure.
- Navigation history.
- Search.
- Find and Replace.
- File operations.
- File watching.
- Local history.
- Vim mode.
- Code snippets.
- Keyboard shortcuts.
- Command palette.
- Docking.
- Tool windows.
- Themes.
- Settings.
- Notifications.
- Accessibility.
- Workspace trust.
- Recent projects.
- Repository cloning.
- Generic project navigation.

### Development infrastructure

- Git integration.
- Terminal.
- Integrated console.
- Child-process management.
- Process ownership.
- Process cancellation.
- Background execution.
- Generic diagnostics.
- Problems infrastructure.
- Language Server Protocol infrastructure.
- Debug Adapter Protocol infrastructure.
- MCP integration.
- Headless testing.
- Screenshot testing.

### Web and backend development

- HTTP Client.
- HTTP request files.
- HTTP variables and environments.
- Request authentication.
- Multipart request support.
- Response visualization.
- OpenAPI document browsing.
- OpenAPI-based requests.
- Generic endpoint testing.
- Generic request inspection.
- JSON tooling.
- YAML tooling.
- HTML tooling.
- CSS tooling.
- JavaScript tooling.
- TypeScript tooling.
- Generic web language-server integration.

### Containers and observability

- Docker integration.
- Docker Compose integration.
- Container Explorer.
- Container management.
- Image management.
- Registry integration.
- Container logs.
- Container statistics.
- Container process inspection.
- Generic OpenTelemetry infrastructure.
- OTLP support.
- Metrics dashboards.
- Log visualization.
- Trace visualization.
- Generic application monitoring.

### Additional productivity features

- Discord Rich Presence.
- BDD and Gherkin editing.
- Generic BDD presentation and navigation.
- Markdown preview where present.
- HTML preview.
- SVG preview.
- Image preview.
- Audio preview.
- Generic automatic-update infrastructure.

Do not remove these capabilities merely to make Pitstop more exclusively focused on C++.

Where a protected capability depends on an obsolete managed backend, preserve its reusable functionality and adapt or isolate that dependency.

Do not leave UI actions claiming native support when their native backend has not yet been implemented.

---

## 6. Remove exclusively managed UI development tooling

Physically remove managed UI development components that have no intended role in Pitstop.

Candidates include:

- WPF preview.
- MAUI preview.
- WPF application inspection.
- MAUI application inspection.
- Managed Avalonia application preview for user projects.
- XAML visual designers for managed user applications.
- Managed binding diagnostics.
- Managed XAML Hot Reload.
- Managed UI inspection protocols and runtime agents.
- Managed-only visual tree backends.

Potential project-removal candidates include:

- Passenger.WpfPreview.
- Passenger.MauiPreview.
- Passenger.Inspector.Wpf.
- Passenger.Inspector.Maui.
- Passenger.Xaml.
- Managed-specific portions of Passenger.Inspector.

Inspect their actual dependencies before deleting them.

Preserve genuinely reusable components, including:

- Generic tree visualization.
- Property grids.
- Value presentation.
- Image rendering.
- Generic inspection abstractions with concrete remaining consumers.
- Headless testing utilities.
- Screenshot infrastructure.
- System hotkey handling.
- Generic automation infrastructure.

Pay particular attention to linked source files included in other projects.

Do not remove Avalonia or XAML resources used to implement Pitstop itself.

Do not remove generic XML editing or highlighting simply because managed XAML tooling is being removed.

Do not remove generic testing infrastructure that happens to exercise Pitstop's own Avalonia interface.

---

## 7. Remove exclusively managed application-development tooling

Remove remaining functionality intended exclusively for managed .NET application development.

Candidates include:

- Entity Framework migrations.
- .NET Aspire orchestration.
- ASP.NET-specific development certificates.
- .NET User Secrets integration.
- Managed Blazor tooling.
- Managed Razor tooling.
- Managed launch-profile workflows.
- Managed application templates.
- Managed mobile workload installation.
- .NET-specific application publishing.
- Managed-only runtime configuration.
- Managed-only Hot Reload.

Preserve general-purpose alternatives and shared infrastructure.

For example:

- Remove ASP.NET-specific certificate workflows, but preserve generic HTTPS and certificate handling required by the HTTP Client.
- Remove .NET User Secrets integration, but preserve secure credential storage and generic environment-variable management.
- Remove managed launch-profile handling, but preserve native run configurations.
- Remove .NET Aspire orchestration, but preserve Docker Compose, service monitoring, logs, metrics and generic OpenTelemetry.
- Remove Razor-specific language tooling, but preserve HTML, CSS, JavaScript and TypeScript tooling.

Do not remove an entire directory when only some of its functionality is managed-specific.

---

## 8. Remove obsolete managed project-system and language backends

Phase 7 introduced the native project-system architecture and native project creation.

Remove remaining obsolete managed-development backends where their functionality has been replaced.

Candidates include:

- MSBuild evaluation for user projects.
- Managed project hosts.
- Visual Studio solution development workflows.
- Roslyn language-server integration.
- Roslyn extensions.
- Managed-only refactoring services.
- Managed project discovery.
- .NET SDK installation for user projects.
- dotnet new integration.
- Managed project templates.
- NuGet package-management workflows for user projects.

Potential removal candidates include:

- Passenger.Host.
- Passenger.Roslyn.Extension.
- Managed-specific portions of Passenger.Workspace.
- Managed-specific portions of Passenger.Languages.
- Managed-specific portions of Passenger.Packages.
- Managed-specific portions of Passenger.Lsp.

Inspect linked source files, generated code, shared protocol definitions and indirect dependencies before removing these components.

Preserve:

- Native project-system contracts.
- Xmake project evaluation.
- Workspace lifecycle.
- File watching.
- Source indexing.
- Generic language registration.
- TextMate integration.
- LSP transport.
- Generic completion and refactoring presentation.
- Generic diagnostics.
- File editing.
- Native compilation contexts.
- Generic workspace utilities.

C# source files may remain editable as ordinary text or syntax-highlighted source files.

Do not retain Roslyn or MSBuild user-project services merely to support the IDE's own source files.

Pitstop's implementation still requires C#, .NET, MSBuild, NuGet and Avalonia.

Preserve all infrastructure required to build, test, package and maintain Pitstop itself.

---

## 9. Discord Rich Presence

Discord Rich Presence is an explicitly desired Pitstop feature.

Do not remove it.

The inherited implementation provides useful functionality independent of managed development.

Preserve:

- Discord IPC transport.
- Windows named-pipe support.
- Unix socket support.
- Cross-platform endpoint discovery.
- Reconnection behavior.
- Activity update rate limiting.
- Background execution.
- Multiple-window coordination.
- Idle detection.
- Privacy settings.
- Clean shutdown.
- Existing regression tests.

Adapt the implementation to Pitstop's native architecture.

### Branding

Replace obsolete descriptions such as:

"An IDE for C# and .NET"

with appropriate Pitstop descriptions, for example:

"The modules-first C++ IDE"

Update:

- Application identity.
- User-facing descriptions.
- Branding assets.
- Button labels.
- Repository links.
- Settings terminology.
- Documentation.

### Native workspace integration

Replace managed solution-specific assumptions with native workspace information.

Use provider-neutral workspace information where appropriate.

Support existing privacy modes adapted to:

- Show Pitstop only.
- Show project name.
- Show project and active file.

Preserve elapsed activity time and idle behavior.

Where native workspace information is already available, consider including relevant activity details such as:

- C or C++.
- Language standard.
- Xmake project.
- Active source file.
- Active module.
- Building.
- Running.

Do not introduce substantial new Rich Presence functionality during this cleanup phase.

The immediate objective is preserving and correctly adapting the existing feature.

### Privacy

Default to a privacy-preserving activity that does not disclose project names, file names or source paths.

Preserve explicit user control over shared activity.

Do not expose:

- Absolute filesystem paths.
- Source-code contents.
- Secrets.
- Environment variables.
- Credentials.
- Private repository URLs without explicit user choice.

Preserve the ability to disable Rich Presence completely.

### Application registration

Use a Pitstop-specific Discord application ID and branding assets when available.

Do not reuse Passenger's Discord application ID.

If the Pitstop Discord application has not yet been registered:

- Preserve the implementation.
- Make application identity configurable.
- Keep the integration disabled until configured appropriately.
- Document the registration procedure.

Do not delete the feature because its production application ID is not yet available.

Do not automatically create external Discord resources or accounts.

---

## 10. Preserve generic HTTP and Web API tooling

Pitstop must remain suitable for native backend development.

C++ developers using frameworks such as Drogon, Crow or other HTTP libraries should benefit from the inherited HTTP development tools.

Preserve:

- HTTP Client.
- HTTP request files.
- Request variables.
- Environment configuration.
- Authentication support.
- Multipart requests.
- Response inspection.
- Request history where present.
- OpenAPI browsing.
- OpenAPI schemas.
- OpenAPI-based requests.
- Generic endpoint testing.
- Generic request presentation.

Remove exclusively managed implementations such as:

- Roslyn-based ASP.NET route discovery.
- Managed Minimal API inspection.
- ASP.NET-specific endpoint extraction.
- Managed-only request instrumentation.
- Entity Framework-specific SQL analysis.

Preserve generic request and SQL presentation where the data model is independent of managed instrumentation.

Do not remove the entire Endpoints or Requests subsystem merely because its original automatic discovery mechanism depended on Roslyn.

Do not implement Drogon-specific or Crow-specific route discovery in this phase.

---

## 11. Preserve Docker and container tooling

Docker and container development are directly relevant to native applications, microservices and Web APIs.

Preserve:

- Docker engine discovery.
- Docker contexts.
- Docker Compose support.
- Container Explorer.
- Container management.
- Image management.
- Volume management.
- Network management.
- Registry integration.
- Container logs.
- Container statistics.
- Container process inspection.
- Container file inspection.
- Dockerfile editing.
- Compose editing.
- Generic container configuration.
- Generic container security inspection where present.

Remove exclusively managed implementations such as:

- Building container images through the .NET SDK.
- Mounting managed build output through managed-specific workflows.
- CoreCLR-specific container debugging.
- Managed-only diagnostic helpers.
- Managed-specific container publishing.

Preserve generic process inspection and execution helpers used by containers.

Audit Passenger.ContainerTools before deciding whether to rename, split or consolidate it.

Do not assume that every container helper is exclusively managed.

Do not implement native remote-container debugging during this phase.

Preserve an architectural path for future LLDB-based container debugging.

---

## 12. Preserve OpenTelemetry and observability

OpenTelemetry is language-independent.

Native C++ applications can emit telemetry through OpenTelemetry and compatible protocols.

Preserve genuinely generic:

- OTLP infrastructure.
- Telemetry ingestion.
- Metrics dashboards.
- Log visualization.
- Trace visualization.
- Application monitoring.
- Service status presentation.
- Request visualization.
- Generic telemetry filtering.
- Generic telemetry data models.

Remove managed-specific dependencies on:

- Aspire.
- EventPipe.
- CoreCLR runtime instrumentation.
- ASP.NET-specific instrumentation.
- Entity Framework-specific instrumentation.

Do not delete generic observability capabilities merely because Aspire was previously their main source of telemetry.

Where a visualization depends on an obsolete backend, extract its reusable data and presentation boundaries.

Do not implement new C++ telemetry instrumentation during this cleanup phase.

Document any retained observability UI that requires a future native integration before becoming fully operational.

---

## 13. Preserve web and auxiliary language support

A professional C++ IDE must also support the surrounding files and technologies used by native applications.

Preserve existing useful language support for:

- C.
- C++.
- Lua.
- Xmake.
- JavaScript.
- TypeScript.
- HTML.
- CSS.
- JSON.
- YAML.
- XML.
- Markdown.
- Shell scripts.
- Dockerfiles.
- Configuration files.
- Gherkin.
- Other existing generic text formats.

Preserve language-server integrations that are independent of managed project-system services.

Do not remove TypeScript or JavaScript support because Pitstop is C++-first.

Native backend projects frequently include web frontends, build scripts, deployment configuration and documentation.

Preserve useful file previews, including existing HTML, SVG, image and audio previews.

Preserve GDScript editing while the broader Godot product decision remains open.

Do not introduce support for new languages during this phase.

---

## 14. Review Godot separately

Do not automatically classify all Godot integration as managed-development functionality.

Godot supports native C++ development through GDExtension and also uses GDScript.

Audit the existing Godot subsystem.

Distinguish between:

- Managed Godot C# integration.
- Managed assembly inspection.
- Managed-specific launch workflows.
- GDScript language intelligence.
- Scene inspection.
- Resource inspection.
- Shader editing.
- Game presentation.
- Game process management.
- Generic game profiling presentation.
- Generic game input.
- Editor communication.
- Potentially reusable GDExtension tooling.

Remove exclusively managed Godot implementations where their removal is safe and does not destroy generic functionality.

Preserve the remaining capabilities.

Do not implement GDExtension support during this phase.

Do not claim that inherited Godot integrations already support native C++ development unless verified.

If retaining or removing the broader Godot integration requires a substantial architectural or product decision:

- Document the available capabilities.
- Document its dependencies and maintenance costs.
- Describe possible native use cases.
- Present the available alternatives.
- Defer destructive removal until the product decision has been made.

Godot must not be deleted indiscriminately as part of managed-development pruning.

---

## 15. Review mobile-development infrastructure separately

Native C and C++ can be used for Android and iOS development.

Remove functionality exclusively related to:

- Managed MAUI workloads.
- .NET mobile application templates.
- Managed mobile debugging.
- Managed mobile application deployment.
- CoreCLR mobile runtime relays.

Audit potentially reusable infrastructure, including:

- Android device discovery.
- ADB integration.
- Device communication.
- Simulator discovery.
- Process management.
- Platform SDK discovery.
- Generic application launching.
- Device logs.
- Generic deployment helpers.

Preserve components with concrete native uses or existing generic consumers.

Do not implement Android NDK or iOS native project support during this phase.

Do not claim native mobile-development support.

If substantial mobile functionality has uncertain product value, document it and defer destructive removal.

---

## 16. Preserve debugger infrastructure for LLDB

Native debugging is the next major development milestone.

The inherited managed debugger backend should not remain part of Pitstop's product indefinitely.

However, the existing debugger UI and DAP infrastructure are valuable.

Preserve and decouple:

- DAP transport.
- Protocol messages.
- Debugger session lifecycle.
- Capability negotiation.
- Generic debugger events.
- Breakpoint storage.
- Breakpoint presentation.
- Conditional breakpoint presentation.
- Debugger toolbar.
- Stack-frame presentation.
- Thread presentation.
- Variable trees.
- Watches.
- Debug console.
- Source navigation.
- Debug session state.
- Generic expression evaluation requests.
- Debugger settings that remain applicable.
- Process ownership.

Remove assumptions that every debug session uses:

- CoreCLR.
- Managed assemblies.
- Managed type metadata.
- Managed thread semantics.
- Managed exception models.
- Managed object identities.
- Managed expression evaluators.
- Managed Hot Reload.
- Managed decompilation.

Potential removal candidates include:

- Passenger.Debugger.
- Passenger.Debugger.Interop.
- Passenger.DebugAdapter.
- Managed-specific debugger helpers.
- Managed debug relays.

Remove the managed debugger engine after separating the reusable infrastructure.

Do not automatically remove a protocol capability merely because it has not yet been exercised by LLDB.

Retain generic DAP support for future native debugging where meaningful.

Preserve deterministic debugger tests using fake DAP adapters when a real native backend is not yet available.

Remove obsolete managed debugger fixtures and tests that exclusively validate the deleted CoreCLR backend.

Do not implement LLDB integration during this phase.

Do not leave active native Debug commands that attempt to launch the removed managed debugger.

Do not pretend native debugging is available before its implementation.

Document the reusable integration points that future Pitstop.NativeDebug functionality will consume.

---

## 17. Preserve profiling and performance visualization

Native performance analysis is strategically important to Pitstop.

Audit the existing profiling subsystem.

Remove backend implementations exclusively dependent on:

- CoreCLR.
- EventPipe.
- Managed GC metadata.
- Managed heap dumps.
- Managed assembly metadata.
- Managed runtime metrics.

Potential removal candidates include managed-specific portions of Passenger.Profiler and associated runtime helpers.

Preserve reusable visualizations, including:

- Flame graphs.
- Call trees.
- Timelines.
- Frame charts.
- Generic memory visualization.
- Profiling tables.
- Filtering.
- Selection.
- Source navigation.
- Generic snapshot presentation.
- Performance result models where backend-neutral.

Do not delete complete profiling tool windows solely because their existing data source is managed.

Separate generic presentation from runtime-specific data acquisition.

Potential future native backends include perf, Tracy and LLVM-compatible profiling tools.

Do not implement these backends during this phase.

Do not retain large managed-only profiling dependencies merely to preserve visualizations.

Where presentation requires future native data, preserve its reusable implementation and keep unsupported product actions appropriately disabled.

---

## 18. Preserve package-management infrastructure for Xrepo

Xrepo management is an upcoming milestone.

Remove NuGet-specific product functionality, including:

- NuGet feeds for user projects.
- Managed package resolution.
- Managed target-framework compatibility.
- PackageReference mutation.
- NuGet restore management.
- Central Package Management editing.
- NuGet-specific dependency analysis.

Preserve reusable package-management presentation where suitable:

- Package search.
- Package lists.
- Version selectors.
- Package descriptions.
- Dependency presentation.
- Installed-package views.
- Update indicators.
- Progress reporting.
- Error reporting.
- Package-operation lifecycle.

Audit Passenger.Packages and the inherited package-manager UI separately.

Do not retain NuGet-specific models merely to preserve UI controls.

Do not remove generic package presentation that can be adapted to Xrepo.

Preserve a clear integration boundary for the future Xrepo Manager.

Do not implement Xrepo package search, installation, removal or update workflows during this phase unless they already exist from previous milestones.

Preserve existing declared-package information from Xmake.

Do not remove NuGet infrastructure required to restore and build Pitstop itself.

---

## 19. Preserve testing and BDD infrastructure

Remove managed-specific test discovery and execution backends where no longer required by the product.

Candidates include:

- VSTest integration for user projects.
- Managed test-runner integration.
- Managed xUnit, NUnit, MSTest and TUnit adapters.
- Managed test runtime hooks.
- Managed test project discovery.
- Managed test-specific debugging.

Preserve reusable Test Explorer infrastructure:

- Test trees.
- Test identifiers.
- Test result presentation.
- Test status.
- Test filtering.
- Source navigation.
- Execution lifecycle.
- Cancellation.
- Output presentation.
- Run and debug command infrastructure.

Future native testing will include Xmake test, Catch2, GoogleTest and eventually CTest.

Do not implement those integrations in this phase.

### BDD and Gherkin

Preserve generic BDD functionality, including:

- Gherkin editing.
- Feature-file syntax highlighting.
- Feature and scenario presentation.
- Generic feature navigation.
- Generic BDD models.
- Generic result presentation.

Remove managed-specific step-definition discovery, Roslyn integration and managed BDD execution backends where necessary.

Do not claim native step-definition discovery or execution without an implemented backend.

Do not delete the entire BDD subsystem merely because its original implementation supported managed testing.

### Pitstop's own test infrastructure

Pitstop itself is a .NET application.

Preserve:

- xUnit or other test frameworks used to test Pitstop.
- Headless Avalonia tests.
- Test fixtures required by generic IDE infrastructure.
- Deterministic protocol tests.
- Screenshot tests.
- Build and packaging tests.

Do not confuse removing managed testing functionality from the product with removing the tools used to test Pitstop itself.

---

## 20. Preserve independent application updates

Pitstop should eventually support its own application updates.

Remove Passenger-specific:

- Release endpoints.
- Update feeds.
- Application identifiers.
- Update channels.
- Release publication behavior.
- Upstream website deployment.
- Upstream application registration.

Preserve genuinely generic application-update infrastructure, including:

- Version checking.
- Release metadata.
- Package downloading.
- Update progress.
- Installer handoff.
- Platform-specific installation mechanisms.
- Update notifications.
- Release-note presentation.

Audit the existing App/Updates subsystem and Velopack integration.

Distinguish generic application updates from managed Hot Reload runtime update handlers.

Passenger.UpdateHandlers may be an obsolete managed runtime component rather than the application's updater. Verify its actual purpose before deciding whether to remove it.

Keep automatic updates disabled or correctly configured until Pitstop has its own verified release channel.

Never contact Passenger's automatic update services.

Do not publish releases, create release channels or enable automatic publication without explicit approval.

---

## 21. Candidate projects for removal

Inspect the actual current repository and consider removing remaining obsolete projects such as:

- Passenger.WpfPreview.
- Passenger.MauiPreview.
- Passenger.Inspector.Wpf.
- Passenger.Inspector.Maui.
- Passenger.Inspector.
- Passenger.Xaml.
- Passenger.Roslyn.Extension.
- Passenger.Host.
- Passenger.Debugger.Interop.
- Passenger.Debugger.
- Passenger.DebugAdapter.
- Passenger.Profiler.
- Passenger.Packages.
- Passenger.TestFrames.
- Passenger.UpdateHandlers.

These are audit candidates, not unconditional deletion instructions.

Some may already have been removed or renamed during Phase 7.

Some may contain linked source files, generic utilities or reusable components.

Extract necessary functionality before removing the original project.

Where a project's implementation is substantially reusable, retain and rename it rather than unnecessarily recreating the same functionality elsewhere.

Audit Passenger.Helper and Passenger.ContainerTools particularly carefully.

Their names alone are insufficient to determine whether they are obsolete.

Do not rename an exclusively obsolete project merely to retain it under the Pitstop identity.

---

## 22. Complete subsystem removal

Removing an obsolete subsystem means removing its obsolete dependency footprint.

For every removed implementation, inspect:

- Project references.
- Linked source files.
- Package references.
- Central package versions.
- Package lockfiles.
- Solution entries.
- Build properties.
- MSBuild targets.
- Embedded resources.
- Generated-code inputs.
- Runtime dependencies.
- Bundled executables.
- Tool discovery.
- Background processes.
- Startup initialization.
- Settings.
- State serialization.
- UI commands.
- Menus.
- Context menus.
- Keyboard shortcuts.
- MCP operations.
- Protocol messages.
- CI workflows.
- Packaging scripts.
- Documentation.
- Tests.
- Test fixtures.

Remove obsolete resources, services and dependencies completely where safe.

Do not merely hide obsolete managed functionality behind feature flags while retaining unnecessary background processes or packaged executables.

However, keeping a reusable subsystem dormant until its planned native backend becomes available is acceptable when its future purpose is concrete and documented.

Distinguish deliberately retained infrastructure from dead code.

After removing a subsystem, verify that its obsolete dependencies are not retained unnecessarily elsewhere.

---

## 23. Rename retained projects and namespaces

Consolidate the retained codebase under the Pitstop identity.

All retained first-party projects should consistently use Pitstop naming.

Rename the solution to:

`Pitstop.slnx`

Where applicable, rename:

| Existing name | Target name |
|---|---|
| Passenger.App | Pitstop.App |
| Passenger.Core | Pitstop.Core |
| Passenger.Editor | Pitstop.Editor |
| Passenger.Text | Pitstop.Text |
| Passenger.Languages | Pitstop.Languages |
| Passenger.Lsp | Pitstop.Lsp |
| Passenger.Dap | Pitstop.Dap |
| Passenger.Terminal | Pitstop.Terminal |
| Passenger.Vcs | Pitstop.Vcs |
| Passenger.Launcher | Pitstop.Launcher |
| Passenger.Analyzers | Pitstop.Analyzers |
| Passenger.Workspace | Pitstop.Workspace |
| Passenger.ContainerTools | Pitstop.ContainerTools |
| Passenger.Helper | Pitstop.Helper |
| Passenger.Tests | Pitstop.Tests |
| Passenger.UnitTests | Pitstop.UnitTests |
| Passenger.Notices | Pitstop.Notices |

Apply equivalent naming to additional retained projects.

Do not create unnecessary projects to match this illustrative list.

Preserve and consolidate existing native projects such as:

- Pitstop.Xmake.
- Pitstop.Cpp.
- Pitstop.Modules.
- Pitstop.ProjectSystem.
- Pitstop.Templates.

Use the actual current project names if Phase 7 established a different justified architecture.

### Rename scope

Update all relevant:

- Directories.
- Project filenames.
- Solution filenames.
- Namespaces.
- Using directives.
- Assembly names.
- Root namespaces.
- Internal type references.
- Project references.
- InternalsVisibleTo declarations.
- Resource identifiers.
- XAML namespace references.
- Linked source paths.
- Reflection-based references.
- Assembly-qualified type references.
- Source generators.
- Generated-code inputs.
- Analyzer configuration.
- Test namespaces.
- Test assembly names.
- Build scripts.
- Packaging scripts.
- CI workflows.
- Tool configuration.
- MCP launcher configuration.
- Documentation references.

Use mechanical, independently reviewable commits for widespread renames.

Do not combine mechanical renaming with unrelated formatting or functional rewrites.

Do not blindly replace every occurrence of Passenger.

Preserve external protocol identifiers, third-party identifiers and persisted compatibility identifiers when changing them would break interoperability or user state.

Keep a documented list of justified remaining identifiers.

---

## 24. Consolidate project boundaries

After pruning, review whether the remaining project structure is appropriate.

Eliminate:

- Empty projects.
- Projects containing only obsolete compatibility code.
- Redundant wrapper assemblies.
- Unnecessary dependency cycles.
- Duplicate abstractions.
- Infrastructure split across projects without meaningful boundaries.

Preserve well-defined boundaries between:

- Core infrastructure.
- Editor and text infrastructure.
- Language intelligence.
- Protocol infrastructure.
- Native project systems.
- C++ intelligence.
- Module intelligence.
- Template generation.
- Application UI.
- Generic development tools.

Do not collapse the entire IDE into a monolithic project.

Do not maximize project count unnecessarily.

Do not introduce speculative abstractions merely to preserve small fragments of inherited code.

Use cohesion, dependency direction, testability and maintainability to determine the final structure.

Do not impose an arbitrary target number of projects.

---

## 25. Preserve the native project-system architecture

Phase 7 established the foundation for Xmake and future native project-system providers.

Preserve:

- Provider-neutral workspace contracts.
- Xmake-specific capabilities.
- Evaluated target metadata.
- Target identities.
- Source membership.
- Target dependencies.
- Configuration isolation.
- Toolchain information.
- Compilation contexts.
- Authoritative compilation databases.
- Derived semantic compilation databases.
- Native build operations.
- Native run configurations.
- P1689 module indexing.
- Module provider resolution.
- Contextual import completion.
- Module creation.
- Declarative templates.
- Extensible language-standard catalog.
- Native project creation.
- Native project editing.
- Native toolchain settings.

Xmake remains the preferred project system and authoritative source of build configuration.

Do not reintroduce Xmake-specific assumptions into provider-neutral components during cleanup.

Do not sacrifice Xmake-specific functionality in the name of generic architecture.

Future CMake support must remain architecturally possible.

Do not implement CMake during this phase.

---

## 26. Preserve C++ module intelligence

Modules-first remains Pitstop's defining product philosophy.

Do not regress:

- Primary module interfaces.
- Interface partitions.
- Internal partitions.
- Implementation units where supported.
- Cross-target modules.
- Standard library modules.
- import std.
- P1689 scanning.
- Module Explorer.
- Module dependency graph.
- Module navigation.
- Contextual import completion.
- Potential impact analysis.
- Ambiguous and unresolved provider handling.
- Configuration isolation.
- Saved-interface semantic recovery.
- Preservation of unsaved buffers.
- Native module creation.

Preserve the distinction between confirmed semantic information and inferred or unavailable information.

Do not simplify the module index in ways that discard compilation context or authoritative Xmake module bindings.

Preserve the existing documented limitations.

Do not claim that the cleanup resolves unsaved cross-module semantic propagation, incremental per-file scanning or implementation-unit ownership unless separately verified.

---

## 27. Application-state compatibility

Review the effect of renaming assemblies, namespaces and projects on existing Pitstop user state.

Preserve where appropriate:

- User settings.
- Recent projects.
- Workspace trust.
- Native run configurations.
- Toolchain configuration.
- Breakpoints.
- Window layouts.
- Theme preferences.
- Keyboard shortcuts.
- Local templates.
- Native project state.
- Discord privacy preferences.
- HTTP Client settings.
- Container settings.
- Generic observability settings.
- Other retained feature preferences.

Identify serialized type names and assembly-qualified references.

Provide targeted migration logic only where necessary.

Obsolete managed-only settings may be removed or ignored safely.

Do not discard valid native or general-purpose user settings.

Do not reset security-related settings as an incidental consequence of renaming.

Keep Pitstop isolated from Passenger's settings, caches, logs and application state.

Do not read or modify Passenger's user state during migration.

---

## 28. Dependency cleanup

Audit direct and transitive package dependencies.

Remove packages exclusively required by deleted implementations.

Review:

- Roslyn packages.
- Managed MSBuild workspace packages.
- NuGet client packages.
- Managed debugger dependencies.
- Managed profiler dependencies.
- Managed XAML inspection dependencies.
- Mobile workload integration dependencies.
- Managed test-runner packages.
- Aspire dependencies.
- Entity Framework tooling.
- Obsolete serialization libraries.
- Obsolete protocol libraries.
- Redundant utilities.

Preserve packages required by:

- Pitstop's implementation.
- Generic editor functionality.
- Avalonia.
- LSP.
- DAP.
- MCP.
- Git.
- Terminal.
- HTTP tooling.
- Docker tooling.
- OpenTelemetry.
- Generic updates.
- File previews.
- Testing.
- Packaging.
- Other explicitly retained features.

Do not remove a dependency solely because its name contains Microsoft, .NET, NuGet or MSBuild.

Update central package versions and lockfiles correctly.

Run locked restore after dependency cleanup.

Preserve all legally required third-party attribution.

Review generated notices and remove obsolete dependency entries only where appropriate, without removing attribution for retained code.

---

## 29. Packaging and distribution cleanup

Audit the actual contents of Pitstop's distribution.

Remove obsolete runtime components such as:

- Managed language servers.
- Managed project hosts.
- Managed debugger binaries.
- Managed profiler helpers.
- WPF preview executables.
- MAUI preview executables.
- Managed inspection helpers.
- Managed mobile relays.
- Managed test adapters.
- Managed project-template resources.
- Obsolete managed Hot Reload helpers.
- Unused protocol assets.

Preserve runtime components required by retained functionality.

In particular, do not remove:

- Generic web language servers.
- HTTP tooling.
- Image and audio preview dependencies.
- Container helpers.
- Terminal dependencies.
- MCP infrastructure.
- Generic updater components.
- Other explicitly protected features.

Build clean distributions without relying on stale build artifacts.

Verify packaged contents directly.

Do not assume that removing a project reference automatically removes all associated packaged files.

Validate:

- Windows portable distribution.
- Windows installer.
- Application startup.
- Native workspace opening.
- Native project creation.
- Toolchain discovery.
- C++ language intelligence.
- Module Explorer.
- Native Build and Run.
- Generic retained tools.
- Application shutdown.
- Process cleanup.
- State isolation.

Measure and report package-size changes.

Do not treat package-size reduction as justification for deleting protected capabilities.

Do not claim Linux or macOS verification unless performed on those platforms.

---

## 30. MCP cleanup and preservation

Audit all MCP operations and descriptions.

Remove operations that exist exclusively for intentionally deleted managed-development functionality.

Preserve and adapt useful operations covering:

- Native workspace inspection.
- Native project creation where implemented.
- File editing.
- Diagnostics.
- Completion.
- Navigation.
- References.
- Formatting.
- Build.
- Run.
- Module workspace inspection.
- Dependency graphs.
- Potential impact analysis.
- Git.
- Terminal.
- IDE screenshots.
- Generic HTTP tooling.
- Generic container inspection.
- Other retained capabilities.

Preserve MCP protocol infrastructure.

Do not introduce a separate model for coding agents.

The IDE and MCP must consume the same native workspace models.

Remove obsolete managed protocol fields only when safe.

Respect existing trust, cancellation, permission and process-ownership boundaries.

Validate retained MCP operations through an actual client connection.

Do not advertise MCP operations for native debugging, package management or testing before those implementations exist.

---

## 31. UI and terminology audit

Audit the complete application experience after pruning and renaming.

Review:

- Welcome screen.
- New Project.
- Project Explorer.
- Module Explorer.
- Main menu.
- Toolbar.
- Context menus.
- Command palette.
- Keyboard shortcuts.
- Settings.
- Status bar.
- Notifications.
- Dialogs.
- Problems.
- Build.
- Run.
- Debug.
- Packages.
- Tests.
- Profiling.
- HTTP Client.
- Containers.
- Observability.
- Discord Rich Presence.
- File associations.
- Documentation links.

Remove commands that refer exclusively to removed implementations.

Preserve commands and settings for retained features.

Use native project terminology where appropriate.

Do not mechanically replace words when their meanings differ.

Ensure unsupported native functionality does not accidentally invoke removed managed backends.

Keep dormant future native tools appropriately unavailable until their backends exist.

Preserve visual consistency, theme tokens, keyboard navigation, accessibility and existing interaction patterns.

Do not redesign the entire UI during cleanup.

Do not remove useful polish or productivity features merely to simplify the interface.

---

## 32. Testing strategy

Maintain deterministic unit tests, headless integration tests and real native-toolchain tests.

Classify inherited tests into:

1. Retain unchanged.
2. Adapt to renamed infrastructure.
3. Adapt to native functionality.
4. Adapt to a replacement fake backend.
5. Remove because they exclusively validate intentionally deleted functionality.
6. Defer because a product decision remains unresolved.

Do not delete failing generic tests to obtain a passing suite.

Do not classify a test as obsolete simply because it originated in Passenger.

Remove obsolete test fixtures and dependencies only when their corresponding functionality has genuinely been removed.

### Core regression coverage

Preserve coverage of:

- Editor.
- Text documents.
- Undo and redo.
- Search.
- File operations.
- Local history.
- Git.
- Terminal.
- Docking.
- Themes.
- Settings.
- Workspace trust.
- Process ownership.
- Cancellation.
- LSP.
- DAP.
- MCP.
- Generic diagnostics.
- Headless rendering.
- Screenshot infrastructure.
- Accessibility where covered.

### Protected feature regression coverage

Preserve or adapt existing tests for:

- Discord IPC.
- Discord activity privacy.
- Discord reconnection.
- Discord idle handling.
- Discord shutdown.
- HTTP Client.
- OpenAPI.
- Request files.
- Generic endpoint tooling.
- Docker.
- Docker Compose.
- Container Explorer.
- Container logs and statistics.
- OpenTelemetry.
- Metrics and trace presentation.
- JavaScript and TypeScript language services.
- HTML and CSS language services.
- JSON and YAML tooling.
- BDD and Gherkin.
- File previews.
- Generic update infrastructure.

Where an existing test depends on a removed managed backend, adapt the test to exercise retained generic behavior through appropriate deterministic data or a fake backend.

### Native regression coverage

Preserve all existing native tests covering:

- Provider-neutral project systems.
- Xmake workspace evaluation.
- Target discovery.
- Source membership.
- C and C++ language registration.
- Lua editing.
- clangd completion.
- Diagnostics.
- Hover.
- References.
- Navigation.
- Formatting.
- Rename where implemented.
- C++23.
- C++26.
- Named modules.
- Exported interface partitions.
- Internal partitions.
- Cross-target modules.
- import std.
- P1689 scanning.
- Module Explorer.
- Module dependency graph.
- Contextual import completion.
- Module creation.
- Saved-interface semantic recovery.
- Compilation-context isolation.
- Compilation database preservation.
- Declarative templates.
- Language-standard catalog.
- Native project creation.
- Native project editing.
- Build.
- Rebuild.
- Clean.
- Cancellation.
- Run.
- Stop.
- Rerun.
- Process-tree cleanup.

### Rename-specific tests

Verify that renaming does not break:

- Reflection.
- Resource loading.
- XAML bindings.
- Assembly discovery.
- Settings serialization.
- State migration.
- Analyzer behavior.
- Source generation.
- Protocol integration.
- Test discovery.
- MCP launchers.
- Packaging.
- Platform-specific startup.

### Validation requirements

Run:

- Locked restore.
- Complete clean solution build.
- Complete applicable unit test suite.
- Complete applicable headless integration suite.
- Real native toolchain tests.
- Actual MCP connection tests.
- Packaging smoke tests.

Use the validated Windows x64 reference profile from the previous milestones.

Native tests must report explicit skip reasons when required tools are unavailable.

Report passed, failed, skipped and timed-out tests separately.

Do not report selected regression tests as complete validation.

Investigate failures rather than suppressing them.

---

## 33. Visual verification

Capture and inspect light and dark screenshots of the consolidated application.

Include:

- Native welcome screen.
- New Project wizard.
- Native Project Explorer.
- Module Explorer.
- Module dependency graph.
- C++ editor.
- Native Build and Run.
- Problems.
- Native toolchain settings.
- HTTP Client.
- Container Explorer.
- Discord Rich Presence settings.
- Other affected retained tool windows.

Where an existing integration requires an external service that is unavailable, validate its settings, lifecycle and deterministic presentation without claiming real-service verification.

Do not fabricate Discord activity screenshots or claim that a production Discord application ID has been configured if it has not.

Verify that obsolete managed-development commands and settings are absent.

Verify that protected general-purpose features remain discoverable.

Follow existing theme and screenshot conventions.

Do not introduce unrelated visual changes.

---

## 34. Preserve attribution and upstream history

Pitstop is an independent product derived from Passenger.

Preserve:

- Passenger's MIT license.
- Original copyright notices.
- Third-party notices.
- Source attribution.
- Git history.
- Relevant historical documentation.
- References to the upstream project.

Do not mechanically replace Passenger with Pitstop in:

- Historical documents.
- Original copyright statements.
- Third-party attribution.
- Historical release notes.
- References to upstream source code.
- External protocol identifiers.

Keep upstream configured as a reference.

Document a selective upstream integration strategy.

Future upstream changes should be reviewed for relevance to Pitstop, particularly:

- Editor improvements.
- Terminal improvements.
- Git improvements.
- LSP improvements.
- DAP improvements.
- Avalonia updates.
- Performance improvements.
- Accessibility.
- Security fixes.
- Generic HTTP tooling.
- Container tooling.
- Discord integration.
- Other retained capabilities.

Do not automatically merge unrelated managed-development functionality.

Do not reintroduce deleted managed backends through indiscriminate upstream synchronization.

---

## 35. Recommended implementation sequence

Organize the work into coherent, independently verifiable subphases.

### Phase 7.5A — Inventory and preservation audit

- Inspect the actual Phase 7 implementation.
- Establish build and test baselines.
- Record repository and distribution metrics.
- Create a dependency map.
- Create the feature-preservation matrix.
- Identify safe removals.
- Identify reusable infrastructure.
- Identify deferred product decisions.

### Phase 7.5B — Safe obsolete subsystem removal

- Remove WPF and MAUI preview tooling.
- Remove obsolete managed inspection backends.
- Remove Entity Framework tooling.
- Remove managed-only development certificate workflows.
- Remove managed-only User Secrets workflows.
- Remove other isolated obsolete implementations.
- Clean associated references, dependencies, resources and tests.

Preserve protected general-purpose functionality.

### Phase 7.5C — Managed backend extraction and removal

- Separate debugger UI and DAP infrastructure from CoreCLR.
- Remove obsolete managed debugger implementations.
- Preserve profiler visualizations while removing managed profiling backends.
- Preserve package-management presentation while removing NuGet product functionality.
- Preserve Test Explorer and BDD while removing managed execution backends.
- Remove remaining obsolete MSBuild and Roslyn user-project services.
- Remove obsolete managed templates and project hosts.

Do not remove useful general-purpose features.

### Phase 7.5D — Protected feature adaptation

- Adapt Discord Rich Presence to native workspace terminology.
- Preserve HTTP and OpenAPI functionality.
- Preserve generic container tooling.
- Preserve generic OpenTelemetry.
- Preserve web language services.
- Preserve generic update infrastructure.
- Review Godot and mobile tooling separately.
- Remove remaining managed assumptions from retained features.

Do not implement major new integrations.

### Phase 7.5E — Identity consolidation

- Rename retained projects.
- Rename first-party namespaces.
- Rename the solution to Pitstop.slnx.
- Update references.
- Update resources.
- Update scripts.
- Update CI.
- Update packaging.
- Consolidate project boundaries where justified.

Keep mechanical renames independently reviewable.

### Phase 7.5F — Dependency and packaging cleanup

- Remove unused dependencies.
- Regenerate lockfiles.
- Review bundled assemblies and executables.
- Verify retained runtime dependencies.
- Build clean distributions.
- Measure package-size changes.

### Phase 7.5G — Stabilization

- Run complete applicable test suites.
- Verify native regressions.
- Verify protected feature regressions.
- Validate state compatibility.
- Validate MCP through an actual connection.
- Inspect screenshots.
- Validate packaging.
- Record final metrics.
- Document remaining limitations and deferred decisions.

Complete each subphase using coherent commits.

Keep the solution buildable throughout the migration.

Do not create one enormous unreviewable commit.

---

## 36. Documentation

Create or update:

`docs/architecture/codebase-consolidation.md`

Document:

- Removed projects.
- Removed subsystems.
- Retained projects.
- Renamed projects.
- Consolidated projects.
- Extracted reusable infrastructure.
- Final dependency architecture.
- Removed dependencies.
- Remaining implementation dependencies.
- Justified remaining legacy components.
- State migration.
- Packaging changes.
- Test results.
- Before-and-after repository metrics.
- Before-and-after distribution metrics.
- Performance measurements.
- Upstream integration strategy.
- Integration points for future LLDB.
- Integration points for future Xrepo.
- Integration points for future native testing.
- Integration points for future native profiling.
- Future CMake compatibility.
- Deferred product decisions.

### Feature-preservation matrix

For each retained feature, report:

- Original capability.
- Original backend.
- Post-migration capability.
- Post-migration backend.
- Preserved infrastructure.
- Removed managed dependencies.
- Current implementation status.
- Verification performed.
- Remaining limitations.
- Future native integration requirements where applicable.

Explicitly include:

- Discord Rich Presence.
- HTTP Client.
- OpenAPI.
- Docker and Compose.
- Container Explorer.
- OpenTelemetry.
- Profiling.
- Debugger UI.
- Package-management UI.
- Test Explorer.
- BDD and Gherkin.
- Godot.
- Mobile tooling.
- Web language services.
- File previews.
- Automatic updates.

Update:

- README.md.
- AGENTS.md.
- CLAUDE.md.
- CONTRIBUTING.md.
- CI documentation.
- Packaging documentation.
- Affected architecture documents.
- MCP documentation.
- Roadmap.

Ensure current build instructions use the renamed solution and project structure.

Documentation must describe actual implemented and verified behavior.

---

## 37. Constraints and non-goals

The following constraints apply throughout this phase:

- Pitstop remains a C#, .NET and Avalonia application.
- Pitstop remains a professional, feature-rich native IDE.
- C++-first remains the primary development focus.
- Modules-first remains the defining philosophy.
- Xmake remains the preferred project system.
- Future CMake support must remain architecturally possible.
- No CMake provider implementation yet.
- No LLDB implementation yet.
- No Xrepo package-manager implementation yet.
- No native test-runner implementation yet.
- No native profiling backend implementation yet.
- No GDExtension implementation yet.
- No Android NDK or native iOS implementation yet.
- No unrelated feature development.
- No proprietary dependencies.
- No custom C++ parser.
- No LLVM modifications.
- No arbitrary Lua parsing using regular expressions.
- Do not remove general-purpose IDE capabilities indiscriminately.
- Do not remove Discord Rich Presence.
- Do not remove generic web tooling.
- Do not remove generic HTTP or OpenAPI tooling.
- Do not remove generic Docker or container tooling.
- Do not remove generic OpenTelemetry infrastructure.
- Do not remove generic BDD functionality.
- Do not remove generic automatic-update infrastructure.
- Do not remove Godot or mobile infrastructure without the required product decision.
- Do not remove reusable debugger infrastructure.
- Do not remove reusable profiling visualizations.
- Do not remove reusable package-management infrastructure.
- Do not remove reusable testing infrastructure.
- Do not retain obsolete managed implementations without concrete justification.
- Do not regress native project creation.
- Do not regress the declarative template engine.
- Do not regress Xmake integration.
- Do not regress clangd.
- Do not regress Module Intelligence.
- Do not regress native Build and Run.
- Do not weaken workspace trust.
- Do not weaken process ownership.
- Do not execute project-controlled tools before trust.
- Do not overwrite user-owned compilation databases.
- Do not discard unsaved editor buffers.
- Do not discard existing user changes.
- Do not sacrifice user-facing capabilities to reduce repository size.
- Keep the application buildable.
- Keep the IDE responsive.
- Preserve licensing, attribution and Git history.
- Do not publish releases or tags without explicit approval.

---

## 38. Acceptance criteria

Phase 7.5 is complete when the following criteria have been evaluated:

### Codebase consolidation

1. Exclusively managed-development implementations have been physically removed wherever safe.
2. WPF and MAUI preview projects are absent.
3. Obsolete managed inspection components are absent from clean distributions.
4. Obsolete managed project-system backends are removed or explicitly isolated with justified remaining dependencies.
5. MSBuild and Roslyn are no longer required as user-project services.
6. The managed debugger backend has been removed or any unavoidable remaining dependency is explicitly justified.
7. Unused managed runtime dependencies are removed.
8. Retained first-party projects consistently use Pitstop naming.
9. The solution is named Pitstop.slnx.
10. First-party namespaces consistently use Pitstop naming.
11. Remaining Passenger identifiers are limited to justified compatibility, history, attribution or external interoperability.
12. Obsolete references, resources, services and packaging tasks are removed.

### Feature preservation

13. Discord Rich Presence implementation remains available.
14. Discord branding and workspace terminology are adapted to Pitstop.
15. Passenger's Discord application ID is not reused.
16. Discord privacy settings and regression coverage remain intact.
17. Generic HTTP and OpenAPI functionality remains available.
18. Docker and Docker Compose functionality remains available.
19. Container Explorer functionality remains available.
20. Generic observability and OpenTelemetry infrastructure remains available.
21. Existing useful web language services remain available.
22. Generic BDD and Gherkin functionality remains available.
23. Generic file previews remain available.
24. Generic automatic-update infrastructure remains available without contacting Passenger update services.
25. Reusable debugger UI and DAP infrastructure remain available for LLDB.
26. Reusable profiling visualizations remain available for native backends.
27. Reusable package-management presentation remains available for Xrepo.
28. Reusable testing infrastructure remains available for native testing.
29. Godot and mobile infrastructure have been audited without indiscriminate deletion.
30. No protected general-purpose functionality has disappeared without an explicit product decision.

### Native architecture

31. Phase 7 project creation remains functional.
32. Declarative templates remain functional.
33. Dynamic language-standard selection remains functional.
34. Native project editing remains functional.
35. Xmake workspace evaluation remains functional.
36. clangd language intelligence remains functional.
37. Module Explorer and dependency indexing remain functional.
38. Module navigation and contextual import completion remain functional.
39. Native Build and Run remain functional.
40. Configuration isolation remains intact.
41. User-owned compilation databases remain preserved.
42. Future CMake integration remains architecturally possible.

### State and security

43. Existing valid Pitstop user settings remain compatible wherever appropriate.
44. Passenger's user state remains untouched.
45. Workspace trust guarantees remain intact.
46. Process ownership and cancellation guarantees remain intact.
47. Retained MCP functionality remains operational.

### Validation and distribution

48. Locked restore passes.
49. The complete solution builds with zero errors and warnings.
50. Applicable regression suites pass, with failures and skips documented accurately.
51. Real native toolchain tests pass on the validated reference profile.
52. Protected feature tests pass or have explicitly documented limitations.
53. MCP has been verified through an actual connection.
54. Light and dark screenshots have been captured and inspected.
55. Windows portable distribution has been verified.
56. Windows installer has been verified, distinguishing package creation from actual installation.
57. Removed runtime components are absent from clean distributions.
58. Before-and-after repository metrics are recorded.
59. Before-and-after distribution metrics are recorded.
60. Remaining technical debt and deferred product decisions are documented.
61. Licensing and attribution remain intact.
62. No unrelated functionality has been introduced.

---

## 39. Final report

Provide a concise but complete implementation report.

Include:

### Repository changes

- Projects deleted.
- Projects renamed.
- Projects consolidated.
- Obsolete subsystems removed.
- Remaining legacy components and justification.
- Final project structure.

### Feature preservation

- Protected features retained.
- Protected features adapted.
- Managed backends removed.
- Reusable infrastructure extracted.
- Dormant features awaiting native backends.
- Discord Rich Presence implementation status.
- Godot audit results.
- Mobile-development audit results.
- Deferred product decisions.

### Dependencies and packaging

- Direct dependencies removed.
- Transitive dependencies removed.
- Bundled assemblies removed.
- Bundled executables removed.
- Clean packaging results.
- Installer verification.
- Portable distribution verification.
- Distribution-size comparison.

### Verification

- Build results.
- Locked restore results.
- Unit test results.
- Headless integration test results.
- Native toolchain test results.
- Protected feature regression results.
- MCP verification.
- State compatibility.
- Screenshot verification.
- Platform-specific restrictions.

### Metrics

- Production project count.
- Test project count.
- Source file count.
- Production lines of code.
- Test lines of code.
- Build duration.
- Startup duration.
- Distribution size.

Distinguish actual measurements from estimates and unavailable results.

### Remaining work

- Technical debt.
- Retained compatibility identifiers.
- Unverified capabilities.
- Deferred product decisions.
- Integration points for LLDB.
- Integration points for Xrepo.
- Integration points for native testing.
- Integration points for native profiling.
- Future CMake provider considerations.

Do not claim complete validation when only selected tests have been executed.

Do not begin the native debugging milestone until the Phase 7.5 acceptance criteria have been evaluated.

---

## 40. Final principle

Pitstop must emerge from this phase as a clean, cohesive, feature-rich native C/C++ IDE with its own identity and architecture.

The objective is not to build a minimal C++ editor.

The objective is not to preserve Passenger indefinitely.

The objective is to remove obsolete managed implementations while preserving the richness, polish, productivity features and general-purpose infrastructure that make Passenger a valuable foundation.

Remove obsolete implementations, not valuable product capabilities.

Build Pitstop.

**Pitstop — The modules-first C++ IDE.**
