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
