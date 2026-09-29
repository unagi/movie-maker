using System.Globalization;
using System.Windows;
using MovieMaker.Models;
using MovieMaker.Services;

namespace MovieMaker.Views;

public partial class TrackNormalizationWindow : Window
{
    private readonly AudioTrackItem _track;

    public TrackNormalizationWindow(AudioTrackItem track)
    {
        InitializeComponent();
        _track = track;
        TrackNameText.Text = track.FileName;
        CommonTargetRadio.Content = $"共通目標を使う（{SettingsService.Current.NormalizationTargetIntegratedLufs:0.###} LUFS / " +
                                    $"{SettingsService.Current.NormalizationTargetTruePeakDbtp:0.###} dBTP）";
        CommonTargetRadio.IsChecked = !track.IsNormalizationOverrideEnabled;
        IndividualTargetRadio.IsChecked = track.IsNormalizationOverrideEnabled;
        LufsTextBox.Text = track.NormalizationTargetLufsText;
        TruePeakTextBox.Text = track.NormalizationTargetTruePeakText;
        Loaded += (_, _) =>
        {
            if (IndividualTargetRadio.IsChecked == true)
                IndividualTargetRadio.Focus();
            else
                CommonTargetRadio.Focus();
        };
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (IndividualTargetRadio.IsChecked != true)
        {
            _track.IsNormalizationOverrideEnabled = false;
            _track.UpdateDefaultNormalizationTargets(
                SettingsService.Current.NormalizationTargetIntegratedLufs,
                SettingsService.Current.NormalizationTargetTruePeakDbtp);
            DialogResult = true;
            return;
        }

        if (!TryParseTarget(LufsTextBox.Text, -70, -5, out var lufs) ||
            !TryParseTarget(TruePeakTextBox.Text, -8, 0, out var truePeak))
        {
            ValidationText.Text = "LUFSは-70～-5、dBTPは-8～0の数値を入力してください。";
            return;
        }

        _track.NormalizationTargetLufsText = lufs.ToString("0.###", CultureInfo.InvariantCulture);
        _track.NormalizationTargetTruePeakText = truePeak.ToString("0.###", CultureInfo.InvariantCulture);
        _track.IsNormalizationOverrideEnabled = true;
        DialogResult = true;
    }

    private static bool TryParseTarget(string text, double min, double max, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               double.IsFinite(value) && value >= min && value <= max;
    }
}
