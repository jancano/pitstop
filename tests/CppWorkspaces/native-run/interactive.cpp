#include <chrono>
#include <cstdlib>
#include <filesystem>
#include <iostream>
#include <string>
#include <thread>

#ifdef _WIN32
#include <windows.h>
__declspec(dllimport)
#endif
int runtime_value();

#ifdef _WIN32
int wmain(int count, wchar_t** arguments) {
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);
#else
int main(int count, char** arguments) {
#endif
    using namespace std::chrono_literals;
    if (count == 2 && std::filesystem::path(arguments[1]) == "--child") {
        for (;;) std::this_thread::sleep_for(1s);
    }
    for (int index = 1; index < count; ++index) {
        auto argument = std::filesystem::path(arguments[index]).u8string();
        std::cout << "arg[" << index << "]=" << reinterpret_cast<const char*>(argument.c_str()) << '\n';
    }
    auto directory = std::filesystem::current_path().u8string();
    std::cout << "cwd=" << reinterpret_cast<const char*>(directory.c_str()) << '\n';
    for (auto key : {"FIXTURE_RUNTIME", "FIXTURE_USER"}) {
        if (auto value = std::getenv(key)) std::cout << key << '=' << value << '\n';
    }
    std::cout << "runtime=" << runtime_value() << '\n';
    std::cerr << "fixture stderr\n";
    std::cout << "ready" << std::endl;
    for (std::string input; std::getline(std::cin, input);) {
        if (input == "fail") return 7;
        if (input == "quit") return 0;
#ifdef _WIN32
        if (input == "spawn") {
            wchar_t executable[32768];
            GetModuleFileNameW(nullptr, executable, std::size(executable));
            std::wstring command = L"\"" + std::wstring(executable) + L"\" --child";
            STARTUPINFOW start{};
            start.cb = sizeof(start);
            PROCESS_INFORMATION child{};
            if (CreateProcessW(executable, command.data(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &start, &child)) {
                std::cout << "child=" << child.dwProcessId << std::endl;
                CloseHandle(child.hThread);
                CloseHandle(child.hProcess);
            }
            continue;
        }
#endif
        std::cout << "echo=" << input << std::endl;
    }
    return 0;
}
