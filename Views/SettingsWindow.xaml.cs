using System;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using MovieMaker.Services;
using MovieMaker.ViewModels;

namespace MovieMaker.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow()
    {
        InitializeComponent();
        _viewModel = new SettingsViewModel(SettingsService.Current);
        DataContext = _viewModel;
    }

    private void BrowseOutput_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = PickFolder(_viewModel.OutputDirectory);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            _viewModel.OutputDirectory = selected;
        }
    }

    private void BrowseArchive_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = PickFolder(_viewModel.ArchiveDirectory);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            _viewModel.ArchiveDirectory = selected;
        }
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        var settings = _viewModel.ToSettings();
        if (string.IsNullOrWhiteSpace(settings.OutputDirectory) ||
            string.IsNullOrWhiteSpace(settings.ArchiveDirectory))
        {
            System.Windows.MessageBox.Show(
                "出力先とアーカイブの両方を設定してください。",
                "設定エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SettingsService.Save(settings);
        this.DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        this.DialogResult = false;
        Close();
    }

    private static string? PickFolder(string? initialPath)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "フォルダを選択してください",
            UseDescriptionForTitle = true
        };

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            dialog.SelectedPath = initialPath;
        }

        var result = dialog.ShowDialog();
        return result == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }
}
