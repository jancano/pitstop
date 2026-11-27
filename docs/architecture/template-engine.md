# Template engine design

The native wizard needs typed parameters, capability filtering, file and directory substitutions, conditional text and file inclusion, and a preview backed by the same bytes as generation. The engine must treat manifests and source files as untrusted data.

| Engine | Fit for the native catalog | Integration cost |
| --- | --- | --- |
| Microsoft.TemplateEngine | Runnable source layouts, replacements, conditionals and output renaming closely match the workflow. | The host still needs to constrain allowed operations, keep post-actions outside rendering and map compiler capabilities into the wizard. [Microsoft template manifest](https://github.com/dotnet/templating/wiki/Reference-for-template.json) · [Post-actions](https://github.com/dotnet/templating/wiki/Post-Action-Registry) |
| Scriban | Supports substitutions, conditions and diagnostic source locations through its parser. | Pitstop would need to restrict the scripting surface, define safe objects and functions, and implement typed manifests, filenames and transactional writes. [Scriban runtime and language](https://github.com/scriban/scriban) |
| Fluid | Liquid syntax provides conditions, output and configurable rendering limits. | The host must configure strict resolution and bounded rendering, control file providers, and independently implement typed parameters and the filesystem plan. [Fluid security and rendering](https://github.com/sebastienros/fluid) |
| Purpose-built renderer | A bounded grammar can express named substitutions and boolean conditions in the initial catalog. | Pitstop owns the compatibility rules and diagnostics. The manifest, path validation and write plan still need independent tests. |

The first schema uses the bounded declarative renderer. The renderer receives validated scalar values and offers explicit escaping modes. Parameter validation and path resolution run outside interpolation. A plan holds the bytes to preview and write. Capability discovery and optional repository initialization belong to the host.

The focused unit run passed catalog discovery, rendering, preview consistency, capability filtering, an added catalog standard, path traversal, collisions, cancellation and existing-destination protection. The symlink check was skipped because the Windows test account could not create a symlink. The IDE wizard and official project catalog are subsequent integrations. See the [authoring reference](../templates/authoring.md) for the implemented syntax.
