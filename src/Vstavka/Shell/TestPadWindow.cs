using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Vstavka.Shell;

// Isolated, opt-in QA fixture. Never shown during normal product use.
internal sealed class TestPadWindow : Window
{
    internal TestPadWindow()
    {
        Title="Вставка — тестовое поле";Width=740;Height=460;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        FontFamily=new FontFamily("Segoe UI");Background=new SolidColorBrush(Color.FromRgb(249,248,252));
        var panel=new StackPanel {Margin=new Thickness(28)};
        panel.Children.Add(new TextBlock{Text="Проверка копирования и вставки",FontSize=24,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)});
        panel.Children.Add(new TextBlock{Text="Только тестовые данные. История этого сеанса изолирована.",Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,20)});
        var box=new TextBox {Name="TestInput",AcceptsReturn=true,FontSize=18,Height=150,Padding=new Thickness(12),Text="",TextWrapping=TextWrapping.Wrap};
        System.Windows.Automation.AutomationProperties.SetName(box,"Тестовое поле ввода");
        var buttons=new WrapPanel{Margin=new Thickness(0,12,0,0)};
        void Add(string label,Action action){var button=new Button{Content=label,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,8)};button.Click+=(_,_)=>action();buttons.Children.Add(button);}
        Add("Копировать тест",()=>{System.Windows.Clipboard.SetText("Вставка: проверка копирования № 1");box.Focus();});
        Add("Копировать ссылку",()=>{System.Windows.Clipboard.SetText("https://example.com/vstavka-test");box.Focus();});
        Add("Копировать цвет",()=>{System.Windows.Clipboard.SetText("#A78BFA");box.Focus();});
        Add("Конфиденциальная копия",()=>{var data=new DataObject();data.SetText("VSTAVKA_PRIVATE_TEST_DO_NOT_STORE");data.SetData("ExcludeClipboardContentFromMonitorProcessing",new MemoryStream(new byte[]{1}));System.Windows.Clipboard.SetDataObject(data,true);box.Focus();});
        Add("Очистить поле",()=>{box.Clear();box.Focus();});
        Add("Вставить в поле",()=>{box.Focus();box.Paste();});
        panel.Children.Add(box);panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock{Text="Ctrl+Shift+V → выбрать карточку → Enter.\nОбычная вставка: Ctrl+V. Ожидается короткий звук.",Margin=new Thickness(0,12,0,0),Foreground=Brushes.DimGray});
        Content=panel;
    }
}
