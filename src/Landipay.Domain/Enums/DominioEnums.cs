namespace Landipay.Domain.Enums;

public enum RegimeTributario
{
    SimplesNacional = 1,
    SimplesNacionalExcessoSublimite = 2,
    LucroPresumido = 3,
    LucroReal = 4,
    Mei = 5
}

public enum TipoMovimentoEstoque
{
    Entrada = 1,
    Saida = 2,
    Ajuste = 3,
    Perda = 4,
    Devolucao = 5
}

public enum StatusVenda
{
    Aberta = 1,
    Finalizada = 2,
    Cancelada = 3
}

public enum FormaPagamento
{
    Dinheiro = 1,
    Debito = 2,
    Credito = 3,
    Pix = 4,
    Vale = 5
}

public enum StatusTurno
{
    Aberto = 1,
    Fechado = 2
}

public enum TipoMovimentoCaixa
{
    Sangria = 1,
    Suprimento = 2
}

public enum StatusLicenca
{
    Ativa = 1,
    EmTolerancia = 2,
    Expirada = 3,
    Revogada = 4
}

public enum TipoMensagemOutbox
{
    VendaFinalizada = 1,
    VendaCancelada = 2,
    MovimentoEstoque = 3,
    TurnoFechado = 4,
    Auditoria = 5
}

public enum OrigemLog
{
    Sistema = 1,
    Operador = 2,
    SuporteRemoto = 3
}

/// <summary>
/// Aplicativos desktop do Landipay. Dois executáveis, um backend, um banco:
/// o escopo do app vai na sessão e é validado a cada operação — não é decoração de UI.
/// </summary>
public enum Aplicativo
{
    /// <summary>Gestão: cadastros, relatórios, usuários, configurações, licença.</summary>
    Admin = 1,

    /// <summary>Frente de caixa: venda, pagamento, sangria/suprimento, turno.</summary>
    Pos = 2
}

/// <summary>Papel da estação de trabalho, para rastreabilidade de instalação.</summary>
public enum TipoEstacao
{
    Admin = 1,
    Pos = 2,
    Mista = 3
}
