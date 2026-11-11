using System.Windows;

namespace Landipay.Admin;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void Tema_Click(object sender, RoutedEventArgs e) => App.AlternarTema();

    private void Inicio_Click(object sender, RoutedEventArgs e) => App.Servicos.AbrirInicio();

    private void Catalogo_Click(object sender, RoutedEventArgs e) => App.Servicos.AbrirCatalogo();

    private void Relatorios_Click(object sender, RoutedEventArgs e) => App.Servicos.AbrirRelatorios();

    private void Sair_Click(object sender, RoutedEventArgs e) => App.Servicos.Sair();
}
