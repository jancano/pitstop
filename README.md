# Pitstop

Pitstop is an open-source, modules-first C++ IDE in development for Windows, macOS and Linux. Its goal is to make C++ modules first-class in the workspace and editor, with deep integration for Xmake projects and Xrepo dependencies.

The editor, workspace, Git, terminal, language servers, debugging, and .NET tooling come from [Passenger](https://github.com/Zaneris/passenger). Open a trusted folder containing `xmake.lua` to browse evaluated targets, dependencies, and source membership in the Explorer. The [Xmake workspace guide](docs/architecture/xmake-workspaces.md) and [C++ language intelligence guide](docs/architecture/cpp-language-intelligence.md) describe setup for the validated Windows toolchain. Xrepo package management is planned.

## Build and run

Install the .NET 11 SDK specified in [`global.json`](global.json), [just](https://github.com/casey/just), and Git. On Linux, install X11 and fontconfig libraries. Tests also use Python 3 and the .NET 10 SDK. Web language and container tests use Node.js and a Linux container engine, respectively.

```sh
just build                  # Debug build of Passenger.slnx
just run                    # Open Pitstop
just test [FILTER]          # Run the test suite or matching tests
just screenshot pitstop.png # Render the window off-screen
```

The app is built under `artifacts/bin/Passenger.App/debug/`. Run `Pitstop.exe` on Windows or `Pitstop` on macOS and Linux. Use `AVALONIA_TELEMETRY_OPTOUT=1` for builds outside `just`, including IDE builds.

## Application data

| Platform | Default data directory |
| --- | --- |
| Windows | `%LOCALAPPDATA%\Pitstop` |
| macOS | `~/Library/Application Support/Pitstop` |
| Linux | `$XDG_DATA_HOME/Pitstop` or `~/.local/share/Pitstop` |

`PITSTOP_SETTINGS` selects a different state directory. A `pitstop-data` directory beside a portable copy provides portable state. Settings, workspace state, cache and logs live under that root. The main log is `logs/pitstop.log`. Pitstop uses its own temporary directories and IPC names, and starts with its own workspace trust decisions.

CI builds and checks packages. Automatic application updates, release publishing, package registry publishing, and website deployment are disabled. Container debugging can use bundled tools or a local build from `just container-tools <RID>`.

## Contribute

See [AGENTS.md](AGENTS.md) for repository conventions, [CLAUDE.md](CLAUDE.md) for architecture and testing details, and [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidance.

## License

Pitstop is distributed under the [MIT License](LICENSE). The original Passenger copyright and [third-party notices](THIRD_PARTY_NOTICES.md) accompany the source and builds.
