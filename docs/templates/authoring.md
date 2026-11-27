# Authoring project templates

The `Pitstop.Templates` library discovers directories containing a `template.json` manifest. Callers supply the catalog roots in precedence order. The first valid manifest for an ID owns that ID. Duplicate IDs, invalid metadata and missing source files are reported alongside the catalog. This library boundary is available to the IDE integration. The New Project wizard and installed local template location are subsequent work.

Use the [version 1 JSON schema](../../templates/schemas/template.schema.json) when writing a manifest. `files` lists source paths relative to the manifest directory and destination paths relative to the generated project. Keep source files beside the manifest and use `binary: true` to copy bytes verbatim. Text inputs use UTF-8. Generated text uses UTF-8 without a BOM and keeps the template's line endings.

Parameters use the manifest's order. A parameter's `when` condition can test the string form of an earlier value. File conditions can inspect the full set of active values. Standard and toolchain choices are checked against the caller's verified toolchain data. `requiresCapability` checks the selected toolchain. `requiresStandardCapability` additionally checks the preceding standard choice. An unavailable boolean contributes `false` to rendering. The [language-standard catalog](../../templates/catalogs/language-standards.json) supplies labels and revision metadata. A catalog capability alone does not establish that a compiler supports it.

| Template token | Result |
| --- | --- |
| `{{projectName}}` in a path | A validated portable filename component |
| `{{standard}}` in text | A scalar consisting of ASCII letters, digits, `.`, `_`, `+` or `-` |
| `{{lua:displayName}}` or `{{c:displayName}}` | An escaped, double-quoted string literal |
| `{{id:projectName}}` | An ASCII identifier with the `project_` prefix |
| `{{#modules}}…{{/modules}}` | Text included when the parameter is active and equals `true` |
| `{{^modules}}…{{/modules}}` | Text included when the parameter has no active true value |

Plan a project in a new directory under an existing parent. The generation plan exposes the bytes for each output before writing. Applying the plan stages those same bytes and moves the staged project into place after a final collision check. Relative paths, filename components, links and output collisions are validated. Template generation performs filesystem operations only. Opening a generated workspace and running any initialization operations remain responsibilities of the host and its trust flow.
