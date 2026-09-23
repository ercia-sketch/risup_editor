using System.Windows;
using System.Windows.Controls;

namespace RisupEditor;
public sealed partial class MainWindow
{
    readonly CheckBox additionalCheckBox = new() { Content = "추가 문법 검사", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 14, 0) };
    UIElement CreateFooter()
    {
        var footer = new DockPanel(); var tools = new StackPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(Button("정보", About, compact: true));
        tools.Children.Add(Button("화면 기본값 복원", () => { leftColumn.Width = new GridLength(4, GridUnitType.Star); workColumn.Width = new GridLength(4, GridUnitType.Star); toggleColumn.Width = new GridLength(2, GridUnitType.Star); dirty = true; Update("화면 비율을 40:40:20으로 복원했습니다."); }, compact: true));
        additionalCheckBox.IsChecked = SyntaxBox.AdditionalChecks;
        additionalCheckBox.Click += (_, _) => { SyntaxBox.AdditionalChecks = additionalCheckBox.IsChecked == true; dirty = true; Update(); };
        tools.Children.Add(additionalCheckBox); DockPanel.SetDock(tools, Dock.Left); footer.Children.Add(tools);
        status.VerticalAlignment = VerticalAlignment.Center; status.TextTrimming = TextTrimming.CharacterEllipsis; footer.Children.Add(status); return footer;
    }
}
