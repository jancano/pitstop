using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Passenger.Core;
using Pitstop.Modules;
using Line = Avalonia.Controls.Shapes.Line;

namespace Passenger.App.ToolWindows;

public sealed class ModuleGraphView : DockPanel
{
    private readonly ComboBox modules = new FormDropDown();
    private readonly TextBox search = SearchBox.Finding("Filter modules or targets");
    private readonly CheckBox external = new() { Content = "External and standard library" };
    private readonly CheckBox transitive = new() { Content = "Transitive" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(Gaps.Normal) };
    private readonly Canvas diagram = new();
    private readonly ScrollViewer scroll;
    private readonly ScaleTransform zoom = new(1, 1);
    private ModuleWorkspace? graph;
    private TranslationUnit? document;
    private Point? drag;
    private Vector dragOffset;

    public ModuleGraphView()
    {
        StackPanel tools = new() { Orientation = Orientation.Horizontal, Spacing = Gaps.Normal, Margin = new Thickness(Gaps.Normal) };
        Button smaller = new() { Content = "−" };
        Button larger = new() { Content = "+" };
        smaller.Click += (_, _) => Scale(0.8);
        larger.Click += (_, _) => Scale(1.25);
        tools.Children.AddRange([search, modules, external, transitive, smaller, larger]);
        StackPanel header = new() { Children = { tools, status } };
        SetDock(header, Dock.Top);
        Children.Add(header);
        scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new LayoutTransformControl { LayoutTransform = zoom, Child = diagram },
        };
        Children.Add(scroll);
        modules.SelectionChanged += (_, _) => Draw();
        search.TextChanged += (_, _) => Offer(modules.SelectedItem as Choice);
        external.IsCheckedChanged += (_, _) => Offer(modules.SelectedItem as Choice);
        transitive.IsCheckedChanged += (_, _) => Draw();
        scroll.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(scroll).Properties.IsMiddleButtonPressed)
            {
                drag = e.GetPosition(scroll);
                dragOffset = scroll.Offset;
                e.Pointer.Capture(scroll);
            }
        };
        scroll.PointerMoved += (_, e) =>
        {
            if (drag is { } start)
            {
                scroll.Offset = dragOffset + (start - e.GetPosition(scroll));
            }
        };
        scroll.PointerReleased += (_, args) =>
        {
            if (drag is not null)
            {
                drag = null;
                args.Pointer.Capture(null);
            }
        };
    }

    public event EventHandler<string>? GoTo;

    internal void Show(ModuleWorkspace? snapshot, string state, ModuleProvider? focus = null, TranslationUnit? activeDocument = null)
    {
        graph = snapshot;
        document = activeDocument;
        status.Text = state;
        Choice? selected = focus is not null ? new(focus.Unit, focus) : activeDocument is not null ? new(activeDocument, null) : modules.SelectedItem as Choice;
        if (focus is { Origin: not ModuleOrigin.Workspace })
        {
            external.IsChecked = true;
        }
        Offer(selected);
    }

    private void Offer(Choice? wanted)
    {
        string query = search.Text?.Trim() ?? string.Empty;
        IEnumerable<Choice> available = graph is null ? [] : graph.Providers
            .Where(provider => external.IsChecked == true || provider.Origin == ModuleOrigin.Workspace)
            .Select(provider => new Choice(provider.Unit, provider));
        if (document is { } active && graph?.Providers.Any(provider => provider.Unit.Id == active.Id) == false)
        {
            available = available.Prepend(new(active, null));
        }
        Choice[] items = [.. available.Where(item => item.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Unit.Context.Target, StringComparer.Ordinal).ThenBy(item => item.Name, StringComparer.Ordinal)];
        modules.ItemsSource = items;
        modules.SelectedItem = items.FirstOrDefault(item => wanted is not null && (wanted.Provider is null || item.Provider?.Identity == wanted.Provider.Identity)
            && item.Unit.Context == wanted.Unit.Context && PathNames.Same(item.Unit.Source, wanted.Unit.Source)) ?? items.FirstOrDefault();
        Draw();
    }

    private void Draw()
    {
        diagram.Children.Clear();
        if (graph is not { } snapshot || modules.SelectedItem is not Choice chosen)
        {
            return;
        }
        TranslationUnit root = chosen.Unit;
        bool all = transitive.IsChecked == true;
        ILookup<int, ModuleProvider> providers = snapshot.Providers.ToLookup(provider => provider.Unit.Id);
        Item[] dependencies = all ? [.. snapshot.Reachable(root, consumers: false).Where(Shown).Select(ItemOf)]
            : [.. snapshot.Imports(root).Where(edge => edge.Provider is null || Shown(edge.Provider.Unit)).Select(edge => edge.Provider is { } provider ? ItemOf(provider.Unit)
                : new Item(edge.Name, edge.Resolution.ToString(), null))];
        Item[] consumers = [.. (all ? snapshot.Reachable(root, consumers: true) : snapshot.Consumers(root).Select(edge => edge.Consumer))
            .Where(Shown).DistinctBy(unit => unit.Id).Select(ItemOf)];
        int rows = Math.Max(dependencies.Length, consumers.Length);
        diagram.Width = 810;
        diagram.Height = Math.Max(140, 100 + Math.Min(rows, 150) * 48);
        Label(all ? "Transitive dependencies" : "Direct dependencies", 20);
        Label(snapshot.IsCyclic(root) ? "Selected unit · dependency cycle" : "Selected compilation unit", 285);
        Label(all ? "Potential transitive impact" : "Direct consumers", 550);
        Node(ItemOf(root), 285, 56);
        Column(dependencies, 20, fromLeft: true);
        Column(consumers, 550, fromLeft: false);

        Item ItemOf(TranslationUnit unit) => new(providers[unit.Id].FirstOrDefault()?.Name ?? Path.GetFileName(unit.Source),
            unit.Context.Target ?? "Unknown target", unit.Source);

        bool Shown(TranslationUnit unit) => external.IsChecked == true || PathNames.Within(unit.Source, snapshot.Workspace.Root);

        void Column(Item[] items, int x, bool fromLeft)
        {
            for (int index = 0; index < Math.Min(150, items.Length); index++)
            {
                int y = 56 + index * 48;
                if (!all)
                {
                    Line line = new() { StartPoint = new Point(fromLeft ? 250 : 515, fromLeft ? y + 18 : 74),
                        EndPoint = new Point(fromLeft ? 285 : 550, fromLeft ? 74 : y + 18), StrokeThickness = 1 };
                    line.Bind(Line.StrokeProperty, line.GetResourceObservable(TokenKeys.TextMuted));
                    diagram.Children.Insert(0, line);
                }
                Node(items[index], x, y);
            }
            if (items.Length > 150)
            {
                Label($"Showing 150 of {Numbers.Said(items.Length)}", x, 56 + 150 * 48);
            }
        }
    }

    private void Label(string text, int x, int y = 16)
    {
        TextBlock label = new() { Text = text };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        diagram.Children.Add(label);
    }

    private void Node(Item item, int x, int y)
    {
        Button node = new() { Content = $"{item.Name}  ·  {item.Detail}", Width = 230, HorizontalContentAlignment = HorizontalAlignment.Left,
            IsEnabled = item.Source is not null };
        ToolTip.SetTip(node, item.Source ?? item.Detail);
        node.Click += (_, _) => GoTo?.Invoke(this, item.Source!);
        Canvas.SetLeft(node, x);
        Canvas.SetTop(node, y);
        diagram.Children.Add(node);
    }

    private void Scale(double factor) => zoom.ScaleX = zoom.ScaleY = Math.Clamp(zoom.ScaleX * factor, 0.5, 2);

    private sealed record Item(string Name, string Detail, string? Source);

    private sealed record Choice(TranslationUnit Unit, ModuleProvider? Provider)
    {
        public string Name => Provider?.Name ?? Path.GetFileName(Unit.Source);
        public override string ToString() => $"{Name} · {Unit.Context.Target ?? Provider?.Origin.ToString() ?? "Unknown target"}";
    }
}
