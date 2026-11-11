using Landipay.Domain.Enums;

namespace Landipay.Domain.Seguranca;

/// <summary>
/// Fonte única das permissões e da matriz papel × permissão.
///
/// Mora no domínio porque **backend e cliente precisam concordar**: o cliente usa para montar
/// a navegação, o servidor usa para autorizar. Se cada lado tivesse a sua lista, a UI e a
/// autorização divergiriam — e "esconder botão" viraria a única proteção, que é exatamente o
/// erro que este desenho corrige.
/// </summary>
public static class CatalogoDeAcesso
{
    // ── Códigos de permissão (recurso.acao) ──────────────────────────────────
    public const string ProdutoLer = "produto.ler";
    public const string ProdutoEscrever = "produto.escrever";
    public const string EstoqueMovimentar = "estoque.movimentar";
    public const string VendaCriar = "venda.criar";
    public const string VendaCancelar = "venda.cancelar";
    public const string VendaDesconto = "venda.desconto";
    public const string CaixaAbrir = "caixa.abrir";
    public const string CaixaFechar = "caixa.fechar";
    public const string CaixaMovimentar = "caixa.movimentar";
    public const string RelatorioLer = "relatorio.ler";
    public const string UsuarioGerenciar = "usuario.gerenciar";
    public const string ConfiguracaoEscrever = "configuracao.escrever";
    public const string LicencaLer = "licenca.ler";

    public static readonly IReadOnlyDictionary<string, string> Descricoes = new Dictionary<string, string>
    {
        [ProdutoLer] = "Consultar produtos e preços",
        [ProdutoEscrever] = "Cadastrar e editar produtos",
        [EstoqueMovimentar] = "Lançar entrada e ajuste de estoque",
        [VendaCriar] = "Operar a frente de caixa e finalizar venda",
        [VendaCancelar] = "Cancelar venda finalizada",
        [VendaDesconto] = "Conceder desconto na venda",
        [CaixaAbrir] = "Abrir turno de caixa",
        [CaixaFechar] = "Fechar turno de caixa",
        [CaixaMovimentar] = "Registrar sangria e suprimento",
        [RelatorioLer] = "Consultar relatórios gerenciais",
        [UsuarioGerenciar] = "Criar, editar e desativar usuários",
        [ConfiguracaoEscrever] = "Alterar configurações da loja",
        [LicencaLer] = "Consultar licença e aplicativos habilitados"
    };

    // ── Papéis de sistema ────────────────────────────────────────────────────
    public const string PapelAdmin = "Admin";
    public const string PapelGerente = "Gerente";
    public const string PapelOperadorCaixa = "OperadorCaixa";

    /// <summary>
    /// Matriz papel → permissões.
    ///
    /// Decisão explícita: <b>Admin não tem permissão de caixa</b> (nem venda, nem abrir/fechar
    /// turno, nem sangria). Segregação de funções: quem administra não opera a gaveta.
    /// O <b>Gerente</b> é o papel que cobre os dois lados — é o supervisor que pode assumir o
    /// caixa em falta de operador, e essa capacidade é concedida de forma explícita.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> PermissoesPorPapel =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [PapelAdmin] = new[]
            {
                ProdutoLer, ProdutoEscrever, EstoqueMovimentar,
                RelatorioLer, UsuarioGerenciar, ConfiguracaoEscrever, LicencaLer
                // sem venda.*, sem caixa.* → não opera a frente de caixa
            },
            [PapelGerente] = new[]
            {
                ProdutoLer, ProdutoEscrever, EstoqueMovimentar,
                VendaCriar, VendaCancelar, VendaDesconto,
                CaixaAbrir, CaixaFechar, CaixaMovimentar,
                RelatorioLer, ConfiguracaoEscrever, LicencaLer
                // sem usuario.gerenciar
            },
            [PapelOperadorCaixa] = new[]
            {
                ProdutoLer,
                VendaCriar,
                CaixaAbrir, CaixaFechar, CaixaMovimentar
                // sem relatorio.ler, sem produto.escrever, sem usuario.gerenciar
            }
        };

    /// <summary>Quais aplicativos cada papel pode abrir.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<Aplicativo>> AppsPorPapel =
        new Dictionary<string, IReadOnlyList<Aplicativo>>(StringComparer.OrdinalIgnoreCase)
        {
            [PapelAdmin] = new[] { Aplicativo.Admin },
            [PapelGerente] = new[] { Aplicativo.Admin, Aplicativo.Pos },
            [PapelOperadorCaixa] = new[] { Aplicativo.Pos }
        };

    public static IReadOnlyList<string> PermissoesDoPapel(string papel) =>
        PermissoesPorPapel.TryGetValue(papel, out var p) ? p : Array.Empty<string>();

    public static IReadOnlyList<Aplicativo> AppsDoPapel(string papel) =>
        AppsPorPapel.TryGetValue(papel, out var a) ? a : Array.Empty<Aplicativo>();

    /// <summary>Permissões efetivas de um conjunto de papéis (união, sem duplicata).</summary>
    public static IReadOnlyList<string> PermissoesDe(IEnumerable<string> papeis) =>
        papeis.SelectMany(PermissoesDoPapel).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

    public static bool PapelAcessaApp(string papel, Aplicativo app) =>
        AppsDoPapel(papel).Contains(app);

    public static string NomeDoApp(Aplicativo app) => app == Aplicativo.Admin
        ? "Landipay Admin"
        : "Landipay Caixa";
}
