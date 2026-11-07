using System.Windows;
using System.Windows.Controls;
namespace KeySwitch.InputProbe;
internal static class Program
{
    [STAThread] private static void Main()
    {
        var app=new Application();
        var panel=new StackPanel { Margin=new Thickness(24) };
        panel.Children.Add(new TextBlock { Text="Проверка ввода KeySwitch",FontSize=24 });
        panel.Children.Add(new TextBlock { Text="Синтетический текст остаётся только в памяти. Окно не сохраняет файлы.",Margin=new Thickness(0,12,0,20),TextWrapping=TextWrapping.Wrap });
        var text=new TextBox { AcceptsReturn=true,MinHeight=140,FontSize=24,TextWrapping=TextWrapping.Wrap,Name="TestInput" };
        panel.Children.Add(new Label { Content="Пробный текст",Target=text }); panel.Children.Add(text);
        panel.Children.Add(new TextBlock { Text="Пробное поле пароля",Margin=new Thickness(0,20,0,8) });
        panel.Children.Add(new PasswordBox { FontSize=24,Name="ProtectedInput" });
        var window=new Window { Title="KeySwitch · проверка ввода",Width=720,Height=440,Content=panel };
        window.Loaded+=(_,_)=>text.Focus(); app.Run(window);
    }
}
