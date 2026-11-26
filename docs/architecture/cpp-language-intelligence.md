# C++ language intelligence

Pitstop connects clangd to evaluated Xmake workspaces. The reference profile is Windows x64, Xmake 3.1.2, LLVM-MinGW 23.1.6, and libc++. [Phase 1 toolchain results](cpp-toolchain.md) · [Phase 2 workspace architecture](xmake-workspaces.md)

## Setup

Configure the project as described in the Xmake workspace guide. Launch Pitstop with LLVM-MinGW's `bin` directory on PATH, set `PITSTOP_LLVM_BIN` to that directory, or set `PITSTOP_CLANGD` to the full executable path. The executable override takes precedence. Windows discovery also checks the conventional LLVM install directory. Open the folder, grant workspace trust, and open a native source file. The language status shows clangd, the active target context when available, and startup, refresh, or failure details.

## Architecture

- **Offline editing.** The language registry identifies C, C++, and Lua files, including `xmake.lua` and conventional module suffixes. TextMate handles lexical highlighting and bracket matching, with the existing editor's comment commands, indentation, and indentation-based folding. Ambiguous `.h` files use an explicit compile command or the owning targets' compiler kinds when they agree. A lowercase `.h` otherwise defaults to C.
- **Tool discovery and lifecycle.** `Pitstop.Cpp` probes clangd's version and module flag. It starts the server through Passenger's child-process runner and stdio LSP transport after trust. The app attaches clangd to the existing editor and shares its text documents, completion, diagnostics, outline, navigation, and edit application. Trust revocation, workspace closure, and Xmake context changes cancel the old generation and disconnect its documents. One automatic recovery is allowed per Xmake snapshot after a server crash.
- **Compilation contexts.** The Xmake snapshot supplies the source-oriented semantic database. For sources shared across targets, Pitstop chooses the first identifiable target by ordinal name and keeps its commands in Xmake's original order. Selection uses the target segment of Xmake object or generated output paths. When a target cannot be identified, a stable output and command ordering selects one row. The selection is written to a private clangd view and displayed in the language status. The Xmake export, the unfiltered semantic database, and the project's own compilation database stay intact.
- **Module refresh.** The compilation commands identify module inputs through module compile and precompile flags. Saves of matching inputs coalesce into a clangd restart. The new session opens every attached document from its current editor snapshot, including unsaved buffers. It can recover the new exported symbol from the saved file without invoking Xmake build. Diagnostics are accepted only from the currently attached LSP document for the current text version. The former server's requests are canceled, and its editor popups and decorations are cleared.
- **Project execution boundary.** The app checks workspace trust before discovery and startup. The clangd invocation disables project YAML configuration and supplies no compiler-driver execution allowlist. Compilation contexts come from trusted Xmake evaluation. Neither Lua highlighting nor ordinary file-type detection evaluates a project.

## Scope and limits

The editor routes clangd completion, hover, definition and declaration, references, signature help, document and workspace symbols, semantic tokens, inlay hints, rename, code actions, and formatting through the inherited controls. On the reference version, completion documentation is returned with the completion item. Semantic highlighting uses full-document tokens, as advertised by clangd. The tests below distinguish protocol checks from UI flows.

Cross-module changes are refreshed after saving a module input recognized in the compilation database. Unsaved provider edits can still leave consumers with stale module information. Newly added module inputs become eligible after Xmake refreshes their compile commands. Header commands may be inferred by clangd when Xmake has no command for a header. Module navigation follows the locations clangd reports, which can favor the interface over a separate definition in an implementation unit. The first target selection is automatic. It has no interactive per-file selector.

`xmake_ls` 0.5.0 initialized and shut down over stdio on Windows x64. A completion request on the fixture returned an empty result in the smoke probe. Its initialization also detects Xmake on PATH. Keep Lua editing available offline while Xmake API completion and project-tool behavior are validated separately. [xmake_ls 0.5.0](https://github.com/CppCXY/xmake_ls/releases/tag/0.5.0)

## Reproduce

```powershell
just build
dotnet test tests/Passenger.Tests --no-build --filter 'FullyQualifiedName~CppLanguageTests|FullyQualifiedName~CppLanguageLifecycleTests'

$env:PITSTOP_XMAKE = '<xmake>/xmake.exe'
$env:PITSTOP_TEST_LLVM_MINGW = '<llvm-mingw>'
$env:PITSTOP_CLANGD = "$env:PITSTOP_TEST_LLVM_MINGW/bin/clangd.exe"
$env:Path = "$env:PITSTOP_TEST_LLVM_MINGW/bin;$env:Path"
$env:PITSTOP_TEST_CPP_SHOTS = "$PWD/artifacts/cpp-language-shots"
dotnet test tests/Passenger.Tests --no-build --filter FullyQualifiedName~CppNativeLanguageTests
```

Native tests report why they skip if the reference installation is missing. They operate on copies of the Phase 1 fixtures. The optional capture directory receives light and dark frames for exported-symbol completion, navigation to the exported partition, and the undeclared-export diagnostic.

## Windows x64 verification

| Coverage | Observed result |
| --- | --- |
| Named modules in C++23 and C++26 | The consumer reported clean diagnostics. The editor completed `module_value` and opened the exported `numbers.cppm` partition. Clangd provided hover, consumer references, and locations in the primary interface. From the implementation unit, it resolved the function defined in the internal `detail.cppm` partition. |
| Saved export in both language modes | The client diagnosed `fresh_value` before the provider exported it. Saving `arithmetic.cppm` restarted clangd, cleared the error, and resolved the new definition. The consumer buffer remained unsaved, and no build ran. |
| Standard library modules | Clangd resolved `std::vector` to libc++ in the C++23 and C++26 `import std` fixtures. |
| Header-based protocol coverage | Clangd returned completion documentation, a function signature, declaration, document and workspace symbols, semantic tokens, a parameter hint, rename edits, formatting edits, and a fix for a missing semicolon. |
| Deterministic and inherited coverage | The fake server covered initialization, document changes, trust, compilation-context refresh, obsolete requests, one recovery after a crash, unsaved-buffer replay, and shutdown. Targeted inherited completion, reference, formatting, LSP transport, and Explorer tests passed. |

The captured frames show the actual completion popup, the caret in the exported partition, and the clangd error in both the editor and Problems.

Remaining work includes reliable navigation from consumers to every implementation unit, a user-selectable context for shared translation units, and validation outside the reference Windows profile. `xmake_ls`, Xrepo package management, native debugging, and module graph indexing have separate integration work.
