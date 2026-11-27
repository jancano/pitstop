using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Templates;
using Passenger.Core;
using Pitstop.Templates;

namespace Passenger.Tests;

public sealed class NativeProjectDialogTests
{
    [Fact]
    public async Task Discovers_verified_standards_from_the_reference_toolchain()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("PITSTOP_XMAKE") is not null
            && Environment.GetEnvironmentVariable("PITSTOP_CLANGD") is not null,
            "Set PITSTOP_XMAKE and PITSTOP_CLANGD to the Windows reference tools to verify project wizard discovery.");
        NativeTemplateSetup setup = await NativeTemplateSetup.LoadAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("Xmake 3.1.2", setup.Toolchain, StringComparison.Ordinal);
        Assert.Contains("c++26", setup.Environment.VerifiedStandards);
        Assert.Contains("cpp.std-module", setup.Environment.Capabilities);
        Assert.Empty(setup.Catalog.Problems);
    }

    [AvaloniaFact]
    public void Previews_and_creates_a_native_project_without_bypassing_workspace_trust()
    {
        string root = TestFolders.Create("pitstop-native-wizard-");
        string destination = Path.Combine(root, "ReviewProject");
        using IDisposable asking = WorkspaceTrust.Ask(destination);
        MainWindow window = new();
        window.Show();
        try
        {
            NativeTemplateSetup setup = Setup();
            NativeProjectDialog dialog = window.ShowNativeProject(_ => Task.FromResult(setup));
            ListBox templates = Find<ListBox>(dialog, "NativeTemplates");
            Ui.WaitFor(() => templates.Items.Count == 7, "the bundled template catalog");
            templates.SelectedItem = templates.Items.Single(item => item?.ToString() == "C++ Console Application");
            Find<TextBox>(dialog, "ProjectLocation").Text = root;
            Find<TextBox>(dialog, "projectName").Text = "ReviewProject";
            ComboBox standard = Find<ComboBox>(dialog, "LanguageStandard");
            Assert.Equal("C++23", standard.SelectedItem?.ToString());
            ListBox preview = Find<ListBox>(dialog, "GeneratedFiles");
            Ui.WaitFor(() => preview.Items.Contains("src/answer.cppm"), "the C++23 module preview");
            Assert.False(Directory.Exists(destination));
            standard.SelectedItem = standard.Items.Single(item => item?.ToString() == "C++17");
            Ui.WaitFor(() => preview.Items.Contains("src/main.cpp") && !preview.Items.Contains("src/answer.cppm"), "the C++17 preview without a module");
            standard.SelectedItem = standard.Items.Single(item => item?.ToString() == "C++23");
            Ui.WaitFor(() => preview.Items.Contains("src/answer.cppm"), "the restored module preview");
            preview.SelectedItem = "src/main.cpp";
            string expected = Find<TextBox>(dialog, "GeneratedContents").Text!;
            Capture(dialog);
            Ui.ClickButton(dialog, "Create Project");
            Ui.WaitFor(() => window.AskingTrust is not null, "the generated workspace's trust prompt");
            Assert.False(Directory.Exists(Path.Combine(destination, ".xmake")));
            Assert.Equal(expected, File.ReadAllText(Path.Combine(destination, "src", "main.cpp")));
        }
        finally
        {
            window.Close();
            TestFolders.Delete(root);
        }
    }

    private static T Find<T>(Window window, string name) where T : Control =>
        Assert.Single(window.GetVisualDescendants().OfType<T>(), control => control.Name == name);

    private static NativeTemplateSetup Setup()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "native-templates");
        using FileStream catalog = File.OpenRead(Path.Combine(root, "catalogs", "language-standards.json"));
        return new(Pitstop.Templates.TemplateCatalog.Discover([Path.Combine(root, "xmake")], TestContext.Current.CancellationToken),
            new(LanguageStandards.Read(catalog), ImmutableHashSet.Create("c++17", "c++23"), ImmutableHashSet.Create("cpp.modules", "cpp.std-module"),
                ImmutableHashSet.Create("llvm-mingw")), "Xmake 3.1.2 · LLVM-MinGW 23.1.6 · Windows x64");
    }

    private static void Capture(Window dialog)
    {
        if (Environment.GetEnvironmentVariable("PITSTOP_TEST_TEMPLATE_SHOTS") is not { Length: > 0 } folder)
        {
            return;
        }
        Directory.CreateDirectory(folder);
        ThemeVariant? previous = dialog.RequestedThemeVariant;
        foreach ((ThemeVariant theme, string name) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
        {
            dialog.RequestedThemeVariant = theme;
            Ui.Settle();
            using WriteableBitmap frame = dialog.CaptureRenderedFrame() ?? throw new InvalidOperationException("No project wizard frame was rendered.");
            frame.Save(Path.Combine(folder, $"new-project-{name}.png"), PngBitmapEncoderOptions.Default);
        }
        dialog.RequestedThemeVariant = previous;
    }
}
