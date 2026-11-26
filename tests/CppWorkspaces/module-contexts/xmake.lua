set_project("pitstop-module-contexts")
set_languages("cxx23")
set_policy("build.c++.modules", true)
add_rules("mode.debug", "mode.release")

target("first")
    set_kind("static")
    add_files("different-filename.cppm", {public = true})

target("second")
    set_kind("static")
    add_files("other.cppm", {public = true})

target("consumer")
    set_kind("binary")
    add_files("selection.cpp")
    if is_mode("debug") then
        add_deps("first")
        add_defines("PITSTOP_SELECT_MODULE")
    else
        add_deps("second")
    end
