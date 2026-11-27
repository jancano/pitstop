# Native debugger foundation

Phase 8 establishes a headless LLDB session for evaluated native targets. The Windows x64 verification uses Xmake-built executables and the same DAP transport as the IDE. Wiring F5, debugger controls and the native debug state into the desktop is Phase 9.

## Repository audit

The Phase 7.5 distribution excludes the managed debugger payload. Source builds retain it for the repository's legacy verification profile. The reusable surfaces are the DAP reader and writer, reverse requests, process management, terminal hosting, the debug tool window, file and line breakpoint persistence, the call stack and threads presentation, the lazy variables tree, watches, editor gutters and navigation. [Phase 7.5 report](codebase-consolidation.md)

| Surface | Reuse and adaptation |
|---|---|
| `Pitstop.Dap` | Framing, correlation, events, reverse requests and disconnect. Native requests supply the specification's case-sensitive `adapterID` and `clientID` fields. The inherited initialization model uses managed defaults and different casing. |
| `AdapterEnd`, `DebugSession`, `DebugController` | Preserve existing sessions and action plumbing. Extract the UI-facing state behind a common boundary before connecting LLDB. The inherited startup sequencer expects a launch reply before it handles `initialized`. Its launch configuration, attach process listing, exception filters and expression defaults are managed-specific. |
| Breakpoints, `DebugView`, `VariablesTree`, `VariablesList` | Reuse state and controls. Keep a requested source line separate from the bound line, and map adapter breakpoint IDs only within a live session. Show debugger-supplied names, types, summaries and child references. |
| Managed integrations | Keep CoreCLR module and assembly identities, runtime IDs, C# declaration inference, decompiled-frame lookup, runtime event handling and Hot Reload extensions outside the native session. Native thread IDs are adapter IDs scoped to the current session. |
| `BuildRunController.Xmake` and terminal | Reuse the active run configuration, workspace operation gate, normal build diagnostics and console plumbing. The existing ConPTY host will need a native reverse-request handler and attached process ownership. |
| `Pitstop.Modules` and MCP | Use the current immutable workspace snapshot for source context. The native MCP product gates inherited managed debugger operations. Phase 8 adds no debugger tool to MCP. |

## Reference profile and discovery

| Component | Observed on Windows x64 |
|---|---|
| Xmake and Xrepo | 3.1.2, Xmake build `3.1.2+HEAD.5d21e9daf`. Use the Xrepo launcher from the same distribution. |
| Compiler and language server | LLVM-MinGW UCRT distribution `llvm-mingw-20261117-ucrt-x86_64`, Clang and clangd 23.1.6, target `x86_64-w64-windows-gnu`, libc++. |
| Debugger | `lldb.exe`, `lldb-dap.exe` and `liblldb.dll` from that same LLVM-MinGW distribution, version 23.1.6, LLVM revision `5b10d2d74929eae78912771222c9855af97496f4`. `lldb-dap --check-python` found `libpython3.14.dll`. No Python formatter or script was supplied by a project. |
| Executable and symbols | PE/COFF AMD64, `.debug_info` and `.debug_line` for Debug. Xmake's observed Debug compilation command uses `-g -O0`. With the fixture's unmodified Release mode, both DWARF sections were absent and its source breakpoint stayed unverified. [Debug](native-debugger-samples/debug-image.json) · [Release](native-debugger-samples/release-image.json) |

Use the above LLVM-MinGW distribution, or explicitly point `PITSTOP_LLDB_DAP` to a trusted absolute adapter path. Keep its runtime DLLs with the distribution. Discovery checks an explicit executable first, then a configured LLVM bin directory or `PITSTOP_LLVM_BIN`, PATH, and Windows' conventional LLVM installation directory. An invalid explicit path yields an error. Discovery reads the PE machine type, executes bounded `--version` and `--help` requests, checks the loaded LLDB version and required startup flags, and reports the optional sibling CLI and runtime library. A successful DAP `initialize` is the final protocol validation. Versions older than 23.1.6 are outside the implemented discovery contract. Newer binaries still need profile verification. Linux, macOS, cross-architecture launch and separate-distribution debugger combinations remain unverified. [Official adapter documentation](https://lldb.llvm.org/use/lldbdap.html)

For the fixture's `releasedbg` mode, Xmake used `-g -O3 -DNDEBUG` and wrote a GNU debug-link sidecar with a `.pdb` extension. The sidecar itself contains COFF sections with DWARF information. The LLDB CLI resolved the `inspect` function breakpoint to an inlined allocator frame. A source breakpoint on the marked fixture line reported `seed = 11`, a possibly optimized-out pointer, and a local unavailable at that point. Those are observed consequences of inlining and variable location information. Preserve the sidecar with the executable. The automated DAP profile remains Debug. [Optimized CLI observations](native-debugger-samples/releasedbg-observations.json)

## Session and launch contract

`Pitstop.NativeDebug` is independent of Avalonia. It hosts one official LLDB adapter over stdio using `Pitstop.Dap`. Requests and event waits are bounded. It sends launch or attach, allows the response on either side of `initialized`, installs breakpoints in the configuration callback and sends `configurationDone` when advertised. The deterministic adapter covers both orderings. In the real launch transcript, LLDB sends `initialized`, answers the breakpoint requests, responds to `configurationDone`, and only then replies to the original `launch` request. The real attach transcript also delays its response until configuration is done. DAP stdout is reserved for protocol frames. Standard error is drained separately, and program output travels through DAP output events or the selected stdio strategy. [DAP sequencing](https://microsoft.github.io/debug-adapter-protocol/overview)

`NativeLaunchPlan` carries the provider's immutable evaluated workspace and target, so the active configuration, platform and toolchain remain available even when several targets use the same executable name. It carries the evaluated executable, working directory, arguments and environment, runtime library directories, build-before-debug policy, console choice and source maps. The caller supplies the provider's normal build operation. Launch is owned and attach is an explicit local PID with a detach-on-disconnect policy. A future provider can produce the same contract using its own evaluated metadata. LLDB has no dependency on Xmake target internals or intermediate directories. [Native provider architecture](native-project-system.md)

The Xmake resolver uses a fixed Lua export through Xmake 3.1.2's runtime environment helper. It verifies the target is a binary and checks the output path, mode, platform, architecture and toolchain against the evaluated workspace. The reference fixture compares a normal `xmake run` with a DAP launch. Both resolve the dependency DLL in a separate output directory and preserve the target-configured arguments, working directory, and assigned and prepended runtime environment. DAP preserves an explicit argument list containing whitespace, a literal quote, an empty argument, Unicode and a leading dash. The fixture uses Windows wide arguments to verify the exact text. User overrides carry their own argument list, working directory and environment. The provider applies the target’s assignment and prepend semantics onto the supplied base. The export includes package environments provided by the Xmake helper. Exact interaction with arbitrary packages and colliding or very long path variables remains unverified. [Xmake run documentation](https://xmake.io/guide/basic-commands/run-targets.html)

The direct-launch resolver refuses a target with a custom run, before-run or after-run hook on the target or its rules. Running `xmake run custom-run` emitted the fixture's hook message in place of the program's output. In a disposable workspace, `xmake run -d debug-fixture` with `--debugger` set to the reference `lldb.exe` opened the standalone CLI, set the target and configured arguments, then returned exit 0 after `quit`. The Xmake 3.1.2 debugger wrapper passes CLI arguments to LLDB. The DAP client uses its own configuration exchange. PID attach is supported as a separate user action. A post-launch attach leaves startup code free to run. The reference path is direct DAP launch over the evaluated default-run contract. [Xmake 3.1.2 debugger invocation](https://github.com/xmake-io/xmake/blob/v3.1.2/xmake/modules/devel/debugger/run.lua)

Before Phase 9 schedules that launch, it must own the same operation gate as Build and Run, check the current workspace snapshot again after the build, and retire the session when its workspace or configuration changes. The Foundation accepts a linked lifetime from the caller and reacts to workspace trust revocation directly. Existing builds and user compilation databases remain under Xmake's existing policies. The probe builds and evaluates disposable copies.

## Console, process ownership and trust

Internal DAP output carried real stdout and nonzero exit codes. Explicit LLDB file-backed stdio round-tripped UTF-8 input and verified distinct stdout and stderr with exit code 17. The probe closes the adapter before reading the output files because the Windows adapter keeps handles open until shutdown. The separate terminal probe accepts `runInTerminal`, launches the adapter's own `--launch-target` helper through owned pipes and verifies interactive UTF-8 echo and exit code 17. This validates the reverse request and interactive stream exchange. The actual Avalonia/ConPTY and external-terminal paths, Ctrl+C handling and a writable internal debug console are Phase 9 validation work.

The adapter is a `ChildProcesses` family in a Windows job. Startup rejects an adapter that cannot get the required job. Terminal launch has a separate owned-process cleanup callback. The adapter only receives permission to launch the evaluated executable after trust checks. Attach is a distinct PID action, requests a stopped initial state, and uses `terminateDebuggee: false` on disconnect. The real attach test verifies that the process resumes and can exit independently. Separate real probes shut down on trust revocation and after killing just the adapter. Both waited for the launched executable and its child process to exit. Cancellation and revocation retire queued events and response delivery. The session never restarts the debuggee automatically. PID and process handles used by the probe come from its own fixture and its corresponding adapter events. [Adapter failure trace](native-debugger-samples/adapter-crash-cleanup-dap.json)

The adapter starts from an application-controlled directory with `--no-lldbinit`, `target.load-script-from-symbol-file false` and variable REPL mode. There is no input for arbitrary init commands, launch commands, attach commands or workspace formatter scripts. Phase 9 must run discovery after granting trust wherever it uses workspace-derived tool paths. User-owned executable overrides are treated as installed tools. For persistent logs retain only the allowlisted DAP envelope metadata. The private connection disables the global raw DAP trace. Test artifact summaries use fixture-owned fields and omit arbitrary environment values, console text, raw errors, addresses and absolute paths. Interactive Debug Console output, if added, belongs to the visible session and should never be redirected to the artifact trace.

Tool discovery, build failure, invalid launch, initialization failure, protocol timeout, adapter closure and process cleanup have separate diagnostics. A failed request can carry a DAP-specific explanation for the caller. A breakpoint that does not bind stays pending or unresolved with its original requested line. A runtime exit code and an exception stop are program state, and should remain distinct from transport failure. Permissions and user consent for attach must be handled at its visible action boundary.

## Source paths, variables and language modules

The module fixture crosses two static targets, an executable target, a primary interface, exported and internal partitions and a separate implementation. Both C++ modes check exact reported source paths down to individual files. A second session steps from the implementation unit into both partitions without installing breakpoints inside either partition. The `import std` consumer exposes a module-exported type and a local libc++ vector. Source stepping into the standard library, mapped generated sources and logical coroutine stacks remain unverified. [Partition stepping trace](native-debugger-samples/modules26-partition-stepping-dap.json)

`DebugModuleContext` joins canonical, absolute source paths to the current `Pitstop.Modules` snapshot and retains every matching translation unit and provider. An older snapshot, relative source without a mapping, an unindexed generated file or incomplete P1689 can yield an empty result. Multiple matching targets remain visible, and an implementation without confirmed ownership stays unknown. DAP `modules` reports loaded executable images and DLLs. Future stack and breakpoint views can display C++ module badges or link to the Module Explorer with the active target and configuration. Binary image inspection should remain a separate view. [Module Intelligence](module-intelligence.md)

The STL probe checks content summaries for `std::string` and `std::string_view`. The large vector returned four indexed children for a request capped at four. Empty and nested vectors, maps, unordered maps, and tuples also returned bounded samples consistent with their fixture contents. The remaining variables report actual child counts. A single child gives no basis for a formatter-parity claim. The text check did not find the expected literal in the `std::u8string` summary. Full Unicode escape rendering, long-string truncation and smart pointer dereferencing remain unverified. The UI should retain LLDB's supplied value and expand children on demand. [Sampled variables](native-debugger-samples/libcxx.json)

The probe checks a stopped frame's arguments and arithmetic evaluation. Automatic watches and hover must use a conservative, inspection-only policy. C++ expressions can execute code. Explicit user evaluation belongs to the selected stopped frame. Invalidate frame-relative references on resume or frame change. Template, lambda, concept and module-exported types are exercised as source and local values. The general evaluation of module-exported functions and qualified type expressions remains unverified.

## Capability matrix

`Core` means a DAP operation available without its own capability bit. `Extension` identifies LLDB launch or an independent host feature. The verified column requires an observation from the named Windows x64 profile. Captured boolean capabilities are in [capabilities.json](native-debugger-samples/capabilities.json).

| Capability | Advertised | Verified | Profile | Result | Limitation |
|---|---|---|---|---|---|
| Initialize, launch, configurationDone and disconnect | Core and yes | Yes | Debug | Real sequence and successful disconnect | Fake adapter also covers deferred launch reply |
| Threads, stackTrace, scopes and variables | Core | Yes | Debug | Two threads with stacks, frame locations, locals and arguments | Per-thread lifecycle and simultaneous breakpoint delivery remain unverified |
| Evaluate | Core, hover flag yes | Yes | Debug | Explicit selected-frame arithmetic returns `13` | Automatic hover, watches and side effects need a policy |
| Continue, next, stepIn, stepOut and pause | Core | Yes | Debug | Source stepping, resume and a stop in a multithreaded fixture | Instruction granularity is separately advertised |
| C and C++ source breakpoints | Core | Yes | Debug | Both verify, and real C++ breakpoint is reached | General relocation and optimized line drift remain unverified |
| Main image and dependency DLL list | Yes | Yes | Debug | Loaded-image list includes the fixture's linked DLL | Images carry no C++ module identity |
| Breakpoints in a dynamically loaded DLL | Core | Yes | Debug | Pending before load, then reports the exact DLL source line | General unload and reload behavior unverified |
| Named module and partition breakpoints and stepping | Core | Yes | C++23 and C++26 Debug | Interfaces, implementation, cross-target paths and source steps into both partitions | Generated source maps require a mapped fixture |
| `import std` | Core | Yes | C++23 and C++26 Debug | Stop in importing consumer and inspect local vector and exported type | Source stepping inside the standard library module is unverified |
| Attach and detach | Core | Yes | Local Windows x64 Debug | Explicit PID and live process after disconnect | Permission-denied, cross-user and startup attach remain unverified |
| File-backed stdio and runInTerminal | Extension and client request | Yes | Debug | UTF-8 input/output, stderr and terminal reverse request with owned pipes | ConPTY integration, external terminal and Ctrl+C remain unverified |
| Owned process cleanup | Extension | Yes | Debug | Trust revoked or adapter killed, then executable and its child both exited | Run hooks and independently detached terminal processes need an explicit ownership contract |
| libc++ visualization | Core variables | Yes | Debug | Vector synthetic elements and string summary | See [sampled variables](native-debugger-samples/libcxx.json) for individual children and summaries |
| Registers | Core scope | Yes | Debug | Nonempty register groups | Register UI is later work |
| readMemory and disassemble | Yes | Yes | Debug | Eight bytes and four instructions from a stopped frame | Memory writes are a separate capability |
| writeMemory, setVariable, data and instruction breakpoints | Yes | No | Debug | Advertised, unverified | Keep mutation behind explicit operations |
| Function, conditional and hit-count breakpoints, logpoints | Yes | Yes | Debug | Reaches `inspect`, filters on the second call, logs from C without stopping, then stops on the second DLL hit | General expression and hit-count syntax remain unverified |
| C++ throw breakpoint and exception info | Yes | Yes | Debug | Stops on `cpp_throw`, reads an exception ID, then resumes to the catch and exit 7 | Filter option expressions and other exception filters remain unverified |
| Native abort | Core stop event | CLI only | Windows x64 `releasedbg` | The fixture's `std::abort` stopped in `ucrtbase.dll` with Windows exception `0xc0000409` | DAP crash reporting and resume behavior remain unverified |
| Release source breakpoints | Core | Yes | Windows x64 Release | Program exits normally, no DWARF, no source breakpoint binding | Separate from optimized builds with symbols |
| Optimized build with symbols | Extension | CLI only | Windows x64 `releasedbg` | GNU debug-link sidecar, inlined frame and unavailable values | Optimized build behavior has no DAP verification |
| DAP cancel and instruction stepping granularity | Yes | No | Debug | Advertised, unverified | Local cancellation and source stepping were verified independently |
| Restart, terminate request, setExpression | No | No | Debug | No capability bit set | Disconnect's terminate-debuggee flag is advertised and verified separately |
| Core dumps | Extension | No | Windows x64 | Unverified | No dump format established for this profile |

## Reproduce

Use a built checkout and set the paths to already installed tools from PowerShell. The probe requires explicit fixture trust and never takes a user project path. The output directory is the only intentionally retained state.

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:PITSTOP_XMAKE = '<absolute path to Xmake 3.1.2 xmake.exe>'
$env:PITSTOP_TEST_LLVM_MINGW = '<absolute path to LLVM-MinGW 23.1.6>'
$env:Path = "$(Join-Path $env:PITSTOP_TEST_LLVM_MINGW 'bin');$env:Path"
dotnet restore Pitstop.slnx --locked-mode
just build
& artifacts/bin/Pitstop.NativeDebugProbe/debug/Pitstop.NativeDebugProbe.exe --trust-fixtures $env:PITSTOP_TEST_LLVM_MINGW artifacts/phase8-evidence
dotnet test tests/Pitstop.UnitTests/Pitstop.UnitTests.csproj --no-build
dotnet test tests/Pitstop.Tests/Pitstop.Tests.csproj --no-build --filter FullyQualifiedName~NativeDebugFoundationTests
```

The DAP test adapter runs independently of native tools. Its cases cover both initialization orders, validated launch arguments and mappings, attach policy, late events, timeouts, crash and the untrusted-workspace boundary. The actual Windows native test skips with the platform or the missing executable or environment setting in its reason. The standalone probe passed 22 checks with zero required or optional failures. [Probe results](native-debugger-samples/results.json) · [Protocol sample](native-debugger-samples/basic-dap.json) · [Interactive terminal sample](native-debugger-samples/terminal-reverse-request-dap.json) · [Attach and detach sample](native-debugger-samples/attach-detach-dap.json)

Locked restore and the complete solution build passed in a clean Windows x64 checkout of `41e4fd58e` with zero build warnings or errors.

| Verification | Passed | Failed | Skipped | Timed out |
|---|---:|---:|---:|---:|
| Complete unit suite | 621 | 0 | 5 | 0 |
| Selected headless cases on the clean checkout | 26 | 0 | 1 | 0 |
| Deterministic debugger lifecycle, separate rerun | 7 | 0 | 0 | 0 |
| Standalone real LLDB-DAP checks | 22 | 0 | 0 | 0 |
| Missing adapter, intentional skip check | 0 | 0 | 1 | 0 |

The headless selection covers the real native probe, Xmake lifecycle and Explorer, C++23 and C++26 Build and Run, clangd, Module Intelligence, native project creation, terminal emulation, process ownership and live MCP. Its skip is a process exit behavior specific to non-Windows platforms. The earlier 48-case headless selection passed 45 cases and skipped two. It found that an adapter closure surfaced a channel exception. The foundation now returns the typed adapter failure, verified in both final headless runs. The entire repository headless suite was outside these selections.

The Windows x64 Debug profile meets the core foundation checks and can proceed to Phase 9 desktop integration. The verified launch route covers default Xmake execution, and the interactive test uses an owned pipe launcher. Arbitrary run hooks, ConPTY, mapped generated sources, additional toolchain profiles and the optional DAP features in the matrix require separate validation.

## Phase 9 sequence

1. Add a native session owner to the current debug controller. Use one workspace/configuration generation and an explicit session lifetime shared by actions and views. Save using the existing editor policy, acquire the existing Build and Run gate, and rebuild only when configured. Feed failures and progress through the existing UI.
2. Add Debug and F5 to the existing native run configuration selector. Keep source breakpoints in the current persistence and editor gutters. Render stable requested locations and transient pending, verified or relocated locations. Gate unsupported breakpoint kinds with the negotiated capability and validated profile.
3. Adapt the shared debug toolbar, Breakpoints, Threads, Call Stack, Variables, Watches and Debug Console. Read the adapter's stopped thread state and only enable evaluation in a current frame. Invalidate thread and frame handles on resume. Reuse navigation for mapped source and surface unresolved source explicitly. Present LLDB-supplied libc++ values and lazily page children.
4. Connect the reverse-request callback to the owned ConPTY path and verify it with real interactive fixtures before exposing it. Add explicit PID selection and confirmation for Attach. Preserve detach semantics for external processes and process-job cleanup for launched programs. Restart means a new user-requested session after disposal.
5. Add module and target badges using source context and offer Module Explorer navigation. Keep binary images a separate category. Defer Memory Viewer, Registers, Disassembly, data breakpoints and any advanced mutation UI until their integration is validated.
6. Use the same session state and action controller for future MCP operations. Reading a stopped snapshot, listing threads or frames and looking up module context can use bounded read operations. Launch, attaching, breakpoint changes, waiting for stop, expression execution, continuing, stepping and stopping need explicit invocation and cancellation. Give evaluation and memory mutation distinct execution boundaries. Trust revocation and workspace closure apply to every client equally.
