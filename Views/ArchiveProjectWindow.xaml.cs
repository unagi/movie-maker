using System.Globalization;
using System.IO;
using System.Windows;
using MovieMaker.Models;
using MovieMaker.Services;

namespace MovieMaker.Views;

public partial class ArchiveProjectWindow : Window
{
    private ArchiveProject _project;
    private readonly VideoOrientation? _imageOrientation;
    private string? _layoutJson;
    private bool _initialized;
    public ArchiveProject Project => _project;

    public ArchiveProjectWindow(ArchiveProject project, AppSettings currentDefaults,
        VideoOrientation? imageOrientation, string titleCandidate)
    {
        _project = ArchiveProjectService.Clone(project);
        _imageOrientation = imageOrientation;
        _layoutJson = project.Settings.TextOverlayLayoutJson ?? currentDefaults.TextOverlayLayoutJson;
        InitializeComponent();
        TitleTextBox.Text = project.Title ?? titleCandidate;
        ImageModeRadio.IsChecked = project.UseDraftMode != true;
        DraftModeRadio.IsChecked = project.UseDraftMode == true;
        LufsTextBox.Text = Format(project.Settings.NormalizationTargetIntegratedLufs ?? currentDefaults.NormalizationTargetIntegratedLufs);
        TruePeakTextBox.Text = Format(project.Settings.NormalizationTargetTruePeakDbtp ?? currentDefaults.NormalizationTargetTruePeakDbtp);
        ShortsMaximumTextBox.Text = (project.Settings.ShortsMaximumSeconds ?? currentDefaults.ShortsMaximumSeconds).ToString(CultureInfo.InvariantCulture);
        OneMinuteOffsetTextBox.Text = Format(project.Settings.OneMinuteShortsOffsetSeconds ?? currentDefaults.OneMinuteShortsOffsetSeconds);
        ThreeMinuteOffsetTextBox.Text = Format(project.Settings.ThreeMinuteShortsOffsetSeconds ?? currentDefaults.ThreeMinuteShortsOffsetSeconds);
        OverlayEnabledCheckBox.IsChecked = project.Settings.NormalTextOverlayEnabled ?? currentDefaults.NormalTextOverlayEnabled;
        DraftHighRadio.IsChecked = project.DraftAudioQuality != DraftAudioQuality.Low;
        DraftLowRadio.IsChecked = project.DraftAudioQuality == DraftAudioQuality.Low;
        ConfirmUnknownTracksCheckBox.Visibility = project.Tracks.Any(IsUnknownTrack) ? Visibility.Visible : Visibility.Collapsed;
        _initialized = true;
        UpdateModePanels();
    }

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static bool IsUnknownTrack(ArchiveTrack track) => track.IsNormalizationOverrideEnabled == null ||
        track.IsNormalizationOverrideEnabled == true && (track.TargetIntegratedLufs == null || track.TargetTruePeakDbtp == null);
    private bool IsStandard => DraftModeRadio.IsChecked != true && _imageOrientation == VideoOrientation.Horizontal;

    private void Mode_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_initialized) UpdateModePanels();
    }

    private void UpdateModePanels()
    {
        var draft = DraftModeRadio.IsChecked == true;
        ImageSettingsPanel.Visibility = IsStandard ? Visibility.Visible : Visibility.Collapsed;
        DraftSettingsPanel.Visibility = draft ? Visibility.Visible : Visibility.Collapsed;
        ShortsSettingsPanel.Visibility = draft ? Visibility.Collapsed : Visibility.Visible;
        OrientationText.Text = draft ? "横画像を自動生成します。" : _imageOrientation switch
        {
            VideoOrientation.Vertical => "縦画像：Shorts（音声1曲）",
            VideoOrientation.Horizontal => "横画像：通常モード",
            _ => "背景画像を確認できません。画像モードでは復元できません。"
        };
        EditLayoutButton.IsEnabled = IsStandard && OverlayEnabledCheckBox.IsChecked == true;
    }

    private void EditLayout_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new TextOverlayJsonWindow(string.IsNullOrWhiteSpace(_layoutJson) ? TextOverlayJsonWindow.ExampleJson : _layoutJson) { Owner = this };
        if (dialog.ShowDialog() == true) _layoutJson = dialog.JsonText;
    }

    private static double Parse(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
        double.IsFinite(value) ? value : throw new InvalidDataException("数値を入力してください。");

    private void Confirm_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var project = ArchiveProjectService.Clone(_project);
            var title = TitleTextBox.Text.Trim();
            if (title.Length == 0 || title.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("使用できるタイトルを入力してください。");
            var draft = DraftModeRadio.IsChecked == true;
            if (!draft && _imageOrientation == null) throw new InvalidDataException("背景画像を確認できません。");
            project.Title = title;
            project.UseDraftMode = draft;
            project.Orientation = draft ? VideoOrientation.Horizontal : _imageOrientation;
            project.Profile = draft ? EncodeProfile.DraftPreview : IsStandard ? EncodeProfile.Standard : EncodeProfile.CopyrightCheckProduction;
            if (draft) project.DraftAudioQuality = DraftLowRadio.IsChecked == true ? DraftAudioQuality.Low : DraftAudioQuality.High;
            else
            {
                if (!int.TryParse(ShortsMaximumTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maximum))
                    throw new InvalidDataException("Shorts上限には整数を入力してください。");
                project.Settings.ShortsMaximumSeconds = maximum;
                project.Settings.OneMinuteShortsOffsetSeconds = Parse(OneMinuteOffsetTextBox.Text);
                project.Settings.ThreeMinuteShortsOffsetSeconds = Parse(ThreeMinuteOffsetTextBox.Text);
                if (IsStandard)
                {
                    if (project.Tracks.Any(IsUnknownTrack) && ConfirmUnknownTracksCheckBox.IsChecked != true)
                        throw new InvalidDataException("個別設定の記録がない音声の目標を確認してください。");
                    project.Settings.NormalizationTargetIntegratedLufs = Parse(LufsTextBox.Text);
                    project.Settings.NormalizationTargetTruePeakDbtp = Parse(TruePeakTextBox.Text);
                    project.Settings.NormalTextOverlayEnabled = OverlayEnabledCheckBox.IsChecked == true;
                    if (project.Settings.NormalTextOverlayEnabled == true) project.Settings.TextOverlayLayoutJson = _layoutJson;
                    foreach (var track in project.Tracks.Where(IsUnknownTrack))
                    {
                        track.IsNormalizationOverrideEnabled = false;
                        track.TargetIntegratedLufs = null;
                        track.TargetTruePeakDbtp = null;
                    }
                }
            }
            var missing = ArchiveProjectService.GetMissingFields(project);
            if (missing.Count > 0) throw new InvalidDataException(string.Join("、", missing));
            _project = project;
            DialogResult = true;
        }
        catch (Exception ex) { ValidationText.Text = ex.Message; }
    }
}
