# Pitstop repository guide

Pitstop is an open-source, modules-first C++ IDE. Trusted Xmake workspaces supply evaluated targets to the Explorer, build and run commands, compilation contexts to clangd, and inputs to the P1689 module index. The foundation uses C# on .NET and Avalonia and retains Passenger's editor, workspace and language-server infrastructure. Windows x64 is the native reference profile. Native debugging and the Xrepo package installation UI are future work.

## Working in this repository

- Keep the existing `Passenger.slnx`, project names and namespaces until a dedicated rename. Use Pitstop for product UI, application metadata, help, registration and distribution.
- Keep product data under the Pitstop state root and use `PITSTOP_*` for product-owned environment overrides. Keep Pitstop's caches, logs, temporary files, sockets and registrations isolated from Passenger's.
- Keep application updates and release publishing disabled. CI can build, test and upload workflow artifacts. Deployment needs a separate Pitstop release design.
- Preserve the original MIT license and attribution in `THIRD_PARTY_NOTICES.md`. Use open-source dependencies and follow the repository's dependency and attribution conventions.
- Reuse existing editor, workspace, background work, process and settings mechanisms. Preserve the .NET build infrastructure and keep the legacy managed verification profile usable while isolating it from the desktop and native MCP defaults.
- Keep provider-specific compilation ownership in the provider. Project templates use versioned manifests and produce an inspectable generation plan before writing. [Native project system](docs/architecture/native-project-system.md) · [Template authoring](docs/templates/authoring.md)
- Run `just build` and the relevant `just test <FILTER>` suites. Keep warnings as errors. For direct `dotnet` or IDE builds set `AVALONIA_TELEMETRY_OPTOUT=1`.

[CLAUDE.md](CLAUDE.md) describes subsystem conventions and test infrastructure in more detail.
