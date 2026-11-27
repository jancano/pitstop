set_project({{lua:projectName}})
add_rules("mode.debug", "mode.release")
{{#lockPackages}}set_policy("package.requires_lock", true)
{{/lockPackages}}local standard = {{lua:standard}}
set_languages((standard:gsub("^c%+%+", "cxx")))
target({{lua:targetName}})
    set_kind("shared")
    add_defines("PITSTOP_SHARED_EXPORTS")
    add_includedirs("include", {public = true})
    add_headerfiles("include/*.hpp")
    add_files("src/answer.cpp")
{{#consumer}}
target("{{id:targetName}}_consumer")
    set_kind("binary")
    add_deps({{lua:targetName}})
    add_files("example/main.cpp")
{{/consumer}}
