using Avalonia.Controls;
using Avalonia.Media;
using Pitstop.Core;
using Pitstop.Cpp;
using Pitstop.Modules;
using Pitstop.Xmake;

namespace Pitstop.App.Settings;

public sealed partial class SettingsDialog
{
    internal string? NativeWorkspaceFolder { get; init; }
    internal XmakeConfiguration? NativeConfiguration { get; init; }

    private void AddNativeToolchains()
    {
        NativeToolPaths paths = Working.NativeTools;
        Field("Xmake executable", paths.Xmake, (p, v) => p with { Xmake = v }, XmakeService.ExecutableVariable);
        Field("Xrepo executable", paths.Xrepo, (p, v) => p with { Xrepo = v });
        Field("LLVM bin directory", paths.LlvmBin, (p, v) => p with { LlvmBin = v }, Clangd.LlvmBinVariable);
        Field("Clang executable", paths.Clang, (p, v) => p with { Clang = v });
        Field("clangd executable", paths.Clangd, (p, v) => p with { Clangd = v }, Clangd.ExecutableVariable);
        Field("clang-scan-deps executable", paths.ScanDeps, (p, v) => p with { ScanDeps = v }, ModuleScanner.ExecutableVariable);
        Field("clang-format executable", paths.Format, (p, v) => p with { Format = v });
        Field("clang-tidy executable", paths.Tidy, (p, v) => p with { Tidy = v });

        TextBlock result = new() { Name = "NativeToolchainReport", TextWrapping = TextWrapping.Wrap };
        Button discover = new() { Content = "Detect Toolchains", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        CancellationTokenSource lifetime = new();
        Closed += (_, _) =>
        {
            lifetime.Cancel();
            lifetime.Dispose();
        };
        discover.Click += (_, _) => IdeErrors.Observe(DetectAsync());
        Add(NativeToolchainsPage, "Installed tools", new StackPanel { Spacing = Gaps.Wide, Children = { discover, result } },
            "discover rediscover validate version P1689 standard library platform SDK native C C++ toolchains",
            "Recommended on Windows x64: Xmake and Xrepo 3.1.2, LLVM-MinGW 23.1.6, and libc++. Set the active build compiler and SDK in Xmake.");

        void Field(string label, string? path, Func<NativeToolPaths, string?, NativeToolPaths> write, string? variable = null)
        {
            TextBox input = new() { Text = path, MaxWidth = OptionRow.EditorWidth, PlaceholderText = variable ?? "Automatic" };
            input.TextChanged += (_, _) => Change(settings => settings with
            {
                NativeTools = write(settings.NativeTools, string.IsNullOrWhiteSpace(input.Text) ? null : input.Text.Trim()),
            });
            Add(NativeToolchainsPage, label, input, $"{label} native toolchain path", variable is null ? null : $"Empty uses {variable} when set.");
        }

        async Task DetectAsync()
        {
            CancellationToken cancel = lifetime.Token;
            discover.IsEnabled = false;
            result.Text = "Checking installed tools…";
            string? folder = NativeWorkspaceFolder;
            NativeToolPaths current = Working.NativeTools;
            try
            {
                string report = await BackgroundWork.Run(() => current.InspectAsync(path =>
                    folder is null || !PathNames.Within(path, folder) || WorkspaceTrust.Trusts(folder), cancel), cancel);
                if (!cancel.IsCancellationRequested)
                {
                    result.Text = current != Working.NativeTools ? "Tool paths changed during detection. Detect toolchains again."
                        : NativeConfiguration is { } active
                        ? $"Active Xmake configuration: {active.Mode} · {active.Platform} · {active.Architecture} · {active.Toolchain ?? "Xmake default"}\n\n{report}"
                        : report;
                }
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                result.Text = error.Message;
            }
            finally
            {
                discover.IsEnabled = true;
            }
        }
    }
}
