using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Landipay.Client.Seguranca;
using Landipay.Domain.Enums;

namespace Landipay.Ui.ViewModels;

/// <summary>
/// Tela de licença: ocupa o lugar do login quando o aplicativo não pode operar.
///
/// Ela é consequência, não controle: quem barra a venda é o <see cref="ServicoDeCaixa"/> via
/// <see cref="ServicoDeLicenca.ExigirOperacao"/>. Esta tela só explica o motivo e permite
/// revalidar depois que a licença for renovada pelo instalador (passo 4 — Ativação).
/// </summary>
public partial class LicencaViewModel : ObservableObject
{
    private readonly Action _revalidar;

    [ObservableProperty] private string _titulo = "Licença";
    [ObservableProperty] private string _mensagem = string.Empty;
    [ObservableProperty] private string _detalhe = string.Empty;
    [ObservableProperty] private string _codigo = string.Empty;
    [ObservableProperty] private string _instrucao = string.Empty;

    public LicencaViewModel(EstadoLicenca estado, Aplicativo app, Action revalidar)
    {
        _revalidar = revalidar;

        Titulo = estado.Codigo switch
        {
            CodigosLicenca.Expirada => "Licença expirada",
            CodigosLicenca.Revogada => "Licença revogada",
            CodigosLicenca.Adulterada => "Licença inconsistente",
            CodigosLicenca.Ausente => "Sem licença nesta instalação",
            _ => "Licença"
        };

        Codigo = estado.Codigo;
        Mensagem = estado.Mensagem;

        var partes = new List<string>();
        if (estado.ExpiraEm is { } expira)
        {
            partes.Add($"expira em {expira:dd/MM/yyyy}");
            partes.Add(estado.DiasRestantes >= 0
                ? $"{estado.DiasRestantes} dia(s) restantes"
                : $"venceu há {Math.Abs(estado.DiasRestantes)} dia(s)");
        }
        if (estado.UltimaValidacaoEm is { } validacao)
            partes.Add($"última validação online em {validacao:dd/MM/yyyy}");
        if (estado.ExpiraEm is not null)
            partes.Add(estado.AssinaturaVerificada ? "assinatura conferida" : "assinatura não conferida (sem chave pinada)");

        if (partes.Count == 0)
            partes.Add("instalação sem licença ativa");

        Detalhe = string.Join(" · ", partes);

        Instrucao = $"Aplicativo: {Domain.Seguranca.CatalogoDeAcesso.NomeDoApp(app)}. " +
                    "Para renovar, abra o instalador do Landipay em modo reparo e use o passo " +
                    "\"Ativação\" com o token novo — o instalador valida a assinatura offline. " +
                    "Depois volte aqui e clique em Verificar novamente.";
    }

    [RelayCommand]
    private void Verificar() => _revalidar();
}
