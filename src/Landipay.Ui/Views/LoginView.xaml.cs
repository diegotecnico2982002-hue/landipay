using System.Windows.Controls;
using Landipay.Ui.ViewModels;

namespace Landipay.Ui.Views;

public partial class LoginView : UserControl
{
    public LoginView() => InitializeComponent();

    // PasswordBox não expõe Password como DependencyProperty (de propósito, no WPF):
    // a ponte para o ViewModel é feita aqui, no code-behind, e a senha não é logada.
    private void Senha_OnPasswordChanged(object sender, EventArgs e)
    {
        if (DataContext is LoginViewModel vm && sender is PasswordBox box)
            vm.Senha = box.Password;
    }

    private void SenhaNova_OnPasswordChanged(object sender, EventArgs e)
    {
        if (DataContext is LoginViewModel vm && sender is PasswordBox box)
            vm.Senha = box.Password;
    }

    private void Confirmacao_OnPasswordChanged(object sender, EventArgs e)
    {
        if (DataContext is LoginViewModel vm && sender is PasswordBox box)
            vm.Confirmacao = box.Password;
    }
}
