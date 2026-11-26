# Native Build and Run

Pitstop builds and runs evaluated Xmake targets through the existing Build, Run, Problems, and Terminal windows. The reference profile is Windows x64 with Xmake/Xrepo 3.1.2 and LLVM-MinGW 23.1.6 with libc++. See the [toolchain validation](cpp-toolchain.md), [workspace provider](xmake-workspaces.md), and [clangd integration](cpp-language-intelligence.md).

## Using the IDE

1. Configure a trusted fixture with the reference toolchain and open its folder. Grant trust and wait for the Explorer to show the evaluated targets and active configuration.
2. Choose an executable in the existing run configuration selector. Use **Build Selected Targets** to build it. An Explorer target selection also supports Build, Rebuild, and Clean for libraries and executables. The Xmake workspace root provides the workspace-wide actions.
3. Open **Edit Configurations** to set arguments, environment, working directory, build before launch, or the integrated terminal. Edit the generated configuration in place or copy it to create a personal configuration. A copy can select a different executable target. Relative working directories resolve from the workspace root. A blank working directory or argument field uses Xmake's target defaults.
4. Run, type input in the Run console or the Terminal, and use Stop or Rerun. Build failures are listed in **Problems → Build**. **Go to Source** opens a located diagnostic, and **Related Locations** opens Clang's located notes.

## Build operations

| IDE operation | Xmake 3.1.2 command |
| --- | --- |
| Build selected | `xmake build <targets>` |
| Build workspace | `xmake build --all` |
| Rebuild selected | `xmake build --rebuild <targets>` |
| Rebuild workspace | `xmake build --rebuild --all` |
| Clean selected | `xmake clean <targets>` |
| Clean workspace | `xmake clean` |
| Run | `xmake run [--workdir=<directory>] <target> [arguments]` |

Pitstop passes the evaluated project directory and project file explicitly, and answers Xmake confirmation prompts with `--confirm=no`. Xmake still owns dependency resolution, incremental builds, module scanning, BMI generation, and build ordering. Workspace-wide Build includes non-default targets, so it intentionally hits the failing targets in the `native-run` fixture. On 3.1.2, plain `clean` visits all targets and preserves the configuration. The `--all` option to Clean also removes configuration files, so the IDE uses plain `clean` for the workspace action. [Xmake Build targets](https://xmake.io/guide/basic-commands/build-targets.html), [Xmake Clean targets](https://xmake.io/guide/basic-commands/clean-targets.html)

## Lifecycle and diagnostics

`Pitstop.Xmake` constructs typed operations against an immutable snapshot and checks workspace trust again before constructing each project command. `BuildRunController` saves editor changes through the inherited save pipeline and streams the owned process through `ProcessRunner`. The Build view shows raw progress and output, process exit information, and elapsed time. Internal and terminal runs use the inherited input and process-tree management.

The workspace session holds evaluation while an IDE-initiated native operation is active. The operation uses one snapshot, and its generation and input stamps are checked before build results or a build-before-run launch can use it. Project changes queue reevaluation. A revocation or workspace closure cancels the operation. Canceling a build in the same configuration leaves the previous completed results visible. A configuration refresh clears that previous generation's build findings. Builds and runs share the workspace's existing clangd integration. Builds alone do not trigger a language-server restart, and snapshots and saved module interfaces retain the Phase 3 refresh behavior.

The reference Xmake build wraps Clang and LLD text. The parser recognizes `path:line:column` Clang headers, source excerpts, warnings and notes, and retains LLD continuation lines and Xmake failures. Located notes can be navigated from their diagnostic and from the Messages filter. Linker and configuration errors without a diagnostic location appear as unlocated rows. Unrecognized chatter remains in Build output. Build findings use the Build tab and stay independent of clangd's live diagnostics. Neither database export rewrites the user's `compile_commands.json`.

## Runtime behavior

The IDE uses `xmake run` in both console modes. The reference fixture places a DLL in a separate target directory. Xmake supplied the DLL search path, configured runtime variables and default arguments, and ran the before and after hooks. An explicit working directory reached the executable, and arguments with spaces, literal quotes, Unicode, empty values, and leading dashes arrived intact. Configuration environment variables go into Xmake's inherited environment, so project `set_runenv` settings retain Xmake's precedence.

The fixture selects UTF-8 when it writes to a Windows console. Other native programs choose their own console encoding.

On the reference build, a successful program exits through Xmake with zero. For a native program that returns 7, Xmake emits `execv(...) failed(7)` and itself returns a different nonzero exit code. The console shows the executable failure from Xmake and the wrapper's process status. Arbitrary run hooks can replace the normal executable launch and control their own output and failure behavior. A project may also opt into Xmake's own `run.autobuild` policy, independently of the IDE's build-before-run checkbox. [Xmake Run targets](https://xmake.io/guide/basic-commands/run-targets.html)

## Reproduce

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:PITSTOP_XMAKE = '<xmake>/xmake.exe'
$env:PITSTOP_TEST_LLVM_MINGW = '<llvm-mingw>'
$env:Path = "$env:PITSTOP_TEST_LLVM_MINGW/bin;$env:Path"
$env:PITSTOP_TEST_NATIVE_SHOTS = "$PWD/artifacts/native-build-run-shots"
just build
dotnet test tests/Passenger.UnitTests --no-build --filter FullyQualifiedName~Xmake
dotnet test tests/Passenger.Tests --no-build --filter FullyQualifiedName~XmakeNativeBuildRunTests
```

For a manual run, copy `tests/CppWorkspaces/native-run` to a working folder and configure it with `xmake f -m debug -p mingw --toolchain=llvm --mingw=<llvm-mingw> --sdk=<llvm-mingw> --require=no`. The `interactive` target accepts any line to echo, `spawn` to start an owned child, `quit` to exit successfully, and `fail` to return 7. The `compile-error` and `link-error` targets produce separate Clang and LLD failures. The automated suite also copies the Phase 1 named-module and `import std` fixtures into scratch workspaces.

## Verification

- The complete solution built with zero warnings or errors. All 12 Xmake unit tests and 11 targeted inherited regressions passed. The regressions cover .NET Build and Run, persisted run configurations, workspace trust and switching, and clangd lifecycle and unsaved document recovery.
- All four real clangd regression tests passed on the reference LLVM-MinGW toolchain, including completion and navigation through modules, saved interface refresh, and `import std` in both language modes.
- All three native Build and Run flows passed. They exercised selected and workspace builds, Clang navigation and related notes, unlocated linker errors, two consoles, persisted settings, workspace switching, process-tree cleanup, cancellation, and trust revocation. The named-module and `import std` cases built and ran in C++23 and C++26 under Debug and Release. Unchanged builds preserved executable timestamps. The suite also built after implementation and interface edits and checked that Clean preserved Xmake's configuration.
- Running the suite without the reference tool paths produced three explicit skips naming Windows x64 and the required Xmake and LLVM-MinGW settings.
- The native headless test captures eight rendered frames when `PITSTOP_TEST_NATIVE_SHOTS` is set. The inspected light and dark frames show real Xmake progress and success, navigation from a Clang error, the editable native configuration, and the program's arguments, environment, stderr, and echoed input.

## Scope

The phase covers the local reference toolchain. Native Debug and Profile are disabled for Xmake configurations. External Xmake invocations and additional IDE windows can change project state independently. Pitstop follows the resulting file changes, with Xmake's project locking governing direct tool operations. Native diagnostics use the validated English Clang/LLD formats. Further compiler formats, cross-host execution and remote Xmake builds remain unverified.
