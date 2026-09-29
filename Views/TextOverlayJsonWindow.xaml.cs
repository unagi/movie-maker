using System.Windows;
using System.Windows.Media;
using MovieMaker.Services;
using WpfBrushes = System.Windows.Media.Brushes;

namespace MovieMaker.Views;

public partial class TextOverlayJsonWindow : Window
{
    public const string ExampleJson = """
        {
          "schemaVersion": 1,
          "canvas": { "width": 1920, "height": 1080 },
          "zones": [
            {
              "id": "title-left",
              "bounds": [72, 120, 210, 830],
              "writingMode": "vertical-rl",
              "horizontalAlign": "center",
              "verticalAlign": "center",
              "font": {
                "id": "source-han-sans-jp-heavy",
                "weight": 900,
                "sizePx": 104,
                "minSizePx": 68,
                "letterSpacingPx": 2
              },
              "paint": { "fill": "#FFFFFFFF" },
              "fit": { "maxLines": 3, "overflow": "reject" }
            }
          ]
        }
        """;

    public string JsonText => JsonTextBox.Text;

    public TextOverlayJsonWindow(string json)
    {
        InitializeComponent();
        JsonTextBox.Text = string.IsNullOrWhiteSpace(json) ? ExampleJson : json;
        JsonTextBox.CaretIndex = JsonTextBox.Text.Length;
    }

    private void JsonTextBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var valid = TextOverlayService.TryParse(JsonTextBox.Text, out _, out var error);
        ValidationText.Text = valid ? "JSONの形式は有効です。" : error;
        ValidationText.Foreground = valid ? WpfBrushes.SeaGreen : WpfBrushes.Firebrick;
        ApplyButton.IsEnabled = valid;
    }

    private void ApplyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (TextOverlayService.TryParse(JsonTextBox.Text, out _, out var error))
        {
            DialogResult = true;
            Close();
            return;
        }

        System.Windows.MessageBox.Show(this, error ?? "JSONを確認してください。", "JSON設定エラー",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
