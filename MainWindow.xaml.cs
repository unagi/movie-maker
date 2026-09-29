using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using MovieMaker.Models;
using MovieMaker.ViewModels;
using MovieMaker.Views;

namespace MovieMaker;

public partial class MainWindow : Window
{
    private const string AudioTrackDataFormat = "MovieMaker.AudioTrack";
    private Point _audioDragStart;
    private AudioTrackItem? _draggedAudioTrack;
    private AudioTrackItem? _audioDropTarget;
    private bool _audioDropAfter;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void EditTrackNormalization_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: AudioTrackItem track } button ||
            DataContext is not MainViewModel { IsEncoding: false })
        {
            return;
        }

        new TrackNormalizationWindow(track) { Owner = this }.ShowDialog();
        button.Focus();
    }

    private void AudioReviewDetails_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: AudioTrackItem track } button ||
            string.IsNullOrWhiteSpace(track.AudioReviewDetailText))
        {
            return;
        }

        System.Windows.MessageBox.Show(this, track.AudioReviewDetailText, "入力音声の確認", MessageBoxButton.OK, MessageBoxImage.Information);
        button.Focus();
    }

    private void DropArea_OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && DataContext is MainViewModel { IsEncoding: false } vm)
        {
            e.Effects = DragDropEffects.Copy;
            vm.SetFileDragOver(true);
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

        if (DataContext is MainViewModel vm)
        {
            vm.SetFileDragOver(false);
        }

        e.Handled = true;
    }

    private void DropArea_OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel dragViewModel)
        {
            dragViewModel.SetFileDragOver(false);
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (DataContext is MainViewModel vm)
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            vm.HandleDrop(files);
        }

        e.Handled = true;
    }

    private void AudioTrackDragHandle_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel { IsEncoding: false } ||
            sender is not FrameworkElement { DataContext: AudioTrackItem track })
        {
            return;
        }

        _audioDragStart = e.GetPosition(this);
        _draggedAudioTrack = track;
        e.Handled = true;
    }

    private void AudioTrackDragHandle_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedAudioTrack == null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _audioDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _audioDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject(AudioTrackDataFormat, _draggedAudioTrack);
        System.Windows.DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        ClearAudioDropIndicators();
        _draggedAudioTrack = null;
        e.Handled = true;
    }

    private void AudioTrackList_OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(AudioTrackDataFormat) ||
            DataContext is not MainViewModel { IsEncoding: false } viewModel)
        {
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        var container = FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        var target = container?.DataContext as AudioTrackItem;
        var insertAfter = false;
        if (container != null && target != null)
        {
            insertAfter = e.GetPosition(container).Y > container.ActualHeight / 2;
        }
        else if (viewModel.AudioTracks.Count > 0)
        {
            target = viewModel.AudioTracks[^1];
            insertAfter = true;
        }

        SetAudioDropIndicator(viewModel, target, insertAfter);
    }

    private void AudioTrackList_OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(AudioTrackDataFormat) ||
            e.Data.GetData(AudioTrackDataFormat) is not AudioTrackItem source ||
            DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var oldIndex = viewModel.AudioTracks.IndexOf(source);
        if (oldIndex >= 0 && _audioDropTarget != null)
        {
            var targetIndex = viewModel.AudioTracks.IndexOf(_audioDropTarget);
            var newIndex = targetIndex + (_audioDropAfter ? 1 : 0);
            if (oldIndex < newIndex)
            {
                newIndex--;
            }

            newIndex = Math.Clamp(newIndex, 0, viewModel.AudioTracks.Count - 1);
            viewModel.MoveAudioTrack(oldIndex, newIndex);
        }

        ClearAudioDropIndicators();
        _draggedAudioTrack = null;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void AudioTrackList_OnDragLeave(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(AudioTrackDataFormat))
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

        ClearAudioDropIndicators();
        e.Handled = true;
    }

    private void SetAudioDropIndicator(MainViewModel viewModel, AudioTrackItem? target, bool insertAfter)
    {
        foreach (var track in viewModel.AudioTracks)
        {
            track.ClearDropIndicator();
        }

        _audioDropTarget = target;
        _audioDropAfter = insertAfter;
        if (target != null)
        {
            target.IsDropTargetBefore = !insertAfter;
            target.IsDropTargetAfter = insertAfter;
        }
    }

    private void ClearAudioDropIndicators()
    {
        if (DataContext is MainViewModel viewModel)
        {
            foreach (var track in viewModel.AudioTracks)
            {
                track.ClearDropIndicator();
            }
        }

        _audioDropTarget = null;
        _audioDropAfter = false;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsClickOnInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            if (WindowState == WindowState.Maximized)
            {
                SystemCommands.RestoreWindow(this);
            }
            else
            {
                SystemCommands.MaximizeWindow(this);
            }
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

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        SystemCommands.MinimizeWindow(this);
    }

    private void MaximizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        SystemCommands.MaximizeWindow(this);
    }

    private void RestoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        SystemCommands.RestoreWindow(this);
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        SystemCommands.CloseWindow(this);
    }
}
