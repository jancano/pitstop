using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Passenger.Core;

namespace Passenger.App;

public partial class App : Application
{
    /// <summary>Whether the class handlers are in, which are the process's (static events), not an App's: a process may
    /// make several Apps, and each adding them again would make every template applied anywhere ask each one.</summary>
    private static bool classHandlersAdded;

    /// <summary>Makes the start fail in <see cref="Initialize"/>, the exception's message its value: what a start that fails
    /// before the log is set up leaves (<see cref="CrashFile"/>) is held by making one.</summary>
    [IdentifierHome]
    public const string FailVariable = "PITSTOP_START_FAILS";

    public override void Initialize()
    {
        if (Environment.GetEnvironmentVariable(FailVariable) is { Length: > 0 } failure)
        {
            throw new InvalidOperationException(failure);
        }
        AvaloniaXamlLoader.Load(this);
        if (OperatingSystem.IsMacOS())
        {
            // macOS takes the application's menu as setup ends, before any window: none set, it shows Avalonia's About.
            NativeMenus.Of(this, NativeMenu.MenuProperty);
        }
        Themes.TakeAccentFromTokens(this);
        Passenger.Editor.FallbackFonts.Install(PlatformFallback.Named, FaceTracking.Set);
        Passenger.Editor.CodeFonts.Install(PlatformWidths.Monospaced);
        Passenger.Editor.CodeFonts.Use(this, Passenger.Editor.CodeEditor.CodeFont);
        WindowFonts.Use(this, WindowFonts.Carried);
        InterfaceScale.Use(this, 1);
        Passenger.Editor.Carets.UseBlink(this, CaretBlink.Default);
        Passenger.Editor.Carets.UseWidth(this, Passenger.Editor.Carets.DefaultWidth);
        if (classHandlersAdded)
        {
            return;
        }
        classHandlersAdded = true;
        InterfaceScale.Install();
        MenuShadow.Install();
        AgentAsks.Install();
        DropDownSearch.Install();
        // In-window menus write keys as the keymap does (⇧⌘R), not as Fluent writes a KeyGesture (Shift+Cmd+R).
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<MenuItem>((item, e) =>
        {
            if (item.InputGesture is { } keys && e.NameScope.Find<TextBlock>("PART_InputGestureText") is { } shown)
            {
                shown.Text = KeyWords.Of(keys.Key, keys.KeyModifiers);
            }
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Whichever dispatcher is the UI thread's when a tracked task ends: a headless session can bring its own.
        BackgroundWork.Install(work => Dispatcher.UIThread.Post(work));
        IdeErrors.Install();
        SystemMotion.LookAgain();
        if (!PassengerApp.ShowsNoWindow)
        {
            ScreenReaders.LookAgain();
        }
        SystemContrast.LookAgain();
        if (MacTextElements.Install() is { } unread)
        {
            IdeErrors.Write($"VoiceOver reads the editor and the terminal by their caret's line alone: {unread}");
        }
        // The screenshot mode has no lifetime and makes its own window.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = new()
            {
                AgentsAllowed = Program.Options.ListenForAgents, SaysLastCrash = true,
            };
            // The person's windows open on the layout and settings they left; headless ones keep the defaults.
            window.KeepLayout();
            window.KeepSettings();
            // A theme named on the command line is the run's, over the one kept.
            Program.ApplyTheme(this, Program.Options);
            // A new state's first start asks the theme and dark or light before anything else, from the first frame.
            window.ShowFirstStartOnce();
            window.OpenWherePlaced();
            window.Opened += (_, _) =>
            {
                IdeErrors.Write($"Pitstop {Product.Version} ({Product.Commit ?? "no commit"}) on {RuntimeInformation.OSDescription} "
                    + $"({Product.Runtime}): window shown {Durations.Said(Stopwatch.GetElapsedTime(Program.StartedAt))} after launch");
                window.Start(Program.Options);
                IdeErrors.Observe(Program.EndUnlessStartedAsync(window, desktop.Shutdown));
            };
            desktop.MainWindow = window;
            // macOS hands the running Passenger what the Finder or the Dock's icon opens, rather than starting another.
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            {
                activatable.Activated += OnActivated;
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void OnActivated(object? sender, ActivatedEventArgs e)
    {
        if (e is FileActivatedEventArgs { Files: { Count: > 0 } files })
        {
            string[] paths = [.. files.Select(f => f.TryGetLocalPath()).OfType<string>()];
            IdeErrors.Write($"Opened by the system: {string.Join(", ", paths)}");
            UiThread.Post(() => PassengerApp.OpenFromSystem(paths));
        }
    }
}
