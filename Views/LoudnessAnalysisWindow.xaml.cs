using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using MovieMaker.ViewModels;

namespace MovieMaker.Views;

public partial class LoudnessAnalysisWindow : Window
{
    private readonly LoudnessAnalysisViewModel _viewModel;

    public LoudnessAnalysisWindow()
    {
        InitializeComponent();
        _viewModel = new LoudnessAnalysisViewModel();
        DataContext = _viewModel;
    }

    private void DropArea_OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            _viewModel.SetFileDragOver(true);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void DropArea_OnDragOver(object sender, DragEventArgs e)
    {
        DropArea_OnDragEnter(sender, e);
    }

    private void DropArea_OnDragLeave(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (sender is FrameworkElement element)
        {
            var position = e.GetPosition(element);
            if (position.X >= 0 && position.X <= element.ActualWidth &&
                position.Y >= 0 && position.Y <= element.ActualHeight)
            {
                return;
            }
        }

        _viewModel.SetFileDragOver(false);
        e.Handled = true;
    }

    private async void DropArea_OnDrop(object sender, DragEventArgs e)
    {
        _viewModel.SetFileDragOver(false);
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            await _viewModel.AddFilesAsync(files);
        }

        e.Handled = true;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsClickOnInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private static bool IsClickOnInteractiveElement(DependencyObject? source)
    {
        var current = source;
        while (current != null)
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
