#include <array>
#include <atomic>
#include <chrono>
#include <concepts>
#include <cstdlib>
#include <filesystem>
#include <iostream>
#include <map>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <tuple>
#include <unordered_map>
#include <variant>
#include <vector>
#ifdef _WIN32
#include <windows.h>
#endif

extern "C" int c_sum(int, int);
extern "C" int shared_answer(int);

enum class Mode { ready = 4, done = 9 };
int global_value = 19;

struct Value {
    int number;
    explicit Value(int input) : number(input) {}
    ~Value() { global_value += number == 0; }
    int read() const { return number; }
};

template<std::integral T>
T add_one(T input)
{
    return input + 1; // template-stop
}

int inspect(int seed)
{
    Value value(seed);
    Mode mode = Mode::ready;
    int& reference = value.number;
    int* pointer = &reference;
    int* null_pointer = nullptr;
    std::string text = "debug text";
    std::string long_text(500, 'x');
    std::u8string unicode = u8"café λ";
    std::string_view view = text;
    std::vector<int> items{2, 3, 5};
    std::vector<int> empty;
    std::vector<std::vector<int>> nested{{2, 3}, {}};
    std::vector<int> large(1024, 7);
    std::array<int, 2> array{7, 8};
    std::map<int, int> ordered{{1, 4}};
    std::unordered_map<int, int> hashed{{2, 5}};
    std::optional<int> maybe{6};
    std::variant<int, Value> either{7};
    std::tuple<int, bool> tuple{8, true};
    auto unique = std::make_unique<Value>(9);
    auto shared = std::make_shared<Value>(10);
    Value moved(std::move(*unique));
    auto update = [&reference](int delta) { return c_sum(reference, delta); };
    int result = update(3); // locals-stop
    {
        int inner = add_one(result);
        result = shared_answer(inner); // shared-call
    }
    return result + value.read() + static_cast<int>(mode);
}

int run(const std::vector<std::string>& args)
{
    std::string_view command = args.empty() ? "" : std::string_view(args.front());
    if (command == "wait-child") {
        for (;;) std::this_thread::sleep_for(std::chrono::milliseconds(20));
    }
    if (command == "attach" || command == "wait") {
        std::atomic_bool ready = false;
        std::jthread worker([&](std::stop_token stop) {
            ready = true;
            while (!stop.stop_requested()) {
                std::this_thread::sleep_for(std::chrono::milliseconds(10));
            }
        });
        while (!ready) {
            std::this_thread::yield();
        }
#ifdef _WIN32
        if (command == "wait") {
            wchar_t executable[32768];
            if (GetModuleFileNameW(nullptr, executable, std::size(executable)) == 0) return 4;
            std::wstring arguments = std::wstring(L"\"") + executable + L"\" wait-child";
            STARTUPINFOW startup{};
            startup.cb = sizeof(startup);
            PROCESS_INFORMATION child{};
            if (!CreateProcessW(executable, arguments.data(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &startup, &child)) return 5;
            CloseHandle(child.hThread);
            CloseHandle(child.hProcess);
            std::cout << "owned-child=" << child.dwProcessId << '\n';
        }
#endif
        std::cout << "fixture ready" << std::endl;
        while (command == "wait") {
            std::this_thread::sleep_for(std::chrono::milliseconds(20));
        }
        std::string line;
        std::getline(std::cin, line);
        return 0;
    }
    if (command == "crash") {
        std::abort();
    }
    if (command == "exception") {
        try {
            throw std::runtime_error("fixture exception");
        } catch (const std::exception&) {
            return 7;
        }
    }
#ifdef _WIN32
    if (command == "dynamic-library") {
        HMODULE library = LoadLibraryW(L"libdebug-late-library.dll");
        if (!library) return 2;
        auto answer = reinterpret_cast<int(*)(int)>(GetProcAddress(library, "late_answer"));
        int result = answer ? answer(7) : 3;
        FreeLibrary(library);
        return result;
    }
#endif
    if (command == "interactive") {
        std::cout << "fixture ready" << std::endl;
        std::string line;
        std::getline(std::cin, line);
        std::cout << "echo=" << line << std::endl;
        std::cerr << "fixture stderr" << std::endl;
        return 17;
    }
    if (command == "breakpoints") {
        inspect(10);
        return inspect(11);
    }
    for (std::size_t i = 0; i < args.size(); ++i) {
        std::cout << "arg[" << i + 1 << "]=[" << args[i] << "]\n";
    }
    std::cout << "cwd=" << std::filesystem::current_path().filename().string() << '\n';
    if (const char* setting = std::getenv("PITSTOP_FIXTURE_MODE")) {
        std::cout << "mode=" << setting << '\n';
    }
    if (const char* list = std::getenv("PITSTOP_FIXTURE_LIST")) {
        std::cout << "list=" << list << '\n';
    }
    return inspect(11);
}

#ifdef _WIN32
int wmain(int argc, wchar_t** argv)
{
    std::vector<std::string> args;
    for (int i = 1; i < argc; ++i) {
        int length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, argv[i], -1, nullptr, 0, nullptr, nullptr);
        if (length == 0) return 2;
        std::string value(length, '\0');
        WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, argv[i], -1, value.data(), length, nullptr, nullptr);
        value.pop_back();
        args.push_back(std::move(value));
    }
    return run(args);
}
#else
int main(int argc, char** argv)
{
    return run({argv + 1, argv + argc});
}
#endif
