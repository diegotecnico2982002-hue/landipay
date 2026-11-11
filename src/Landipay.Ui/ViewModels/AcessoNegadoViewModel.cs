using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Landipay.Ui.ViewModels;

/// <summary>
/// Tela de acesso negado. Recebe o código da permissão que faltou e o texto do serviço —
/// a UI não inventa a razão da negativa, ela repete o que a autorização decidiu.
/// </summary>
public partial class AcessoNegadoViewModel : ObservableObject
{
    private readonly Action _aoVoltar;

    public AcessoNegadoViewModel(string codigo, string mensagem, Action aoVoltar)
    {
        Codigo = codigo;
        Mensagem = mensagem;
        _aoVoltar = aoVoltar;
    }

    public string Codigo { get; }
    public string Mensagem { get; }

    public string Explicacao =>
        "Esta operação não é permitida pelo seu perfil. A tentativa foi registrada na auditoria " +
        "com usuário, permissão e data — o administrador pode auditar em Relatórios/LogAuditoria.";

    [RelayCommand]
    private void Voltar() => _aoVoltar();
}
