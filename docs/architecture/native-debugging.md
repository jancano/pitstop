# Native debugging

Pitstop debugs evaluated executable targets with the official LLVM `lldb-dap` adapter. The desktop and MCP use the same debugger session, source breakpoints and process ownership. Windows x64 is the verified profile with Xmake and Xrepo 3.1.2, LLVM-MinGW Clang and LLDB 23.1.6, and libc++. Xmake Debug builds on this profile provide PE/COFF images with DWARF symbols. [Debugger foundation and actual adapter capabilities](native-debugger-foundation.md)

## Debug Hello Modules

1. Install Xmake 3.1.2 and the LLVM-MinGW UCRT distribution from the reference profile. Put both `bin` directories on PATH. Under **Settings → Native Toolchains**, use **Detect Toolchains** to check LLDB, or enter the full path of `lldb-dap.exe`. The reference LLVM-MinGW distribution already contains it. [Toolchain setup](cpp-toolchain.md)
2. Open the [Hello Modules example](../../examples/hello-modules) and review and trust its Xmake project. Configure the Debug build when preparing the example. Pitstop keeps the active configuration chosen by Xmake.

   ```powershell
   $llvm = 'C:\path\to\llvm-mingw'
   xmake f -m debug -p mingw --toolchain=llvm "--mingw=$llvm" "--sdk=$llvm" --require=no -y
   ```

   The example declares Xmake's `mode.debug` rule so the Debug artifact carries source information. Selecting the mode alone did not add source information to the example's own object files before this rule was added.

3. Select **Xmake: hello-modules** in the main toolbar. Open `greetings-implementation.cpp` and set a gutter breakpoint on the line that returns `words()`. Press **F5** on the Visual Studio keymap or choose **Run → Debug**.
4. At the stop, select the thread and stack frame in **Debug**. Expand values using their disclosure buttons. Step Into can follow calls across Xmake targets and language-module units with symbols.
5. Select **Reveal Module in Explorer** from a stack frame when Pitstop has a confirmed module provider. The example's separate implementation unit can lack a confirmed provider in the index. Its source and stack still work.
6. Use **Continue** to run to the next breakpoint or program exit. **Stop** ends a launched program and its owned descendants. **Rerun** restarts the debug launch after cleanup. Use **Edit Configurations** to set arguments, environment, working directory, console and Stop on Entry.

## Session and provider boundary

The application extends the existing `DebugSession`, `DebugController`, Debug tool window and workspace breakpoint store. The native transport and process owner are `Pitstop.NativeDebug` and `Pitstop.Dap` from Phase 8. The native session reads the event stream continuously while the user works. Requests and shutdown remain bounded. The controller tracks the selected thread and frame, and invalidates inspection data and markers as the selected context changes or the program resumes.

The native build and attach paths use the provider snapshot and the native session constructor. Their dependencies are independent of CoreCLR, Hot Reload, managed exception types, and the legacy adapter payload. The shared editor and debugger controls continue to serve the inherited verification profile.

Build before Debug uses the same build controller, Problems and Xmake operation gate as Build and Run. Preparation saves through the editor’s usual policy, captures an evaluated Xmake configuration, and builds the selected target before resolving its runtime plan. A failed or canceled build ends preparation before the adapter is opened. Changes detected during preparation discard the launch. A change to the active configuration ends the old session. The session holds its original module snapshot.

The provider supplies the executable, arguments, environment, runtime paths and working directory in a provider-neutral launch plan. The native transport consumes that plan and has no knowledge of Xmake intermediate directories. Future providers can supply the same contract. The Xmake resolver preserves evaluated default-run metadata, including the reference profile's DLL search paths. If a target or rule has a custom run hook, launch preparation explains that direct debugging requires a target with default run behavior. [Native project system](native-project-system.md) · [Build and Run](native-build-run.md)

Auto-discovery checks an explicit settings path first, then `PITSTOP_LLDB_DAP`, then the configured LLVM bin directory or `PITSTOP_LLVM_BIN`, PATH, and the conventional Windows LLVM directory. An invalid explicit path produces a diagnostic. The IDE brings up the official adapter over private stdio with init files and automatic symbol-file scripts disabled. Active workspace trust controls preparation and retirement. Adapter stderr, DAP framing and debuggee output have separate paths. [LLVM adapter configuration reference](https://lldb.llvm.org/use/lldbdap.html)

A run configuration can list mappings as `absolute build directory => local source directory`, one per line. Local paths may be relative to the workspace. Native Debug checks the mapping before launch. Verified paths open through the existing editor navigation. An optimized or stripped active configuration gets a warning without changing build flags.

## Breakpoints and execution

The gutter and Breakpoints window share the workspace store. The requested file and line are kept across sessions. Verification, explanations and relocated lines are session observations. Set, enable, disable or remove a source breakpoint while debugging to replace the adapter's breakpoint set for that source. A condition may execute an expression in the debuggee when its breakpoint is hit. Numeric hit counts, log messages, named function breakpoints and the C++ throw filter use verified LLDB operations. A log message makes its source breakpoint continue execution.

Use the existing execution toolbar to pause, continue, step into, step over, step out, run to a source line, show the execution point, stop or rerun. The native toolbar hides Smart Step Into. It marks stopped threads using the actual stopped event. Other thread states may remain unknown. A library target can contribute frames and source locations when the executable loads it.

## Stack, values and watches

Frames retain the adapter's function name and source information. Stack requests initially fetch at most 64 frames. Selecting a frame changes navigation and its variable context. Pitstop reads normal local scope data and expands other scopes and children on demand. For large groups it requests pages of 100. It displays values and child references supplied by LLDB, including verified libc++ string summaries and vector, map and tuple children where available. A variable can be uninitialized at a breakpoint before its declaration is executed. An optimized-out or unavailable value remains as LLDB reports it.

Native watches are kept with workspace state. Use the Variables context menu to add, edit or remove a watch. Automatic watches and editor inspection use identifiable variable data already returned by the adapter for the current frame. Use **Evaluate Expression** in the Debugger or enter an expression in the **Debug Console** to evaluate it explicitly in the selected paused frame. C++ expressions may execute program code. Variable assignment, memory writes and debugger commands remain outside this integration.

Language-module context comes from matching the stopped source to the captured compiler-backed module snapshot. A unique confirmed provider can label a frame or breakpoint and point to the Module Explorer. Multiple compilation contexts remain ambiguous. DAP loaded modules are executable images and libraries, independent of C++ language modules. [Module Intelligence](module-intelligence.md)

## Console, attach and shutdown

The **Internal** console displays debugger messages, program stdout and stderr, and exit status. For interactive stdin select **Terminal** in Edit Configurations. The adapter sends a reverse terminal request for its validated launch helper. Pitstop runs it using its existing Windows ConPTY terminal and holds its process family for shutdown. The terminal displays the combined terminal stream. External-terminal integration remains unverified.

**Run → Attach to Process** lists local processes without launching one. Select a specific process, check its executable and use **Attach with LLDB**. Pitstop rechecks the process identity before attaching. Stop on an attached session detaches and leaves the process running. Native attachment and launch use separate ownership policies.

Restart waits for the previous owned debuggee to stop. Closing a workspace, revoking trust or shutting down the IDE cancels the session and releases owned processes. An adapter crash is surfaced as a debug failure and triggers cleanup. Sessions never automatically restart an application after failure.

## MCP

Connect an MCP client using `Pitstop.exe --mcp-attach`. The same Debug tab and state are visible to the person and the client.

| Tool | Operations | Authorization |
|---|---|---|
| `native_debug_inspect` | `availability`, `state`, `breakpoints`, `wait`, `threads`, `frames`, `scopes`, `variables`, `module` | Read operations require a trusted workspace. Availability describes the host and how to validate the adapter. |
| `native_debug_control` | `validate`, `start`, `set_breakpoint`, `remove_breakpoint`, `continue`, `pause`, `step_into`, `step_over`, `step_out`, `stop`, `select_thread`, `select_frame`, `evaluate` | Execution and mutation require the existing tool permission. Start refuses an active native debug session. Validate runs the configured adapter's version probe. |

Use the `sessionId` and `stop` returned by `state` to associate explicit thread, frame and variable references with the paused context. Inspection limits lists to at most 100 entries. `wait` is bounded and cancellable. Breakpoint paths and one-based line numbers are scoped to the trusted workspace. Evaluate is an explicit execution operation. The server avoids including launch environment, arguments or raw protocol transcripts in tool responses.

## Validation

The real IDE tests build disposable copies of the Phase 8 fixtures through Xmake. They drive F5 and source markers, inspect actual LLDB variables, connect through the existing MCP client transport, check native source mapping and exercise both C++23 and C++26 module configurations. The tests also cover ConPTY input, detach and launched descendant cleanup. The Phase 8 fake adapter remains the deterministic test for startup orders, timeouts, cancellation and adapter shutdown.

| Validation | Passed | Failed | Skipped | Timed out |
| --- | ---: | ---: | ---: | ---: |
| Complete unit suite | 621 | 0 | 5 | 0 |
| Selected native toolchain, editor, templates, build and MCP regressions | 30 | 0 | 0 | 0 |
| Selected protected general-purpose regressions | 11 | 0 | 5 | 0 |
| Native debugger IDE, real MCP and Phase 8 foundation | 19 | 0 | 0 | 0 |
| Targeted rerun after verification labels and screenshot refinements | 5 | 0 | 0 | 0 |

Locked restore and the complete solution build succeeded with zero warnings or errors. The unit skips were environment-dependent file-system tests and the optional captured-graph benchmark. In the protected suite, three Docker Scout tests require a non-Windows script launcher. Two update tests are specific to macOS and Linux. These are distinct from the native debugger probes, which ran on the full Windows reference profile.

During development, a source created after opening the test workspace correctly invalidated a pending build. One module stepping run reached the partition after missing the first requested consumer stop. The repeatable module tests exercise Stop on Entry and then Continue to the consumer breakpoint before stepping into the facade and out of the partition. The ordinary F5 test and both package smoke paths exercise a direct source stop. The reason for the earlier missed first stop was not independently isolated.

```powershell
$env:PITSTOP_XMAKE = '<absolute path to Xmake 3.1.2 xmake.exe>'
$env:PITSTOP_TEST_LLVM_MINGW = '<absolute path to LLVM-MinGW 23.1.6>'
$env:Path = "$(Join-Path $env:PITSTOP_TEST_LLVM_MINGW 'bin');$env:Path"
dotnet restore Pitstop.slnx --locked-mode
just build
dotnet test tests/Pitstop.Tests/Pitstop.Tests.csproj --no-build --filter 'FullyQualifiedName~NativeDebuggerIdeTests|FullyQualifiedName~McpNativeDebuggerTests|FullyQualifiedName~NativeDebugFoundationTests'
```

### Windows packages

The package smoke test uses a fresh state directory and a disposable copy of Hello Modules outside all Git worktrees. Use the example's Debug configuration, including its Xmake mode rule. Run `python -I tools/alpha-package-smoke.py <Pitstop.exe> <sample> <new-state> --version 0.3.0-alpha.1 --native-product --native-debug` with the reference tools available. The probe exercises startup, project loading, module indexing, build and run, LLDB discovery, a source breakpoint, frames, scopes and debugger shutdown over MCP. The candidate contains Pitstop's native DAP integration and attribution. Obtain LLDB independently from the reference LLVM-MinGW distribution.

Local unsigned Windows x64 packages were built from `74befebda`. The ZIP includes `Pitstop.Dap.dll`, `Pitstop.NativeDebug.dll` and the third-party notice. The inventory check found no bundled LLVM binaries or managed debugger payload. The package build also checked portable state isolation. The runtime smoke starts from the extracted ZIP and from a real per-user installation. The installer check verifies the installed executable, Start menu shortcut and uninstall registration. It uninstalls the verification copy and checks that the user PATH and existing Pitstop and Passenger state are unchanged.

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| `Pitstop-windows-x64-portable.zip` | 143,477,777 | `30743b97614421ba4a28f73ce23f67beb366144642f098256a0f639c9d9e6421` |
| `Pitstop-windows-x64.exe` | 157,401,003 | `f27e331382822483824e8281fd00f23205454dc964340426c203c3568f1c3b32` |

### Capability evidence

The Phase 8 matrix records the adapter's advertised capabilities separately from the probe results. In Phase 9 an enabled action additionally depends on the current workspace, toolchain, session state and selected source or frame. [Raw capability matrix and sanitized DAP captures](native-debugger-foundation.md#capability-matrix)

| Area | Integrated behavior | Validation |
| --- | --- | --- |
| Launch, source mapping and source breakpoints | Build before Debug, persistent source requests, configured source maps, actual verification and relocation reporting | Real Windows IDE sessions, including source paths under a build directory absent locally. |
| Conditional, function and exception breakpoint requests | Conditional source breakpoints, optional hit counts and logpoints, named functions and C++ throw | Phase 8 verified the LLDB requests. Phase 9 stresses conditional source breakpoints in the IDE and MCP. |
| Stack and variables | Thread and frame selection, local scopes, lazy STL children, watches and explicit evaluation | Real adapter values and actual voided handles when switching frame or resuming. |
| Console and attachment | DAP output and exit, ConPTY stdin/stdout/stderr, explicit local PID, disconnect and release | Interactive fixture returns exit 17. The fixture attached to remains live after Detach. |
| Module context | Source matches linked to confirmed named-module and partition providers in the captured compilation context | Debug frames for the exported partition and facade show separate module labels. The separate implementation unit can have unknown ownership. |
| Advanced debugger views | Reserved for later work | Memory, registers, disassembly, instruction stepping and data breakpoints stay out of the desktop integration. |

### Screenshots

Set `PITSTOP_TEST_DEBUG_SHOTS` to an output directory before running `NativeDebuggerIdeTests` to capture the light and dark views from actual LLDB sessions. Startup, Settings and Attach views can show local executable paths. The committed screenshots show portable examples.

| View | Light | Dark |
| --- | --- | --- |
| Actual stop and module-aware stack in an exported partition | [Light](../screenshots/native-debug-partition-light.png) | [Dark](../screenshots/native-debug-partition-dark.png) |
| Watches and an expanded libc++ vector | [Light](../screenshots/native-debug-variables-light.png) | [Dark](../screenshots/native-debug-variables-dark.png) |
| A verified conditional breakpoint and a pending source breakpoint | [Light](../screenshots/native-debug-breakpoints-light.png) | [Dark](../screenshots/native-debug-breakpoints-dark.png) |
| Selecting a worker thread reveals its own stack | [Light](../screenshots/native-debug-threads-light.png) | [Dark](../screenshots/native-debug-threads-dark.png) |
| Interactive input, stdout, stderr and exit status in ConPTY | [Light](../screenshots/native-debug-terminal-light.png) | [Dark](../screenshots/native-debug-terminal-dark.png) |

### Responsiveness

The measurements are from Windows 11 x64 build 26300 on a Ryzen 9 3900X with 24 logical processors and 64 GiB of RAM. The real IDE test times UI-visible transitions and uses a disposable workspace for a cold build. The package smoke uses an incremental build of the already compiled Hello Modules project before its breakpoint request. These are single-run observations on this machine.

| Measurement | Observed time |
| --- | ---: |
| F5 to first source stop, including the cold Xmake build | 6.26 s |
| Stack presentation after the stop | 13 ms |
| Variables presentation after the stop | 136 ms |
| Select a caller frame and return to the original frame | 133 ms |
| Expand the small libc++ vector | 25 ms |
| Read two pages of a 1,024-element libc++ vector | 117 ms |
| Step Over until the next stop | 167 ms |
| Stop until the debugger is no longer active | 183 ms |
| Portable package, incremental build to first breakpoint, then shutdown | 3.57 s to break, 121 ms to stop |
| Installed package, incremental build to first breakpoint, then shutdown | 3.42 s to break, 115 ms to stop |

The session requests the first 64 stack frames and reads the selected frame. Variables expand on demand in groups of up to 100 per request. Native inline values are cleared on resume and during a frame change. Watches are resolved from already fetched variables without repeated evaluation requests.

## Phase 9.5 follow-up

Validate instruction-level stepping, disassembly, registers, memory and data breakpoints individually before exposing them in the desktop or MCP. Expand exception filters and optimized-build coverage, including generated source paths and DLL unload and reload. Investigate additional terminal modes and toolchains on their actual platforms. Keep binary image inspection separate from the Module Explorer, and extend confirmed debugger module context as compiler evidence improves. The Xrepo installation UI remains separate work.
