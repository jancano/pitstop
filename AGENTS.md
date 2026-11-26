# Pitstop repository guide

Pitstop is an open-source, modules-first C++ IDE. Its roadmap gives C++ modules first-class workspace and editor support through Xmake and Xrepo. The foundation uses C# on .NET and Avalonia and retains Passenger's editor, workspace, language servers and debugging. Trusted Xmake workspaces supply targets to the Explorer and compilation contexts to clangd. Xrepo package management is future work.

## Working in this repository

- Keep the existing `Passenger.slnx`, project names and namespaces until a dedicated rename. Use Pitstop for product UI, application metadata, help, registration and distribution.
- Keep product data under the Pitstop state root and use `PITSTOP_*` for product-owned environment overrides. Keep Pitstop's caches, logs, temporary files, sockets and registrations isolated from Passenger's.
- Keep application updates and release publishing disabled. CI can build, test and upload workflow artifacts. Deployment needs a separate Pitstop release design.
- Preserve the original MIT license and attribution in `THIRD_PARTY_NOTICES.md`. Use open-source dependencies and follow the repository's dependency and attribution conventions.
- Reuse existing editor, workspace, background work, process and settings mechanisms. Keep .NET features working as the C++ roadmap proceeds.
- Run `just build` and the relevant `just test <FILTER>` suites. Keep warnings as errors. For direct `dotnet` or IDE builds set `AVALONIA_TELEMETRY_OPTOUT=1`.

[CLAUDE.md](CLAUDE.md) describes subsystem conventions and test infrastructure in more detail.
