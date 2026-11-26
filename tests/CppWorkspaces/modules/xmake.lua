set_project("pitstop-modules")
set_languages("cxx23")
set_policy("build.c++.modules", true)

target("arithmetic")
    set_kind("static")
    add_files("detail.cppm", "implementation.cpp")
    add_files("arithmetic.cppm", "numbers.cppm", {public = true})

target("modules")
    set_kind("binary")
    add_deps("arithmetic")
    add_files("main.cpp")
