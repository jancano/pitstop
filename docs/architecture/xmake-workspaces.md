# Xmake workspaces

Pitstop recognizes an `xmake.lua` at the root of an opened folder. Use File → Open Folder to open it. The Explorer opens a new Xmake folder in the Xmake view and can also show its files on disk. In a mixed workspace the same switch offers the .NET Solution view. The Xmake provider carries its own snapshot independently of the .NET project model.

The validated profile is Xmake 3.1.2 and LLVM-MinGW 23.1.6 on Windows x64. Pitstop looks for Xmake on the login shell's PATH. Set `PITSTOP_XMAKE` to an absolute executable path for a portable installation. The version check accepts 3.1.2 and later 3.x releases. The native integration suite exercises 3.1.2. Use Xmake to choose the active build configuration. For the reference profile, run these commands in a trusted project folder before opening it.

```powershell
$env:PITSTOP_XMAKE = '<xmake>/xmake.exe'
$llvm = '<llvm-mingw>'
$env:Path = "$llvm/bin;$env:Path"
& $env:PITSTOP_XMAKE f -m debug -p mingw --toolchain=llvm "--mingw=$llvm" "--sdk=$llvm" --require=no
```

## Evaluation

Detection checks for the project file. After workspace trust is granted, the session debounces requests and evaluates off the UI thread. It cancels the previous request when the project inputs change and publishes a result only if its request is still current. Closing or switching folders cancels the native process. Revoking trust cancels the request and clears the evaluated model. The provider checks trust before each command. Missing tools, incompatible versions and Xmake command failures appear under the Explorer root with details on hover.

The provider runs `xmake f --require=no` with the project's retained settings. It uses the JSON configuration, target list, target details, and dependency graph shown in the [Phase 1 captures](cpp-toolchain-samples). Target outputs come from `targetfile`. Package rows represent Xmake's reported target declarations. An authored adapter uses Xmake's evaluated project API for expanded sources and declared headers, active toolchain names, the actual configuration file, and the list of loaded project scripts. That API resolves globs and preserves repeated file membership in different targets. Xmake runs the project's scripts after trust is granted, so project hooks run with the IDE user's permissions.

The session follows changes to workspace Lua files, the package lock, file additions or removals that can affect a glob, and the active Xmake configuration file. Reopening the folder also refreshes the model. Changes to project scripts included from outside the workspace require reopening the folder. Package installation and version resolution, native build and run commands, and module graph visualization are outside this phase.

## Compilation databases

The authoritative database is an unchanged export from Xmake’s `compile_commands` generator with the Phase 1 clangd profile. The generator runs with dependency installation disabled and writes to a Pitstop-owned temporary directory. The existing project database stays in place. Each evaluation gets a new output directory, which keeps published snapshot paths available until the session closes.

The separate semantic database uses the Phase 1 source-oriented transformation for Clang. It removes explicit BMI mappings, normalizes GNU-driver include flags, and keeps each translation unit's compilation context. A snapshot publishes both paths together when generation succeeds. A generation error leaves the targets and configuration available. [Phase 1 toolchain findings](cpp-toolchain.md)

Pitstop.Cpp can consume the semantic path when connecting clangd. Pitstop.Modules can later join the target and file identities with compiler scan data, preserving distinct target contexts for shared sources.

## Verification

```powershell
just build
dotnet test tests/Pitstop.UnitTests --no-build --filter FullyQualifiedName~XmakeMetadataTests
dotnet test tests/Pitstop.Tests --no-build --filter FullyQualifiedName~XmakeWorkspaceLifecycleTests

$env:PITSTOP_XMAKE = '<xmake>/xmake.exe'
$env:PITSTOP_TEST_LLVM_MINGW = '<llvm-mingw>'
$env:Path = "$env:PITSTOP_TEST_LLVM_MINGW/bin;$env:Path"
$env:PITSTOP_TEST_XMAKE_SHOTS = "$PWD/artifacts/xmake-explorer"
dotnet test tests/Pitstop.Tests --no-build --filter FullyQualifiedName~XmakeNativeExplorerTests
```

The native test is opt-in. When a screenshot directory is set, it captures light and dark frames from Pitstop's Explorer with the actual Phase 1 modules fixture open.
