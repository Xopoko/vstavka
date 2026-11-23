using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Vstavka.Core.Localization;
using Vstavka.Platform;

namespace Vstavka.Desktop;

internal sealed partial class SettingsWindow
{
    private Control SoundSection(IClipboardSounds sounds)
    {
        var copy = SoundRow("CopySound", "PlayCopySound", _draft.SoundEnabled,
            value => _draft.SoundEnabled = value, () => sounds.Play(paste: false, _draft.Volume));
        var paste = SoundRow("PasteSound", "PlayPasteSound", _draft.PasteSoundEnabled,
            value => _draft.PasteSoundEnabled = value, () => sounds.Play(paste: true, _draft.Volume));
        var volume = new Slider
        {
            Name = "VolumeSetting", Minimum = 0, Maximum = 1, Value = _draft.Volume,
            SmallChange = 0.05, LargeChange = 0.1, VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(volume, Strings.Get("Volume"));
        var percent = SetupVisuals.Text(FormatVolume(_draft.Volume), 12, "MutedBrush");
        percent.VerticalAlignment = VerticalAlignment.Center;
        volume.ValueChanged += (_, e) =>
        {
            _draft.Volume = e.NewValue;
            percent.Text = FormatVolume(e.NewValue);
        };
        var volumeControl = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        SetupVisuals.Add(volumeControl, volume);
        SetupVisuals.Add(volumeControl, percent, 1);
        return Section("Sound", Card(copy, paste, Field("Volume", volumeControl)));
    }

    private static Grid SoundRow(string label, string previewName, bool enabled, Action<bool> changed, Action preview)
    {
        var toggle = new CheckBox { Name = label + "Setting", IsChecked = enabled, Content = SetupVisuals.Text(Strings.Get(label)) };
        AutomationProperties.SetName(toggle, Strings.Get(label));
        toggle.IsCheckedChanged += (_, _) => changed(toggle.IsChecked == true);
        var button = SetupVisuals.Button("PreviewSound", preview, name: previewName);
        AutomationProperties.SetName(button, Strings.Get(previewName));
        var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12 };
        SetupVisuals.Add(row, toggle);
        SetupVisuals.Add(row, button, 1);
        return row;
    }

    private static string FormatVolume(double value) => value.ToString("P0", CultureInfo.CurrentCulture);
}
