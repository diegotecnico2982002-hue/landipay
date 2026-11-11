using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Landipay.Client.Servicos;
using Landipay.Domain.Enums;

namespace Landipay.Ui.ViewModels;

// Linhas já formatadas para exibição: a tela não formata nem soma nada.
public sealed record FormaExibicao(string Forma, string Total, string Lancamentos);
public sealed record ProdutoExibicao(string Descricao, string Quantidade, string Total);
public sealed record TurnoExibicao(string Numero, string Abertura, string Fechamento,
    string Calculado, string Informado, string Diferenca, bool DiferencaNegativa);

/// <summary>
/// Relatórios por período. Somente leitura — nenhum comando aqui grava nada.
/// O recorte é explícito na tela (de/até) e todo número exibido vem do serviço,
/// nunca é recalculado no ViewModel: uma conta duplicada na UI é uma segunda verdade.
/// </summary>
public partial class RelatoriosViewModel : ObservableObject
{
    private static readonly CultureInfo Br = new("pt-BR");

    private readonly ServicoDeRelatorios _servico;
    private readonly Guid _lojaId;
    private readonly Guid _usuarioId;

    [ObservableProperty] private DateTime? _de;
    [ObservableProperty] private DateTime? _ate;
    [ObservableProperty] private string _rotuloPeriodo = "Hoje";

    [ObservableProperty] private string _faturamento = "R$ 0,00";
    [ObservableProperty] private string _vendas = "0";
    [ObservableProperty] private string _ticketMedio = "R$ 0,00";
    [ObservableProperty] private string _descontos = "R$ 0,00";
    [ObservableProperty] private string _sangrias = "R$ 0,00";
    [ObservableProperty] private string _suprimentos = "R$ 0,00";
    [ObservableProperty] private string _diferencaCaixa = "R$ 0,00";
    [ObservableProperty] private bool _diferencaNegativa;
    [ObservableProperty] private string _resumoTexto = string.Empty;

    public ObservableCollection<FormaExibicao> Formas { get; } = new();
    public ObservableCollection<ProdutoExibicao> Produtos { get; } = new();
    public ObservableCollection<TurnoExibicao> Turnos { get; } = new();

    public RelatoriosViewModel(ServicoDeRelatorios servico, Guid lojaId, Guid usuarioId)
    {
        _servico = servico;
        _lojaId = lojaId;
        _usuarioId = usuarioId;

        var hoje = DateTime.Today;
        De = hoje;
        Ate = hoje;
        Atualizar();
    }

    [RelayCommand]
    private void Hoje()
    {
        De = DateTime.Today;
        Ate = DateTime.Today;
        RotuloPeriodo = "Hoje";
        Atualizar();
    }

    [RelayCommand]
    private void SeteDias()
    {
        De = DateTime.Today.AddDays(-6);
        Ate = DateTime.Today;
        RotuloPeriodo = "Últimos 7 dias";
        Atualizar();
    }

    [RelayCommand]
    private void Mes()
    {
        var hoje = DateTime.Today;
        De = new DateTime(hoje.Year, hoje.Month, 1);
        Ate = hoje;
        RotuloPeriodo = "Este mês";
        Atualizar();
    }

    [RelayCommand]
    private void Atualizar()
    {
        var de = (De ?? DateTime.Today).Date;
        var ate = (Ate ?? DateTime.Today).Date;

        // Limite local: o dia "até" entra inteiro (dia seguinte às 00:00, exclusivo).
        var inicio = new DateTimeOffset(DateTime.SpecifyKind(de, DateTimeKind.Local));
        var fim = new DateTimeOffset(DateTime.SpecifyKind(ate.AddDays(1), DateTimeKind.Local));

        var relatorio = _servico.Gerar(_lojaId, inicio, fim, _usuarioId);

        Faturamento = Moeda(relatorio.Resumo.Liquido);
        Vendas = relatorio.Resumo.Canceladas == 0
            ? $"{relatorio.Resumo.Finalizadas}"
            : $"{relatorio.Resumo.Finalizadas} · {relatorio.Resumo.Canceladas} cancelada(s)";
        TicketMedio = Moeda(relatorio.Resumo.TicketMedio);
        Descontos = Moeda(relatorio.Resumo.Desconto);
        Sangrias = Moeda(relatorio.Sangrias);
        Suprimentos = Moeda(relatorio.Suprimentos);

        var diferencaTotal = relatorio.Turnos.Sum(t => t.Diferenca);
        DiferencaCaixa = Moeda(diferencaTotal);
        DiferencaNegativa = diferencaTotal < 0;

        ResumoTexto = relatorio.Resumo.Finalizadas == 0
            ? "Nenhuma venda finalizada neste período."
            : $"Bruto {Moeda(relatorio.Resumo.Bruto)} · desconto {Moeda(relatorio.Resumo.Desconto)} · líquido {Moeda(relatorio.Resumo.Liquido)}";

        Formas.Clear();
        foreach (var f in relatorio.Formas)
            Formas.Add(new FormaExibicao(DescricaoForma(f.Forma), Moeda(f.Total), f.Lancamentos.ToString()));

        Produtos.Clear();
        foreach (var p in relatorio.Produtos)
            Produtos.Add(new ProdutoExibicao(p.Descricao, p.Quantidade.ToString("0.####", Br), Moeda(p.Total)));

        Turnos.Clear();
        foreach (var t in relatorio.Turnos)
        {
            Turnos.Add(new TurnoExibicao(
                $"#{t.Numero}",
                t.AbertoEm.ToLocalTime().ToString("dd/MM HH:mm", Br),
                t.FechadoEm?.ToLocalTime().ToString("dd/MM HH:mm", Br) ?? "aberto",
                Moeda(t.Calculado),
                t.Informado is null ? "—" : Moeda(t.Informado.Value),
                Moeda(t.Diferenca),
                t.Diferenca < 0));
        }
    }

    public static string Moeda(decimal valor) => valor.ToString("C", Br);

    public static string DescricaoForma(FormaPagamento forma) => forma switch
    {
        FormaPagamento.Dinheiro => "Dinheiro",
        FormaPagamento.Debito => "Débito",
        FormaPagamento.Credito => "Crédito",
        FormaPagamento.Pix => "PIX",
        FormaPagamento.Vale => "Vale",
        _ => forma.ToString()
    };
}
