# Template engine design

The native wizard needs typed parameters, capability filtering, file and directory substitutions, conditional text and file inclusion, and a preview backed by the same bytes as generation. The engine must treat manifests and source files as untrusted data.

| Engine | Fit for the native catalog | Integration cost |
| --- | --- | --- |
| Microsoft.TemplateEngine | Runnable source layouts, replacements, conditionals and output renaming closely match the workflow. | The host still needs to constrain allowed operations, keep post-actions outside rendering and map compiler capabilities into the wizard. [Microsoft template manifest](https://github.com/dotnet/templating/wiki/Reference-for-template.json) · [Post-actions](https://github.com/dotnet/templating/wiki/Post-Action-Registry) |
| Scriban | Supports substitutions, conditions and diagnostic source locations through its parser. | Pitstop would need to restrict the scripting surface, define safe objects and functions, and implement typed manifests, filenames and transactional writes. [Scriban runtime and language](https://github.com/scriban/scriban) |
| Fluid | Liquid syntax provides conditions, output and configurable rendering limits. | The host must configure strict resolution and bounded rendering, control file providers, and independently implement typed parameters and the filesystem plan. [Fluid security and rendering](https://github.com/sebastienros/fluid) |
| Purpose-built renderer | A bounded grammar can express named substitutions and boolean conditions in the initial catalog. | Pitstop owns the compatibility rules and diagnostics. The manifest, path validation and write plan still need independent tests. |

Use the bounded declarative renderer for the first schema. Give the renderer validated scalar values and explicit escaping modes. Keep parameter validation, capability discovery and path resolution outside interpolation. Compile a plan before writing and let the wizard inspect that plan. Run optional repository initialization in the host only after confirmation. This decision describes the planned engine.
