using Avalonia;
using Avalonia.Controls;

namespace Passenger.App;

/// <summary>The About dialog and the application menu on macOS.</summary>
public partial class MainWindow
{
    /// <summary>The actions of macOS's application menu, in order, a separator where null.</summary>
    private static readonly string?[] ApplicationActions = [AppActions.About, AppActions.CheckForUpdates, null, AppActions.ShowSettings];

    private void AddAbout() =>
        actions!.Add(new IdeAction(AppActions.About, OperatingSystem.IsMacOS() ? "About Pitstop" : "About", "Help", _ => ShowAbout())
        {
            Menus = OperatingSystem.IsMacOS() ? [] : [new MenuPlace("Help", 3, 2)],
        });

    /// <summary>About, over this window until it is closed; its links close it and open the notices or the licence in
    /// an editor here, read-only.</summary>
    public AboutDialog ShowAbout()
    {
        AboutDialog dialog = new(path => OpenFile(path));
        IdeErrors.Observe(dialog.ShowDialog(this));
        return dialog;
    }

    /// <summary>macOS's application menu, the process's one, which App gives the application before the platform takes
    /// it: About Passenger and Check for Updates, then Settings…, before what the system adds (Services, Hide, Quit).
    /// Null on other systems.</summary>
    public static NativeMenu? ApplicationMenu =>
        OperatingSystem.IsMacOS() && Application.Current is { } app ? NativeMenu.GetMenu(app) : null;

    /// <summary>The application menu's items, made at its top by the first window, since the platform keeps the menu it
    /// took, then given the titles and keys in force by each window as it builds its menu bar; each runs in the window
    /// in front.</summary>
    private void FollowApplicationMenu()
    {
        if (ApplicationMenu is not { } menu)
        {
            return;
        }
        if (!menu.Items.OfType<NativeMenuItem>().Any(OfApplication))
        {
            int at = 0;
            foreach (string? action in ApplicationActions)
            {
                if (action is not null && actions![action] is null)
                {
                    continue;
                }
                menu.Items.Insert(at++, action is { } id ? new NativeMenuItem(NativeWords(actions![id]!.Title))
                {
                    Command = new MenuCommand(() => RunInFront(id)),
                    CommandParameter = id,
                } : new NativeMenuItemSeparator());
            }
        }
        ActionContext context = actions!.Context();
        foreach (NativeMenuItem item in menu.Items.OfType<NativeMenuItem>().Where(OfApplication))
        {
            string id = (string)item.CommandParameter!;
            if (actions[id] is not { } action)
            {
                item.IsVisible = false;
                continue;
            }
            item.IsVisible = true;
            item.Header = NativeWords(action.TitleWhere(context));
            item.Gesture = actions.MenuBarGesture(id);
        }

        static bool OfApplication(NativeMenuItem item) => item.CommandParameter is string id && ApplicationActions.Contains(id);

        static void RunInFront(string id)
        {
            if (PassengerApp.Front is { IsVisible: true } front)
            {
                front.RunFromMenu(id);
            }
        }
    }
}
