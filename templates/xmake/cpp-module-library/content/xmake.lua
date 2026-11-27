set_project({{lua:projectName}})
add_rules("mode.debug", "mode.release")
{{#lockPackages}}set_policy("package.requires_lock", true)
{{/lockPackages}}local standard = {{lua:standard}}
set_languages((standard:gsub("^c%+%+", "cxx")))
set_policy("build.c++.modules", true)
target({{lua:targetName}})
    set_kind("moduleonly")
    add_files("src/answer.cppm", {public = true})
{{#consumer}}
target("{{id:targetName}}_consumer")
    set_kind("binary")
    add_deps({{lua:targetName}})
    add_files("example/main.cpp")
{{/consumer}}
