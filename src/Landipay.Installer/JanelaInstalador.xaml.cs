using System.Windows;
using System.Windows.Controls;
using Landipay.Installer.ViewModels;

namespace Landipay.Installer;

public partial class JanelaInstalador : Window
{
    public JanelaInstalador() => InitializeComponent();

    // PasswordBox não aceita binding por design: o valor só sai daqui.
    private InstaladorViewModel? Vm => DataContext as InstaladorViewModel;

    private void Senha_Alterada(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) Vm.AdminSenha = ((PasswordBox)sender).Password;
    }

    private void Confirmacao_Alterada(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) Vm.AdminConfirmacao = ((PasswordBox)sender).Password;
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Troca de tema sem reiniciar: substitui o dicionário de tokens (índice 0).</summary>
    private void Tema_Click(object sender, RoutedEventArgs e) => App.AlternarTema();
}
