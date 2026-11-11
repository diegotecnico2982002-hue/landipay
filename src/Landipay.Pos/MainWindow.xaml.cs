using System.Windows;

namespace Landipay.Pos;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void Tema_Click(object sender, RoutedEventArgs e) => App.AlternarTema();

    private void Sair_Click(object sender, RoutedEventArgs e) => App.Servicos.Sair();
}
