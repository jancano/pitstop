# Hello Modules

This C++23 workspace uses Xmake to build the `greetings` library and the `hello-modules` executable. The library re-exports `greetings:punctuation`. Its implementation unit imports the internal `greetings:words` partition. The executable imports `greetings` and the libc++ standard library module `std`.

Configure the project with Xmake 3.1.2 and LLVM-MinGW 23.1.6 on Windows x64. Run these commands from this directory.

```powershell
$llvm = 'C:\path\to\llvm-mingw'
$env:Path = "$llvm\bin;$env:Path"
xmake f -m debug -p mingw --toolchain=llvm "--mingw=$llvm" "--sdk=$llvm" --require=no -y
xmake build
xmake run hello-modules
```

The program prints `Hello, modules!`. Open this folder in Pitstop and grant trust after reviewing `xmake.lua`. Select **Xmake: hello-modules** in the toolbar and use **Build** or **Run**. In the Modules view, open `greetings` and choose **Find Module Consumers** to open the graph. In `greetings.cppm`, edit the `export import :punctuation;` line to `export import :` and invoke completion. The editor offers `:punctuation`. Undo the edit before running the sample again.

To debug, keep the Xmake **debug** configuration and use the `lldb-dap.exe` included in the reference LLVM-MinGW bundle. If it is outside the configured LLVM directory and PATH, select it in **Settings → Native Toolchains**. In `greetings-implementation.cpp`, click the gutter next to the return statement, then choose **Run → Debug**. The **Debug** window pauses in the module implementation so you can inspect the Call Stack and Variables. Choose **Continue** to finish running the sample. [Native Debugging](../../docs/architecture/native-debugging.md)
