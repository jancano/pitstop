# Native project system

`Pitstop.ProjectSystem` defines the evaluated workspace boundary used by clangd and module scanning. The workspace exposes provider identity, configuration, source and header membership, target dependencies, compiler languages, output paths and the two compilation databases. Providers resolve ambiguous command ownership. Xmake implements the contract on its immutable workspace snapshot, so callers can still use snapshot identity to reject obsolete results.

Xmake owns its `.objs` and `.gens` output conventions and its authoritative export. Clangd reads the source-oriented database and selects one target for a shared source. Module scanning reads both databases and preserves provider and configuration identity in its compilation contexts. The exported Xmake target models still carry package, script and toolchain details. Xmake workspace discovery, evaluation, edit previews and build and run currently remain in the existing Xmake integration.

## Verification

Locked restore and the complete solution build passed with zero warnings and errors. The focused module suite recorded five passes and skipped its opt-in scale benchmark. A synthetic provider resolved target ownership from its own output layout. Four focused headless checks passed, covering deterministic command selection, module navigation and saved-interface refresh with clangd, and real module scanning across Xmake configurations. These are focused checks for this boundary change.

## Future CMake provider

A CMake provider can use the File API codemodel for target identity, sources, dependencies, compile groups and artifacts. Presets can supply configuration and toolchain identity. A provider-owned mapping from compilation database commands to codemodel targets can resolve shared-source ownership. Once a supported generator emits usable commands, clangd and P1689 can consume those commands through the same boundary. CMake build operations, CTest discovery, external package integrations and project editing will need their own capabilities when concrete consumers exist. CMake evaluation and execution must go through workspace trust and the existing cancellation lifecycle.
