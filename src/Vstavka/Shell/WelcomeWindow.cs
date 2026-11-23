using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vstavka.Core.Localization;
using Vstavka.Presentation;

namespace Vstavka.Shell;

internal sealed class WelcomeWindow : Window
{
    private readonly CheckBox _startup;
    internal bool StartAtLogin => _startup.IsChecked == true;

    internal WelcomeWindow(string hotkey)
    {
        ThemeManager.EnsureInitialized();
        Width = 620;
        MinWidth = 420;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Vstavka;component/Presentation/Theme.xaml", UriKind.Relative)
        });
        SetResourceReference(BackgroundProperty, "WindowBrush");
        Bind(this, TitleProperty, "AppName");

        var layout = new StackPanel { Margin = new Thickness(30) };
        var header = new Grid { Margin = new Thickness(0, 0, 0, 26) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        layout.Children.Add(header);

        var iconUri = new Uri("pack://application:,,,/Vstavka;component/Assets/Vstavka.ico");
        var icons = new IconBitmapDecoder(iconUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Icon = icons.Frames.OrderBy(frame => Math.Abs(frame.PixelWidth - 40)).First();
        var identity = new Grid { Margin = new Thickness(0, 0, 12, 0) };
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        identity.ColumnDefinitions.Add(new ColumnDefinition());
        identity.Children.Add(new Image { Source = Icon, Width = 40, Height = 40 });
        var name = Label("AppName", 14);
        name.FontWeight = FontWeights.SemiBold;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(name, 1);
        identity.Children.Add(name);
        header.Children.Add(identity);

        var language = new ComboBox
        {
            ItemsSource = Strings.AvailableLanguages,
            DisplayMemberPath = "Name",
            SelectedValuePath = "Code",
            SelectedValue = Strings.Language,
            FlowDirection = FlowDirection.LeftToRight,
            VerticalAlignment = VerticalAlignment.Center
        };
        Bind(language, AutomationProperties.NameProperty, "SelectLanguage");
        Grid.SetColumn(language, 1);
        header.Children.Add(language);

        var title = Label("WelcomeTitle", 28);
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(0, 0, 0, 16);
        layout.Children.Add(title);
        var summary = new TextBlock
        {
            FontSize = 18, LineHeight = 25, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 22)
        };
        layout.Children.Add(summary);
        var privacy = Label("WelcomePrivacy", 14);
        privacy.LineHeight = 23;
        privacy.Margin = new Thickness(0, 0, 0, 16);
        layout.Children.Add(privacy);
        var exclusions = Label("WelcomeExclusions", 14);
        exclusions.LineHeight = 21;
        exclusions.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        layout.Children.Add(exclusions);

        _startup = new CheckBox
        {
            IsChecked = true, Style = (Style)FindResource("OptionCheckBox"), Margin = new Thickness(0, 20, 0, 18)
        };
        Bind(_startup, ContentControl.ContentProperty, "StartWithWindows");
        layout.Children.Add(_startup);
        var button = new Button
        {
            Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(20, 13, 20, 13)
        };
        Bind(button, ContentControl.ContentProperty, "EnableHistory");
        button.Click += (_, _) => DialogResult = true;
        layout.Children.Add(button);

        var scroll = new ScrollViewer
        {
            Content = layout,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var surface = new Border { Child = scroll };
        surface.SetResourceReference(Border.BackgroundProperty, "WindowBrush");
        Content = surface;

        void UpdateLanguage()
        {
            summary.Text = Strings.Format("WelcomeDescription", hotkey);
            FlowDirection = Strings.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            Language = XmlLanguage.GetLanguage(Strings.Culture.IetfLanguageTag);
        }

        language.SelectionChanged += (_, _) =>
        {
            Strings.SetLanguage(language.SelectedValue as string);
            UpdateLanguage();
        };
        UpdateLanguage();
        SourceInitialized += (_, _) => ThemeManager.UpdateWindowChrome(this);
    }

    private static TextBlock Label(string key, double size)
    {
        var text = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap };
        Bind(text, TextBlock.TextProperty, key);
        return text;
    }

    private static void Bind(DependencyObject target, DependencyProperty property, string key) =>
        BindingOperations.SetBinding(target, property, new Binding($"[{key}]") { Source = Strings.Current, Mode = BindingMode.OneWay });
}
