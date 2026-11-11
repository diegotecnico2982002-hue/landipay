using Landipay.Domain.Enums;

namespace Landipay.Client.Servicos;

/// <summary>
/// Escolha de aplicativos de uma estação (Opção A da correção: o MSI entrega os dois,
/// a estação habilita quais podem abrir aqui).
/// </summary>
public sealed record EscolhaDeAplicativos(bool Admin, bool Pos)
{
    public bool Algum => Admin || Pos;

    public TipoEstacao Tipo => (Admin, Pos) switch
    {
        (true, true) => TipoEstacao.Mista,
        (true, false) => TipoEstacao.Admin,
        (false, true) => TipoEstacao.Pos,
        _ => TipoEstacao.Admin
    };

    public IReadOnlyList<Aplicativo> Habilitados =>
        new[] { Admin ? Aplicativo.Admin : (Aplicativo?)null, Pos ? Aplicativo.Pos : (Aplicativo?)null }
            .Where(a => a is not null).Select(a => a!.Value).ToList();

    public string Descricao => (Admin, Pos) switch
    {
        (true, true) => "Admin + Caixa (estação mista)",
        (true, false) => "somente Admin (gestão)",
        (false, true) => "somente Caixa (frente de loja)",
        _ => "nenhum"
    };
}

/// <summary>Estado persistido da estação: quais apps foram habilitados na instalação.</summary>
public sealed record ConfiguracaoDeEstacao(
    TipoEstacao Tipo,
    IReadOnlyList<string> Aplicativos,
    DateTimeOffset DefinidaEm,
    string? Maquina);
