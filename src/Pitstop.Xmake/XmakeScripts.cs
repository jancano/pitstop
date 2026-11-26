namespace Pitstop.Xmake;

internal static class XmakeScripts
{
    // show --target reports add_files declarations, which can contain globs. These are the evaluated paths.
    internal const string Membership = """
        import("core.base.json")
        import("core.project.config")
        import("core.project.project")

        function main(output)
            config.load()
            local targets = {}
            for name, target in pairs(project.targets()) do
                local chains = {}
                for _, chain in ipairs(target:toolchains()) do
                    table.insert(chains, chain:name())
                end
                table.insert(targets, {
                    name = name,
                    sources = json.mark_as_array(target:sourcefiles() or {}),
                    headers = json.mark_as_array(target:headerfiles() or {}),
                    toolchains = json.mark_as_array(chains)
                })
            end
            io.writefile(assert(output), json.encode({
                configuration_file = config.filepath(),
                toolchain = config.get("toolchain"),
                project_files = json.mark_as_array(project.allfiles()),
                targets = json.mark_as_array(targets)
            }))
        end
        """;

    // The project plugin loads config again. Prime the same process with require=no before invoking it.
    internal const string CompilationDatabase = """
        import("core.base.task")

        function main(output)
            assert(output)
            task.run("config", {require = "no", yes = true}, {loadonly = true})
            task.run("project", {kind = "compile_commands", lsp = "clangd", outputdir = output})
        end
        """;
}
