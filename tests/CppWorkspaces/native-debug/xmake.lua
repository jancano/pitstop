set_project("pitstop-native-debug")
set_languages("c17", "cxx23")
add_rules("mode.debug", "mode.release", "mode.releasedbg")

target("debug-library")
    set_kind("shared")
    set_targetdir("build/library/$(mode)")
    add_files("library.cpp")

target("debug-fixture")
    set_kind("binary")
    add_deps("debug-library")
    if is_plat("mingw") then
        add_deps("debug-late-library", {inherit = false})
    end
    add_files("main.cpp", "c_math.c")
    if is_plat("mingw") then
        add_ldflags("-municode", {force = true})
    end
    set_runargs("configured argument")
    set_rundir("run-directory")
    set_runenv("PITSTOP_FIXTURE_MODE", "evaluated")
    add_runenvs("PITSTOP_FIXTURE_LIST", "first", "second")

if is_plat("mingw") then
    target("debug-late-library")
        set_kind("shared")
        set_targetdir("build/late-library/$(mode)")
        add_files("late_library.cpp")
    target_end()
end

target("custom-run")
    set_kind("binary")
    add_files("custom.cpp")
    on_run(function (_) print("custom-run replaced the executable") end)
