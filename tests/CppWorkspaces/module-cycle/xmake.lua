set_project("pitstop-module-cycle")
set_languages("cxx23")
set_policy("build.c++.modules", true)

target("cycle")
    set_kind("static")
    add_files("cycle-one.cppm", "cycle-two.cppm")
