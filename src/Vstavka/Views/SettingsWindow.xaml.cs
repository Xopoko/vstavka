using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Vstavka.Core;
using Vstavka.Core.Localization;
using Vstavka.Presentation;
using System.Windows.Markup;

namespace Vstavka.Views;

public partial class SettingsWindow : Window
{
    public AppSettings? Result { get; private set; }
    public event Func<bool>? ClearHistoryRequested;
    public event Action<double>? PreviewCopySoundRequested;
    public event Action<double>? PreviewPasteSoundRequested;
    public event Action<string, string>? AppearancePreviewRequested;
    private bool _initializing = true;

    public SettingsWindow(AppSettings settings)
    {
        ThemeManager.EnsureInitialized();
        InitializeComponent();
        FlowDirection = Strings.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Language = XmlLanguage.GetLanguage(Strings.Culture.IetfLanguageTag);
        SourceInitialized += (_, _) => ThemeManager.UpdateWindowChrome(this);
        CopySoundCheck.IsChecked = settings.SoundEnabled;
        PasteSoundCheck.IsChecked = settings.PasteSoundEnabled;
        StartupCheck.IsChecked = settings.StartAtLogin;
        VolumeSlider.Value = Math.Clamp(settings.Volume, 0, 1);
        VolumeLabel.Text = VolumeSlider.Value.ToString("P0", Strings.Culture);
        LanguageCombo.ItemsSource = Strings.AvailableLanguages;
        LanguageCombo.SelectedValue = settings.Language;
        if (LanguageCombo.SelectedIndex < 0) LanguageCombo.SelectedValue = "en";
        SelectOption(ThemeCombo, settings.Theme, "system");
        SelectOption(AccentCombo, settings.Accent, "windows");
        MaxItemsBox.Text = settings.MaxItems.ToString(CultureInfo.InvariantCulture);
        ExcludedAppsBox.Text = string.Join(Environment.NewLine, settings.ExcludedApps ?? Array.Empty<string>());
        HotkeyCombo.SelectedItem = HotkeyCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(x => string.Equals(x.Content as string, settings.Hotkey, StringComparison.OrdinalIgnoreCase)) ?? HotkeyCombo.Items[0];
        RetentionCombo.SelectedItem = RetentionCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == settings.RetentionDays.ToString(CultureInfo.InvariantCulture));
        if (RetentionCombo.SelectedItem == null)
        {
            var option = new ComboBoxItem { Content = Strings.Format("DaysCount", settings.RetentionDays), Tag = settings.RetentionDays.ToString(CultureInfo.InvariantCulture) };
            RetentionCombo.Items.Add(option); RetentionCombo.SelectedItem = option;
        }
        MaxHeight = SystemParameters.WorkArea.Height - 26;
        _initializing = false;
    }

    private string SelectedTheme => (ThemeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "system";
    private string SelectedAccent => (AccentCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "windows";

    private static void SelectOption(ComboBox combo, string value, string fallback) => combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(x => string.Equals(x.Tag as string, value, StringComparison.OrdinalIgnoreCase)) ?? combo.Items.OfType<ComboBoxItem>().First(x => (string)x.Tag == fallback);
    private void Appearance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing) AppearancePreviewRequested?.Invoke(SelectedTheme, SelectedAccent);
    }

    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeLabel != null) VolumeLabel.Text = e.NewValue.ToString("P0", Strings.Culture);
    }
    private void PreviewCopy_Click(object sender, RoutedEventArgs e) => PreviewCopySoundRequested?.Invoke(VolumeSlider.Value);
    private void PreviewPaste_Click(object sender, RoutedEventArgs e) => PreviewPasteSoundRequested?.Invoke(VolumeSlider.Value);
    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        ClearConfirmation.Visibility = Visibility.Visible;
        ClearStatus.Visibility = Visibility.Collapsed;
        CancelClearButton.Focus();
        ClearConfirmation.BringIntoView();
    }
    private void CancelClear_Click(object sender, RoutedEventArgs e) => CloseClearConfirmation();

    private void CloseClearConfirmation()
    {
        ClearHistoryButton.Focus();
        ClearConfirmation.Visibility = Visibility.Collapsed;
    }

    private void ConfirmClear_Click(object sender, RoutedEventArgs e)
    {
        bool cleared = ClearHistoryRequested?.Invoke() == true;
        CloseClearConfirmation();
        ClearStatus.Text = Strings.Get(cleared ? "HistoryCleared" : "SaveFailed");
        ClearStatus.SetResourceReference(TextBlock.ForegroundProperty, cleared ? "SuccessBrush" : "DangerBrush");
        ClearStatus.Visibility = Visibility.Visible;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MaxItemsBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int maxItems) ||
            maxItems < 1 || maxItems > HistoryStore.MaxItemCount)
        {
            ErrorLabel.Text = Strings.Get("InvalidMaxItems"); MaxItemsBox.Focus(); MaxItemsBox.SelectAll(); return;
        }
        Result = new AppSettings
        {
            SoundEnabled = CopySoundCheck.IsChecked == true,
            PasteSoundEnabled = PasteSoundCheck.IsChecked == true,
            StartAtLogin = StartupCheck.IsChecked == true,
            Volume = VolumeSlider.Value,
            MaxItems = maxItems,
            Hotkey = AppSettings.NormalizeHotkey((HotkeyCombo.SelectedItem as ComboBoxItem)?.Content as string),
            RetentionDays = int.Parse((string)((ComboBoxItem)RetentionCombo.SelectedItem).Tag, CultureInfo.InvariantCulture),
            ExcludedApps = ExcludedAppsBox.Text.Split(['\r', '\n', ';', ','], StringSplitOptions.RemoveEmptyEntries),
            Language = LanguageCombo.SelectedValue as string ?? "en",
            Theme = SelectedTheme,
            Accent = SelectedAccent
        };
        DialogResult = true;
    }
}
