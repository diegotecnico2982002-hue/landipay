using Landipay.Domain.Common;

namespace Landipay.Domain.Entities;

/// <summary>
/// Usuário do sistema. A senha NUNCA é armazenada em claro nem comparada em texto:
/// <see cref="SenhaHash"/> recebe o PHC string do Argon2id (Fase 2/3 fornecem o hasher).
/// Bloqueio progressivo é regra de domínio, não de UI — eliminando A5.
/// </summary>
public sealed class Usuario : Entity
{
    public const int FalhasParaBloqueioCurto = 5;
    public const int FalhasParaBloqueioLongo = 10;
    public static readonly TimeSpan DuracaoBloqueioCurto = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan DuracaoBloqueioLongo = TimeSpan.FromMinutes(30);

    public Guid EmpresaId { get; private set; }
    public Guid? LojaId { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public string Login { get; private set; } = string.Empty;
    public string SenhaHash { get; private set; } = string.Empty;
    public bool Ativo { get; private set; } = true;
    public int TentativasFalhas { get; private set; }
    public DateTimeOffset? BloqueadoAte { get; private set; }
    public DateTimeOffset? UltimoAcessoEm { get; private set; }
    public DateTimeOffset? SenhaAlteradaEm { get; private set; }
    public bool ExigeTrocaDeSenha { get; private set; }

    private Usuario() { }

    public static Usuario Criar(Guid empresaId, string nome, string login, string senhaHash, Guid? lojaId = null)
    {
        if (empresaId == Guid.Empty) throw new DomainException("Empresa é obrigatória.");
        if (string.IsNullOrWhiteSpace(nome)) throw new DomainException("Nome é obrigatório.");
        if (string.IsNullOrWhiteSpace(login)) throw new DomainException("Login é obrigatório.");
        if (string.IsNullOrWhiteSpace(senhaHash)) throw new DomainException("Hash de senha é obrigatório.");

        return new Usuario
        {
            EmpresaId = empresaId,
            LojaId = lojaId,
            Nome = nome.Trim(),
            Login = login.Trim().ToLowerInvariant(),
            SenhaHash = senhaHash,
            SenhaAlteradaEm = DateTimeOffset.UtcNow,
            ExigeTrocaDeSenha = true
        };
    }

    public void DefinirSenha(string senhaHash, DateTimeOffset agora, bool exigirTroca = false)
    {
        if (string.IsNullOrWhiteSpace(senhaHash)) throw new DomainException("Hash de senha é obrigatório.");
        SenhaHash = senhaHash;
        SenhaAlteradaEm = agora;
        ExigeTrocaDeSenha = exigirTroca;
        TentativasFalhas = 0;
        BloqueadoAte = null;
    }

    public bool EstaBloqueado(DateTimeOffset agora) => BloqueadoAte is not null && BloqueadoAte > agora;

    /// <summary>
    /// Registra falha e aplica bloqueio progressivo. Retorna o instante de liberação
    /// (ou null se ainda não bloqueou). A contagem é do domínio, não da tela.
    /// </summary>
    public DateTimeOffset? RegistrarFalhaDeAcesso(DateTimeOffset agora)
    {
        TentativasFalhas++;

        if (TentativasFalhas >= FalhasParaBloqueioLongo)
        {
            BloqueadoAte = agora.Add(DuracaoBloqueioLongo);
        }
        else if (TentativasFalhas >= FalhasParaBloqueioCurto)
        {
            BloqueadoAte = agora.Add(DuracaoBloqueioCurto);
        }

        return BloqueadoAte;
    }

    public void RegistrarAcessoBemSucedido(DateTimeOffset agora)
    {
        TentativasFalhas = 0;
        BloqueadoAte = null;
        UltimoAcessoEm = agora;
    }

    public void Desativar() => Ativo = false;
}

public sealed class Papel : Entity
{
    public string Nome { get; private set; } = string.Empty;
    public string? Descricao { get; private set; }
    public bool Sistema { get; private set; }

    private Papel() { }

    public static Papel Criar(string nome, string? descricao = null, bool sistema = true)
    {
        if (string.IsNullOrWhiteSpace(nome)) throw new DomainException("Nome do papel é obrigatório.");
        return new Papel { Nome = nome.Trim(), Descricao = descricao, Sistema = sistema };
    }
}

public sealed class Permissao : Entity
{
    public string Codigo { get; private set; } = string.Empty;
    public string Descricao { get; private set; } = string.Empty;

    private Permissao() { }

    public static Permissao Criar(string codigo, string descricao)
    {
        if (string.IsNullOrWhiteSpace(codigo)) throw new DomainException("Código da permissão é obrigatório.");
        return new Permissao { Codigo = codigo.Trim().ToLowerInvariant(), Descricao = descricao };
    }
}

public sealed class UsuarioPapel
{
    public Guid UsuarioId { get; private set; }
    public Guid PapelId { get; private set; }

    private UsuarioPapel() { }
    public UsuarioPapel(Guid usuarioId, Guid papelId) { UsuarioId = usuarioId; PapelId = papelId; }
}

public sealed class PapelPermissao
{
    public Guid PapelId { get; private set; }
    public Guid PermissaoId { get; private set; }

    private PapelPermissao() { }
    public PapelPermissao(Guid papelId, Guid permissaoId) { PapelId = papelId; PermissaoId = permissaoId; }
}
