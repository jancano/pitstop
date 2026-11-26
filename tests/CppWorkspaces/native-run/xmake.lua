set_project("pitstop-native-run")
set_languages("cxx23")
add_rules("mode.debug", "mode.release")

target("runtime")
    set_kind("shared")
    set_targetdir("build/runtime/$(mode)")
    add_files("runtime.cpp")

target("interactive")
    set_kind("binary")
    add_files("interactive.cpp")
    add_deps("runtime")
    if is_plat("mingw") then
        add_ldflags("-municode", {force = true})
    end
    set_runargs("project default")
    set_runenv("FIXTURE_RUNTIME", "configured")
    before_run(function () print("before_run") end)
    after_run(function () print("after_run") end)

target("compile-error")
    set_kind("binary")
    set_default(false)
    add_files("compile-error.cpp")

target("link-error")
    set_kind("binary")
    set_default(false)
    add_files("link-error.cpp")
