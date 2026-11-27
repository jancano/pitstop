set_project({{lua:projectName}})
add_rules("mode.debug", "mode.release")
{{#lockPackages}}set_policy("package.requires_lock", true)
{{/lockPackages}}
