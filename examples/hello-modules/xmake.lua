set_project("hello-modules")
set_languages("cxx23")
set_policy("build.c++.modules", true)

target("greetings")
    set_kind("static")
    add_files("greetings.cppm", "punctuation.cppm", {public = true})
    add_files("words.cppm", "greetings-implementation.cpp")

target("hello-modules")
    set_kind("binary")
    add_deps("greetings")
    add_files("main.cpp")
