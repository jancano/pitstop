set_project({{lua:projectName}})
add_rules("mode.debug", "mode.release")
{{#lockPackages}}set_policy("package.requires_lock", true)
{{/lockPackages}}local standard = {{lua:standard}}
set_languages((standard:gsub("^c%+%+", "cxx")))
{{#modules}}set_policy("build.c++.modules", true)
{{/modules}}target({{lua:targetName}})
    set_kind("binary")
    add_files("src/main.cpp")
{{#modules}}    add_files("src/answer.cppm")
{{/modules}}
