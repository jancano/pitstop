using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Pitstop.App.ToolWindows;
using Pitstop.Core;
using Pitstop.Editor;
using Pitstop.Templates;

namespace Pitstop.App.Templates;

public sealed class NativeProjectDialog : IdeWindow
{
    private readonly Func<string, string?, Task<string?>> pickFolder;
    private readonly Func<CancellationToken, Task<NativeTemplateSetup>> load;
    private readonly Action<GenerationPlan> created;
    private readonly TextBox search = SearchBox.Of("Search templates");
    private readonly ComboBox language = new FormDropDown();
    private readonly ComboBox category = new FormDropDown();
    private readonly ComboBox provider = new FormDropDown();
    private readonly ListBox templates = new() { Name = "NativeTemplates", Classes = { StyleClasses.Tree } };
    private readonly ListBox files = new() { Name = "GeneratedFiles", Classes = { StyleClasses.Tree } };
    private readonly TextBox text = new() { Name = "GeneratedContents", IsReadOnly = true, AcceptsReturn = true,
        Classes = { StyleClasses.Code }, MinHeight = 130 };
    private readonly TextBox location = new() { Name = "ProjectLocation" };
    private readonly TextBlock toolchain = new() { Text = "Checking Xmake and LLVM…", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock description = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel parameters = new() { Spacing = FormRows.Between };
    private readonly Button create = DialogFooter.Default("Create Project");
    private readonly Dictionary<string, (Control Field, Control Row)> fields = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Debounce debounce;
    private NativeTemplateSetup? setup;
    private GenerationPlan? plan;
    private CancellationTokenSource? preview;
    private bool updating;
    private bool writing;

    internal NativeProjectDialog(string parent, Func<string, string?, Task<string?>> pickFolder, Action<GenerationPlan> created,
        Func<CancellationToken, Task<NativeTemplateSetup>>? load = null)
    {
        this.pickFolder = pickFolder;
        this.created = created;
        this.load = load ?? NativeTemplateSetup.LoadAsync;
        Title = "New Project";
        Width = DialogWidths.Workspace;
        Height = 730;
        FormRows.Scope(this);
        debounce = new(TimeSpan.FromMilliseconds(150), RefreshAsync);
        location.Text = parent;
        create.IsEnabled = false;
        create.Click += (_, _) => IdeErrors.Observe(CreateAsync());
        search.TextChanged += (_, _) => Filter();
        language.SelectionChanged += (_, _) => Filter();
        category.SelectionChanged += (_, _) => Filter();
        provider.SelectionChanged += (_, _) => Filter();
        templates.SelectionChanged += (_, _) => BuildForm();
        files.SelectionChanged += (_, _) => ShowFile();
        location.TextChanged += (_, _) => Changed();
        ReusedRows.Use(templates, TemplateRow);

        Grid catalog = new() { Margin = new Thickness(Gaps.Normal), RowDefinitions = new RowDefinitions("Auto,*") };
        StackPanel filters = new() { Spacing = FormRows.Between, Children =
        {
            search, FormRows.Of("Language", language), FormRows.Of("Category", category), FormRows.Of("Build system", provider),
        } };
        catalog.Children.Add(filters);
        Grid.SetRow(templates, 1);
        catalog.Children.Add(templates);
        StackPanel detail = new() { Margin = new Thickness(Gaps.Normal), Spacing = FormRows.Between, Children =
        {
            description, toolchain, FormRows.Of("Location", Browse(location)), parameters, status,
            new TextBlock { Text = "Generated files", Classes = { StyleClasses.PaneTitle } }, files, text,
        } };
        ScrollViewer page = new() { Content = detail };
        Grid.SetColumn(page, 1);
        Grid body = new() { ColumnDefinitions = new ColumnDefinitions("*,2*"), Children = { catalog, page } };
        Content = DialogFooter.Page(body, DialogFooter.OkCancel(create, Close));
        Opened += (_, _) => IdeErrors.Observe(LoadAsync());
        Closed += (_, _) =>
        {
            debounce.Stop();
            lifetime.Cancel();
            preview?.Cancel();
        };
    }

    private async Task LoadAsync()
    {
        CancellationToken cancel = lifetime.Token;
        try
        {
            NativeTemplateSetup loaded = await BackgroundWork.Run(() => load(cancel), cancel);
            cancel.ThrowIfCancellationRequested();
            setup = loaded with { Catalog = loaded.Catalog with
            {
                Templates = [.. loaded.Catalog.Templates.Where(item => item.Manifest.BuildSystem == "xmake")],
            } };
            toolchain.Text = setup.Toolchain;
            updating = true;
            SetFilter(language, setup.Catalog.Templates.Select(item => LanguageLabel(item.Manifest.Language)));
            SetFilter(category, setup.Catalog.Templates.Select(item => item.Manifest.Category));
            SetFilter(provider, setup.Catalog.Templates.Select(item => ProviderLabel(item.Manifest.BuildSystem)));
            updating = false;
            Filter();
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            status.Text = error.Message;
        }
    }

    private static void SetFilter(ComboBox box, IEnumerable<string> entries)
    {
        box.ItemsSource = new[] { "All" }.Concat(entries.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)).ToArray();
        box.SelectedIndex = 0;
    }

    private void Filter()
    {
        if (updating || setup is null)
        {
            return;
        }
        string needle = search.Text?.Trim() ?? "";
        TemplateChoice[] choices = [.. setup.Catalog.Templates.Where(item => Included(language, LanguageLabel(item.Manifest.Language))
            && Included(category, item.Manifest.Category) && Included(provider, ProviderLabel(item.Manifest.BuildSystem))
            && $"{item.Manifest.Name} {item.Manifest.Description}".Contains(needle, StringComparison.OrdinalIgnoreCase))
            .Select(item => new TemplateChoice(item, Incompatible(item.Manifest, setup.Environment)))];
        string? before = (templates.SelectedItem as TemplateChoice)?.Project.Manifest.Id;
        templates.ItemsSource = choices;
        templates.SelectedItem = choices.FirstOrDefault(choice => choice.Project.Manifest.Id == before)
            ?? choices.FirstOrDefault(choice => choice.Problem is null) ?? choices.FirstOrDefault();
        if (choices.Length == 0)
        {
            status.Text = setup.Catalog.Problems.FirstOrDefault()?.Message ?? "No templates match.";
        }

        static bool Included(ComboBox box, string value) => box.SelectedIndex <= 0 || (string?)box.SelectedItem == value;
    }

    private static string LanguageLabel(string language) => language switch { "c" => "C", "cpp" => "C++", "none" => "Any language", _ => language };

    private static string ProviderLabel(string provider) => provider == "xmake" ? "Xmake" : provider;

    private static string? Incompatible(TemplateManifest template, TemplateEnvironment environment)
    {
        if (!template.RequiresCapabilities.IsDefaultOrEmpty && !template.RequiresCapabilities.All(environment.Capabilities.Contains))
        {
            return "Requires a toolchain with " + string.Join(", ", template.RequiresCapabilities.Where(feature => !environment.Capabilities.Contains(feature))
                .Select(feature => feature == "cpp.modules" ? "C++ named modules" : feature)) + ".";
        }
        return template.Parameters.Any(parameter => parameter.Type == TemplateParameterKind.LanguageStandard && parameter.Required)
            && !environment.Standards.Available(template, environment.VerifiedStandards).Any()
            ? "No compatible language standard was verified on this toolchain."
            : null;
    }

    private static Control TemplateRow()
    {
        RowIcon icon = new();
        TextBlock label = new() { TextTrimming = TextTrimming.CharacterEllipsis };
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = Gaps.Tight, Children = { icon, label } };
        return row.Showing<TemplateChoice>(choice =>
        {
            icon.DataKey = choice.Project.Manifest.Category switch
            {
                "Application" => IconKeys.Terminal,
                "Library" => IconKeys.ClassLibrary,
                _ => IconKeys.Explorer,
            };
            label.Text = choice.ToString();
            label.Opacity = choice.Problem is null ? 1 : 0.65;
            ToolTip.SetTip(row, choice.Problem);
        });
    }

    private void BuildForm()
    {
        updating = true;
        fields.Clear();
        parameters.Children.Clear();
        description.Text = "";
        if (setup is { } loaded && templates.SelectedItem is TemplateChoice choice)
        {
            TemplateManifest manifest = choice.Project.Manifest;
            description.Text = manifest.Description;
            foreach (TemplateParameter parameter in manifest.Parameters)
            {
                Control field = MakeField(loaded.Environment, manifest, parameter);
                if (parameter.Help is { } help)
                {
                    ToolTip.SetTip(field, help);
                }
                Control editor = parameter.Type == TemplateParameterKind.Directory && field is TextBox path ? Browse(path) : field;
                Control row = FormRows.Of(field is CheckBox ? null : parameter.Label, editor);
                fields.Add(parameter.Id, (field, row));
                parameters.Children.Add(row);
            }
        }
        updating = false;
        Changed();
    }

    private Control MakeField(TemplateEnvironment environment, TemplateManifest template, TemplateParameter parameter)
    {
        string? value = parameter.Default.ValueKind == JsonValueKind.String ? parameter.Default.GetString()
            : parameter.Default.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : parameter.Default.GetRawText();
        switch (parameter.Type)
        {
            case TemplateParameterKind.Boolean:
                CheckBox toggle = new() { IsChecked = value == "true", Content = parameter.Label };
                toggle.IsCheckedChanged += (_, _) => Changed();
                return toggle;
            case TemplateParameterKind.Choice or TemplateParameterKind.LanguageStandard or TemplateParameterKind.Toolchain:
                FieldChoice[] options = parameter.Type switch
                {
                    TemplateParameterKind.LanguageStandard => [.. environment.Standards.Available(template, environment.VerifiedStandards)
                        .Select(standard => new FieldChoice(standard.Id, standard.Label, standard.Recommended))],
                    TemplateParameterKind.Toolchain => [.. environment.Toolchains.Order(StringComparer.Ordinal).Select(name => new FieldChoice(name, name))],
                    _ => parameter.Choices.IsDefault ? [] : [.. parameter.Choices.Select(name => new FieldChoice(name, name))],
                };
                ComboBox box = new FormDropDown { ItemsSource = options, Name = parameter.Id == "standard" ? "LanguageStandard" : parameter.Id };
                box.SelectedItem = options.FirstOrDefault(item => item.Id == value) ?? options.FirstOrDefault(item => item.Recommended) ?? options.FirstOrDefault();
                box.SelectionChanged += (_, _) => Changed();
                return box;
            case TemplateParameterKind.Integer:
                NumericUpDown number = new() { Minimum = int.MinValue, Maximum = int.MaxValue,
                    Value = int.TryParse(value, CultureInfo.InvariantCulture, out int parsed) ? parsed : null };
                number.ValueChanged += (_, _) => Changed();
                return number;
            default:
                TextBox input = new() { Text = value, Name = parameter.Id };
                input.TextChanged += (_, _) => Changed();
                return input;
        }
    }

    private Control Browse(TextBox path)
    {
        Button choose = new() { Content = "Browse…" };
        choose.Click += (_, _) => IdeErrors.Observe(BrowseAsync(path));
        DockPanel.SetDock(choose, Dock.Right);
        return new DockPanel { Children = { choose, path } };
    }

    private async Task BrowseAsync(TextBox path)
    {
        if (await pickFolder("Choose a project folder", path.Text) is { } selected && !lifetime.IsCancellationRequested)
        {
            path.Text = selected;
        }
    }

    private Dictionary<string, string> Values(TemplateManifest manifest, TemplateEnvironment environment)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        LanguageStandard? standard = null;
        foreach (TemplateParameter parameter in manifest.Parameters)
        {
            (Control field, Control row) = fields[parameter.Id];
            bool active = (parameter.RequiresCapability is not { } feature || environment.Capabilities.Contains(feature))
                && (parameter.When is not { } condition || result.GetValueOrDefault(condition.Parameter) == condition.EqualsValue)
                && (parameter.RequiresStandardCapability is not { } requirement || standard is { } revision
                    && !revision.Capabilities.IsDefaultOrEmpty && revision.Capabilities.Contains(requirement, StringComparer.Ordinal));
            row.IsVisible = active;
            string? value = field switch
            {
                TextBox box => box.Text,
                CheckBox toggle => active && toggle.IsChecked == true ? "true" : "false",
                ComboBox { SelectedItem: FieldChoice choice } => choice.Id,
                NumericUpDown number => number.Value?.ToString(CultureInfo.InvariantCulture),
                _ => null,
            };
            if (value is not null && (active || field is CheckBox))
            {
                result.Add(parameter.Id, value);
            }
            if (parameter.Type == TemplateParameterKind.LanguageStandard)
            {
                standard = environment.Standards.Entries.FirstOrDefault(item => item.Id == value);
            }
        }
        return result;
    }

    private void Changed()
    {
        if (!updating && !writing && !lifetime.IsCancellationRequested)
        {
            plan = null;
            create.IsEnabled = false;
            files.ItemsSource = null;
            preview?.Cancel();
            debounce.Restart();
        }
    }

    private async Task RefreshAsync()
    {
        if (setup is not { } loaded || templates.SelectedItem is not TemplateChoice choice || lifetime.IsCancellationRequested)
        {
            return;
        }
        if (choice.Problem is { } problem)
        {
            status.Text = problem;
            return;
        }
        using CancellationTokenSource current = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        preview = current;
        try
        {
            Dictionary<string, string> values = Values(choice.Project.Manifest, loaded.Environment);
            if (values.GetValueOrDefault("projectName") is not { Length: > 0 } name || !Path.IsPathFullyQualified(location.Text ?? ""))
            {
                status.Text = "Enter a project name and an existing parent directory.";
                return;
            }
            if (name.IndexOfAny(['/', '\\', ':']) >= 0 || name is "." or "..")
            {
                status.Text = "Enter a project name without a directory separator.";
                return;
            }
            string destination = Path.Combine(location.Text!, name);
            GenerationPlan ready = await BackgroundWork.Run(() => TemplateGeneration.PlanAsync(choice.Project, destination, loaded.Environment, values, current.Token), current.Token);
            current.Token.ThrowIfCancellationRequested();
            plan = ready;
            files.ItemsSource = ready.Files.Select(file => file.RelativePath).ToArray();
            files.SelectedIndex = 0;
            status.Text = ready.Destination;
            create.IsEnabled = true;
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            status.Text = error.Message;
        }
        finally
        {
            if (preview == current)
            {
                preview = null;
            }
        }
    }

    private void ShowFile()
    {
        text.Text = plan?.Files.FirstOrDefault(file => file.RelativePath == (string?)files.SelectedItem) is { } file
            ? file.Binary ? $"Binary file ({Plural.Of(file.Contents.Length, "byte")})" : Encoding.UTF8.GetString(file.Contents.AsSpan())
            : "";
    }

    private async Task CreateAsync()
    {
        if (writing || plan is not { } ready)
        {
            return;
        }
        writing = true;
        create.IsEnabled = false;
        try
        {
            await BackgroundWork.Run(() => ready.WriteAsync(lifetime.Token), lifetime.Token);
            if (!lifetime.IsCancellationRequested)
            {
                Close();
                created(ready);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            status.Text = error.Message;
            writing = false;
            create.IsEnabled = true;
        }
    }

    private sealed record TemplateChoice(Pitstop.Templates.ProjectTemplate Project, string? Problem)
    {
        public override string ToString() => Project.Manifest.Name;
    }

    private sealed record FieldChoice(string Id, string Label, bool Recommended = false)
    {
        public override string ToString() => Label;
    }
}
