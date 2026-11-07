using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using KeySwitch.Core;
using KeySwitch.Data;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
namespace KeySwitch.App;

internal sealed class SettingsWindow : Window
{
    private readonly Settings settings;
    private readonly IReadOnlyList<LanguageProfile> profiles;
    private readonly Func<Settings, Task> save;
    private readonly TabControl tabs = new();
    private readonly List<Action> readers = [];
    private readonly ObservableCollection<AppRow> appRows;
    private readonly TextBox words = new(), typos = new(), yo = new();
    private readonly CheckBox startup = new() { Content = "Запускать при входе в Windows" };
    private readonly CheckBox administrator = new() { Content = "Запускать от администратора через планировщик (UAC)" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly bool originalElevated;
    private readonly bool originalStartup;
    private readonly ComboBox theme = new() { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
    internal sealed class AppRow
    {
        public string Pattern { get; set; } = "";
        public bool IsFolder { get; set; }
        public ApplicationMode Mode { get; set; } = ApplicationMode.ManualOnly;
    }
    private sealed record ModeChoice(ApplicationMode Value, string Label);
    public SettingsWindow(Settings settings, IReadOnlyList<LanguageProfile> profiles, string dataPath, Func<Settings, Task> save)
    {
        this.settings = settings; this.profiles = profiles; this.save = save;
        originalElevated = settings.ElevatedStartup;
        try { originalStartup = StartupManager.Enabled || originalElevated; } catch { }
        Title = "KeySwitch — настройки"; Width = 830; Height = 710; MinWidth = 700; MinHeight = 570;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        appRows = new(settings.Applications.Select(r => new AppRow { Pattern = r.Pattern, IsFolder = r.IsFolder, Mode = r.Mode }));
        var root = new DockPanel { Margin = new Thickness(28, 20, 28, 20) }; Content = root;
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        heading.Children.Add(new TextBlock { Text = "KeySwitch", FontSize = 28, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Русский и английский. В вашем ритме.", Margin = new Thickness(0, 4, 0, 0), Opacity = 0.7 });
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Закрыть", MinWidth = 100, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) }; cancel.Click += (_, _) => Close();
        var apply = new Button { Content = "Сохранить", MinWidth = 110, IsDefault = true, Background = new SolidColorBrush(Color.FromRgb(0, 103, 192)), Foreground = Brushes.White };
        apply.Click += async (_, _) => { apply.IsEnabled = false; try { await Apply(); } finally { apply.IsEnabled = true; } };
        buttons.Children.Add(apply); buttons.Children.Add(cancel); DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons); footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(tabs);
        General(); Exceptions(); Corrections(); Sounds(); About(dataPath);
        SourceInitialized += (_, _) => { var handle = new WindowInteropHelper(this).Handle; var corner = 2; DwmSetWindowAttribute(handle, 33, ref corner, 4); ApplyTheme(); };
        ApplyTheme();
    }
    public void SelectTab(int index) => tabs.SelectedIndex = index;
    private StackPanel Tab(string title)
    {
        var panel = new StackPanel { Margin = new Thickness(4, 20, 14, 8) };
        tabs.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel } });
        return panel;
    }
    private static void Heading(Panel panel, string text, string? description = null)
    {
        panel.Children.Add(new TextBlock { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 10) });
        if (description is not null) panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 14) });
    }
    private void Check(Panel panel, string text, bool initial, Action<bool> setter)
    {
        var control = new CheckBox { Content = text, IsChecked = initial, Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(control); readers.Add(() => setter(control.IsChecked == true));
    }
    private TextBox Field(Panel panel, string label, string value)
    {
        var box = new TextBox { Text = value, Margin = new Thickness(0, 4, 0, 14), Padding = new Thickness(8, 6, 8, 6) };
        panel.Children.Add(new Label { Content = label, Target = box, Padding = new Thickness(0) }); panel.Children.Add(box); return box;
    }
    private void General()
    {
        var panel = Tab("Общие"); Heading(panel, "Печатайте, как привыкли", "Ошибочное слово исправляется после пробела, Enter или Tab. Pause отменяет последнее исправление или меняет раскладку последнего слова.");
        Check(panel, "Автоматически исправлять раскладку", settings.Enabled, v => settings.Enabled = v);
        Check(panel, "Дополнительная диагностика фокуса (DEBUG)", settings.Debug, v => settings.Debug = v);
        Check(panel, "Исправлять две заглавные в начале: ПРивет → Привет", settings.FixCapitals, v => settings.FixCapitals = v);
        Check(panel, "Переключать язык одиночным тапом Shift", settings.SingleShift, v => settings.SingleShift = v);
        Check(panel, "Раннее исправление при очень высокой уверенности (экспериментально)", settings.EarlySwitch, v => settings.EarlySwitch = v);
        panel.Children.Add(new TextBlock { Text = "Двойной тап Shift за 400 мс отменяет исправление. Любое редактирование завершает возможность отмены.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 20) });
        var selection = Field(panel, "Конвертация выделения", FormatShortcut(settings.SelectionModifiers, settings.SelectionKey));
        var pause = Field(panel, "Приостановка автопереключения", FormatShortcut(settings.PauseModifiers, settings.PauseKey));
        readers.Add(() => { (settings.SelectionModifiers, settings.SelectionKey) = ParseShortcut(selection.Text); (settings.PauseModifiers, settings.PauseKey) = ParseShortcut(pause.Text); });
        panel.Children.Add(new TextBlock { Text = "Тема", Margin = new Thickness(0, 4, 0, 6) });
        theme.ItemsSource = new[] { "Как в Windows", "Светлая", "Тёмная" }; theme.SelectedIndex = settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        theme.SelectionChanged += (_, _) => ApplyTheme(); panel.Children.Add(theme);
        readers.Add(() => settings.Theme = theme.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" });
        startup.IsChecked = originalStartup; startup.Margin = new Thickness(0, 24, 0, 12); administrator.IsChecked = originalElevated; administrator.Margin = new Thickness(0, 0, 0, 16);
        panel.Children.Add(startup); panel.Children.Add(administrator);
        var threshold = Field(panel, "Порог уверенности (0.85–1.00)", settings.ConfidenceThreshold.ToString("0.00", CultureInfo.InvariantCulture));
        readers.Add(() => settings.ConfidenceThreshold = double.Parse(threshold.Text.Replace(',', '.'), CultureInfo.InvariantCulture));
    }
    private void Exceptions()
    {
        var panel = Tab("Исключения"); Heading(panel, "Где работать", "Для каждой программы выберите режим. Правило программы имеет приоритет над автоматическим исключением игр. Добавьте строку в конце таблицы или удалите выбранную строку клавишей Delete.");
        var grid = new DataGrid { ItemsSource = appRows, AutoGenerateColumns = false, CanUserAddRows = true, CanUserDeleteRows = true, MinHeight = 190, MaxHeight = 280, RowHeaderWidth = 20, Margin = new Thickness(0, 0, 0, 18) };
        grid.Columns.Add(new DataGridTextColumn { Header = "Имя exe или путь к папке", Binding = new Binding(nameof(AppRow.Pattern)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Папка", Binding = new Binding(nameof(AppRow.IsFolder)), Width = 65 });
        grid.Columns.Add(new DataGridComboBoxColumn { Header = "Режим", ItemsSource = new[] { new ModeChoice(ApplicationMode.Disabled, "Выключено"), new ModeChoice(ApplicationMode.ManualOnly, "Только вручную"), new ModeChoice(ApplicationMode.Full, "Полностью") }, SelectedValuePath = "Value", DisplayMemberPath = "Label", SelectedValueBinding = new Binding(nameof(AppRow.Mode)), Width = 155 });
        panel.Children.Add(grid); readers.Add(() => { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); settings.Applications = appRows.Where(r => !string.IsNullOrWhiteSpace(r.Pattern)).Select(r => new ApplicationRule(r.Pattern.Trim(), r.IsFolder, r.Mode)).ToList(); });
        var games = Field(panel, "Имена exe игр через запятую", string.Join(", ", settings.GameExecutables));
        readers.Add(() => settings.GameExecutables = games.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList());
        Heading(panel, "Слова", "Одна строка: слово | направление. Направления: en>ru, ru>en или *. Одиночные буквы и 100 самых частых слов защищены от добавления.");
        ConfigureEditor(words, 150); words.Text = string.Join(Environment.NewLine, settings.Words.Select(w => w.Word + " | " + w.Direction)); panel.Children.Add(words);
        readers.Add(() =>
        {
            var list = new List<WordException>();
            foreach (var line in Lines(words.Text))
            {
                var parts = line.Split('|', 2, StringSplitOptions.TrimEntries); var word = parts[0]; var direction = parts.Length > 1 ? parts[1] : "*";
                var error = ExclusionPolicy.ValidateWord(word, profiles); if (error is not null) throw new InvalidDataException(error);
                if (direction is not ("*" or "en>ru" or "ru>en")) throw new InvalidDataException("Направление: en>ru, ru>en или *.");
                list.Add(new(word, direction));
            }
            settings.Words = list;
        });
        panel.Children.Add(new TextBlock { Text = "Обучение: после трёх отмен слово становится исключением. Сохраняются только подтверждённые слова и счётчики.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 14, 0, 0) });
    }
    private void Corrections()
    {
        var panel = Tab("Опечатки"); Heading(panel, "Небольшие исправления", "Заменяются только целые слова. Правила доступны для редактирования здесь и в settings.json.");
        Check(panel, "Исправлять опечатки", settings.Typos, v => settings.Typos = v);
        ConfigureEditor(typos, 170); typos.Text = string.Join(Environment.NewLine, settings.Replacements.Select(p => p.Key + " = " + p.Value)); panel.Children.Add(typos);
        readers.Add(() => settings.Replacements = ParseRules(typos.Text));
        Heading(panel, "Буква ё", "Используйте только однозначные словоформы. «Все/всё» отсутствует в правилах намеренно.");
        Check(panel, "Расставлять ё по словарю", settings.Yoficator, v => settings.Yoficator = v);
        ConfigureEditor(yo, 140); yo.Text = string.Join(Environment.NewLine, settings.YoWords.Select(p => p.Key + " = " + p.Value)); panel.Children.Add(yo);
        readers.Add(() => settings.YoWords = ParseRules(yo.Text));
    }
    private void Sounds()
    {
        var panel = Tab("Звуки"); Heading(panel, "Тихо по умолчанию", "Выберите собственные WAV-файлы. Громкость применяется только к звукам KeySwitch.");
        Check(panel, "Проигрывать звуки", settings.Sounds, v => settings.Sounds = v);
        SoundField(panel, "Переключение раскладки", settings.SwitchSound, v => settings.SwitchSound = v);
        SoundField(panel, "Исправление опечатки", settings.TypoSound, v => settings.TypoSound = v);
        panel.Children.Add(new TextBlock { Text = "Громкость", Margin = new Thickness(0, 10, 0, 8) });
        var volume = new Slider { Minimum = 0, Maximum = 1, Value = settings.Volume, TickFrequency = 0.1, Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(volume); readers.Add(() => settings.Volume = volume.Value);
    }
    private void SoundField(Panel panel, string label, string initial, Action<string> setter)
    {
        var box = Field(panel, label, initial); var button = new Button { Content = "Выбрать WAV…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 20) };
        button.Click += (_, _) => { var dialog = new OpenFileDialog { Filter = "WAV (*.wav)|*.wav", CheckFileExists = true }; if (dialog.ShowDialog(this) == true) box.Text = dialog.FileName; };
        panel.Children.Add(button); readers.Add(() => { var value = box.Text.Trim(); if (value.Length > 0 && (!System.IO.File.Exists(value) || !value.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Выберите существующий WAV-файл."); setter(value); });
    }
    private void About(string path)
    {
        var panel = Tab("О программе"); Heading(panel, $"KeySwitch {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)}", "Локальный переключатель русской и английской раскладки для Windows 11 x64.");
        panel.Children.Add(new TextBlock { Text = "Никакой телеметрии, сетевых запросов и истории набора. Настройки, исключения и счётчики обучения находятся в вашей папке профиля.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
        var open = new Button { Content = "Открыть папку данных", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 24) };
        open.Click += (_, _) => { System.IO.Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }; panel.Children.Add(open);
        Heading(panel, "Границы поддержки", "RDP и виртуальные машины могут перехватывать ввод. Код, транслитерация и сокращения неоднозначны. Умляуты и нестандартные раскладки пока не поддерживаются. Терминалы и редакторы кода по умолчанию работают только вручную.");
        panel.Children.Add(new TextBlock { Text = "Обработка отключена, когда Windows явно помечает поле как пароль. Окно администратора требует такого же уровня прав у KeySwitch. Игры исключаются автоматически. После Enter или Tab отмена недоступна, поскольку сообщение уже могло быть отправлено.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        Heading(panel, "Открытые языковые данные", "Частотные словоформы: Hermit Dave / FrequencyWords 2018 / OpenSubtitles. 3–4-граммы: Universal Dependencies English EWT и Russian GSD r2.15. Данные и производные модели: CC BY-SA 4.0. Атрибуция и полные лицензии поставляются в папке licenses.");
        panel.Children.Add(new TextBlock { Text = "Точность и задержку проверяйте на своих приложениях. Результаты воспроизводимого стенда и ручной чек-лист находятся в README проекта.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
    }
    private async Task Apply()
    {
        try
        {
            foreach (var read in readers) read(); settings.Validate();
            var wantsStartup = startup.IsChecked == true; var wantsElevated = wantsStartup && administrator.IsChecked == true;
            if (wantsElevated != originalElevated) await StartupManager.SetElevatedAsync(wantsElevated);
            if (!wantsElevated && (wantsStartup != originalStartup || originalElevated)) StartupManager.SetNormal(wantsStartup);
            settings.ElevatedStartup = wantsElevated; await save(settings); Close();
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException or IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { status.Text = ex is System.ComponentModel.Win32Exception ? "Изменение автозапуска отменено." : ex.Message; }
    }
    public void ApplyTheme()
    {
        var dark = theme.SelectedIndex == 2;
        if (theme.SelectedIndex <= 0)
        { try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); dark = key?.GetValue("AppsUseLightTheme") is 0; } catch { dark = false; } }
        var background = new SolidColorBrush(dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(243, 243, 243));
        var surface = new SolidColorBrush(dark ? Color.FromRgb(44, 44, 44) : Colors.White);
        var foreground = new SolidColorBrush(dark ? Color.FromRgb(245, 245, 245) : Color.FromRgb(25, 25, 25));
        var border = new SolidColorBrush(dark ? Color.FromRgb(80, 80, 80) : Color.FromRgb(205, 205, 205));
        Background = background; Foreground = foreground;
        foreach (var type in new[] { typeof(TabControl), typeof(TabItem), typeof(ScrollViewer), typeof(DataGrid), typeof(DataGridRow), typeof(DataGridCell), typeof(System.Windows.Controls.Primitives.DataGridColumnHeader), typeof(TextBox), typeof(ComboBox), typeof(ComboBoxItem), typeof(Button), typeof(CheckBox), typeof(Label) })
        {
            var style = new Style(type); style.Setters.Add(new Setter(Control.ForegroundProperty, foreground));
            if (type != typeof(CheckBox) && type != typeof(Label)) style.Setters.Add(new Setter(Control.BackgroundProperty, surface));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, border));
            if (type == typeof(Button)) style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 7, 14, 7)));
            if (type == typeof(TabItem)) style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 8, 12, 8)));
            if (type == typeof(TabItem)) style.Setters.Add(new Setter(Control.TemplateProperty, ParseTemplate("""
                <ControlTemplate TargetType="TabItem" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <Border x:Name="Chrome" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="0,0,0,2" Padding="{TemplateBinding Padding}" Margin="0,0,2,0">
                    <ContentPresenter ContentSource="Header" RecognizesAccessKey="True" HorizontalAlignment="Center" />
                  </Border>
                  <ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter TargetName="Chrome" Property="BorderBrush" Value="#0067C0"/><Setter Property="FontWeight" Value="SemiBold"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Chrome" Property="BorderThickness" Value="1"/></Trigger></ControlTemplate.Triggers>
                </ControlTemplate>
                """)));
            if (type == typeof(Button)) style.Setters.Add(new Setter(Control.TemplateProperty, ParseTemplate("""
                <ControlTemplate TargetType="Button" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <Border x:Name="Chrome" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="4" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" RecognizesAccessKey="True" /></Border>
                  <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Chrome" Property="Opacity" Value="0.8"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Chrome" Property="BorderBrush" Value="#0067C0"/><Setter TargetName="Chrome" Property="BorderThickness" Value="2"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="Chrome" Property="Opacity" Value="0.5"/></Trigger></ControlTemplate.Triggers>
                </ControlTemplate>
                """)));
            if (type == typeof(ComboBox)) style.Setters.Add(new Setter(Control.TemplateProperty, ParseTemplate("""
                <ControlTemplate TargetType="ComboBox" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <Grid><ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}" Background="{TemplateBinding Background}" Foreground="{TemplateBinding Foreground}" BorderBrush="{TemplateBinding BorderBrush}">
                    <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="4" Padding="8,6"><ContentPresenter/></Border></ControlTemplate></ToggleButton.Template>
                    <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="22"/></Grid.ColumnDefinitions><ContentPresenter Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"/><TextBlock Grid.Column="1" Text="⌄" HorizontalAlignment="Right"/></Grid>
                  </ToggleButton><Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False"><Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" MinWidth="{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}"><ScrollViewer CanContentScroll="True"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer></Border></Popup></Grid>
                </ControlTemplate>
                """)));
            Resources[type] = style;
        }
        Resources[SystemColors.WindowBrushKey] = surface; Resources[SystemColors.WindowTextBrushKey] = foreground; Resources[SystemColors.ControlBrushKey] = surface; Resources[SystemColors.ControlTextBrushKey] = foreground;
        var handle = new WindowInteropHelper(this).Handle; if (handle != 0) { var value = dark ? 1 : 0; DwmSetWindowAttribute(handle, 20, ref value, 4); }
    }
    private static ControlTemplate ParseTemplate(string xaml) => (ControlTemplate)XamlReader.Parse(xaml);
    private static void ConfigureEditor(TextBox box, double height)
    { box.AcceptsReturn = true; box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; box.MinHeight = height; box.MaxHeight = height + 80; box.Padding = new Thickness(8); box.FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"); }
    private static IEnumerable<string> Lines(string input) => input.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(s => !s.StartsWith('#'));
    private static Dictionary<string, string> ParseRules(string input)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in Lines(input)) { var parts = line.Split('=', 2, StringSplitOptions.TrimEntries); if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Правило: исходное слово = исправление."); result[parts[0]] = parts[1]; }
        return result;
    }
    private static string FormatShortcut(int modifiers, int vk) => (modifiers switch { 3 => "Ctrl+Alt+", 10 => "Ctrl+Win+", _ => "Ctrl+Shift+" }) + KeyInterop.KeyFromVirtualKey(vk);
    private static (int Modifiers, int Key) ParseShortcut(string input)
    {
        var parts = input.Split('+', StringSplitOptions.TrimEntries); if (parts.Length != 3 || !parts[0].Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Сочетание: Ctrl+Shift+Space, Ctrl+Alt+F12 или Ctrl+Win+F12.");
        var mods = parts[1].ToLowerInvariant() switch { "shift" => 6, "alt" => 3, "win" => 10, _ => 0 };
        if (mods == 0 || !Enum.TryParse<Key>(parts[2], true, out var key)) throw new InvalidDataException("Неизвестная горячая клавиша.");
        return (mods, KeyInterop.VirtualKeyFromKey(key));
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
