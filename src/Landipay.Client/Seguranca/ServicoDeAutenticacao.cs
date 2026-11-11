using Landipay.Domain.Entities;
using Landipay.Domain.Enums;
using Landipay.Domain.Seguranca;
using Landipay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Landipay.Client.Seguranca;

public sealed record SessaoUsuario(
    Guid UsuarioId,
    Guid EmpresaId,
    Guid? LojaId,
    string Nome,
    string Login,
    IReadOnlyList<string> Papeis,
    DateTimeOffset IniciadaEm,
    DateTimeOffset ExpiraEm,
    IReadOnlyList<string>? Permissoes = null,
    Aplicativo App = Aplicativo.Pos)
{
    /// <summary>Permissões efetivas desta sessão (união dos papéis).</summary>
    public IReadOnlyList<string> PermissoesEfetivas =>
        Permissoes ?? Domain.Seguranca.CatalogoDeAcesso.PermissoesDe(Papeis);

    public bool Pode(string permissao) =>
        PermissoesEfetivas.Contains(permissao, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Sessão do operador com dois timeouts independentes: inatividade (15 min) e absoluto
/// (12 h). O timeout roda no serviço, não na tela — fechar a tela não encerra a sessão,
/// e deixar a tela aberta não a mantém viva para sempre.
/// </summary>
public sealed class SessaoService
{
    public static readonly TimeSpan TimeoutInatividade = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan TimeoutAbsoluto = TimeSpan.FromHours(12);

    private readonly TimeProvider _relogio;
    private DateTimeOffset _ultimaAtividade;

    public SessaoService(TimeProvider? relogio = null) => _relogio = relogio ?? TimeProvider.System;

    public SessaoUsuario? Atual { get; private set; }

    public bool EstaAtiva
    {
        get
        {
            if (Atual is null) return false;
            var agora = _relogio.GetUtcNow();
            return agora <= Atual.ExpiraEm && agora - _ultimaAtividade <= TimeoutInatividade;
        }
    }

    public SessaoUsuario Iniciar(Guid usuarioId, Guid empresaId, Guid? lojaId, string nome, string login,
        IReadOnlyList<string>? papeis = null, DateTimeOffset? agora = null,
        IReadOnlyList<string>? permissoes = null, Aplicativo app = Aplicativo.Pos)
    {
        var inicio = agora ?? _relogio.GetUtcNow();
        Atual = new SessaoUsuario(usuarioId, empresaId, lojaId, nome, login,
            papeis ?? Array.Empty<string>(), inicio, inicio.Add(TimeoutAbsoluto), permissoes, app);
        _ultimaAtividade = inicio;
        return Atual;
    }

    /// <summary>Retoma uma sessão emitida pela autenticação (mesma instância de serviço).</summary>
    public void Restaurar(SessaoUsuario sessao)
    {
        Atual = sessao;
        _ultimaAtividade = _relogio.GetUtcNow();
    }

    /// <summary>Chamado a cada ação do operador. Retorna false se a sessão já caducou.</summary>
    public bool RegistrarAtividade()
    {
        if (!EstaAtiva) { Encerrar(); return false; }
        _ultimaAtividade = _relogio.GetUtcNow();
        return true;
    }

    public void Encerrar() => Atual = null;
}

public sealed record ResultadoLogin(bool Ok, string Codigo, SessaoUsuario? Sessao = null,
    DateTimeOffset? BloqueadoAte = null, int TentativasRestantes = 0);

/// <summary>
/// Autenticação real (A5). Regras que moram aqui, e não na tela:
///  - mensagem de erro idêntica para usuário inexistente e senha errada (não enumera contas);
///  - lockout progressivo vindo do domínio (<see cref="Usuario.RegistrarFalhaDeAcesso"/>);
///  - toda tentativa vira evento de auditoria, com ou sem sucesso.
/// </summary>
public sealed class ServicoDeAutenticacao
{
    private readonly LandipayDbContext _db;
    private readonly ISenhaHasher _hasher;
    private readonly TimeProvider _relogio;

    public ServicoDeAutenticacao(LandipayDbContext db, ISenhaHasher hasher, TimeProvider? relogio = null)
    {
        _db = db;
        _hasher = hasher;
        _relogio = relogio ?? TimeProvider.System;
    }

    public ResultadoLogin Autenticar(string? login, string? senha, Aplicativo app = Aplicativo.Pos)
    {
        var agora = _relogio.GetUtcNow();
        var loginNormalizado = (login ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(loginNormalizado) || string.IsNullOrEmpty(senha))
            return new ResultadoLogin(false, "credenciais_invalidas");

        var usuario = _db.Usuarios.SingleOrDefault(u => u.Login == loginNormalizado);

        if (usuario is null)
        {
            _db.Auditoria.Add(LogAuditoria.Registrar("login.falha", "Usuario",
                dadosJson: $"{{\"login\":\"{loginNormalizado.Replace("\"", "'")}\",\"motivo\":\"inexistente\"}}",
                origem: Landipay.Domain.Enums.OrigemLog.Operador, quando: agora));
            _db.SaveChanges();
            return new ResultadoLogin(false, "credenciais_invalidas");
        }

        if (!usuario.Ativo)
            return Falha(usuario, agora, "usuario_inativo");

        if (usuario.EstaBloqueado(agora))
            return new ResultadoLogin(false, "usuario_bloqueado", null, usuario.BloqueadoAte,
                Math.Max(0, Usuario.FalhasParaBloqueioLongo - usuario.TentativasFalhas));

        if (!_hasher.Verificar(senha, usuario.SenhaHash))
        {
            var bloqueadoAte = usuario.RegistrarFalhaDeAcesso(agora);
            var resultado = Falha(usuario, agora, bloqueadoAte is null ? "credenciais_invalidas" : "usuario_bloqueado");
            return resultado with { BloqueadoAte = bloqueadoAte };
        }

        usuario.RegistrarAcessoBemSucedido(agora);

        var papeis = (from up in _db.UsuarioPapeis
                      join p in _db.Papeis on up.PapelId equals p.Id
                      where up.UsuarioId == usuario.Id
                      select p.Nome).ToList();

        // A senha está certa, mas o papel precisa abrir ESTE aplicativo.
        // Admin não entra no Caixa; OperadorCaixa não entra no Admin — e a negativa é auditada.
        if (papeis.Count == 0 || !papeis.Any(p => CatalogoDeAcesso.PapelAcessaApp(p, app)))
        {
            _db.Auditoria.Add(LogAuditoria.Registrar("acesso.negado", "Aplicativo", usuario.Id,
                usuario.EmpresaId, usuario.LojaId, usuario.Id.ToString(),
                dadosJson: $"{{\"app\":\"{app}\",\"papeis\":\"{string.Join(',', papeis)}\",\"motivo\":\"login\"}}",
                origem: Landipay.Domain.Enums.OrigemLog.Operador, quando: agora, maquina: Environment.MachineName));
            _db.SaveChanges();

            return new ResultadoLogin(false, papeis.Count == 0 ? "sem_papel" : "app_nao_autorizado");
        }

        var permissoes = CatalogoDeAcesso.PermissoesDe(papeis);

        _db.Auditoria.Add(LogAuditoria.Registrar("login.ok", "Usuario", usuario.Id, usuario.EmpresaId,
            usuario.LojaId, usuario.Id.ToString(),
            dadosJson: $"{{\"app\":\"{app}\",\"papeis\":\"{string.Join(',', papeis)}\"}}",
            origem: Landipay.Domain.Enums.OrigemLog.Operador, quando: agora, maquina: Environment.MachineName));
        _db.SaveChanges();

        var inicio = agora;
        var sessao = new SessaoUsuario(usuario.Id, usuario.EmpresaId, usuario.LojaId, usuario.Nome,
            usuario.Login, papeis, inicio, inicio.Add(SessaoService.TimeoutAbsoluto), permissoes, app);

        var restantes = Math.Max(0, Usuario.FalhasParaBloqueioCurto - usuario.TentativasFalhas);
        return new ResultadoLogin(true, "ok", sessao, null, restantes);
    }

    private ResultadoLogin Falha(Usuario usuario, DateTimeOffset agora, string codigo)
    {
        _db.Auditoria.Add(LogAuditoria.Registrar("login.falha", "Usuario", usuario.Id, usuario.EmpresaId,
            usuario.LojaId, usuario.Id.ToString(),
            dadosJson: $"{{\"motivo\":\"{codigo}\",\"tentativas\":{usuario.TentativasFalhas}}}",
            origem: Landipay.Domain.Enums.OrigemLog.Operador, quando: agora));
        _db.SaveChanges();

        return new ResultadoLogin(false, codigo, null, usuario.BloqueadoAte,
            Math.Max(0, Usuario.FalhasParaBloqueioLongo - usuario.TentativasFalhas));
    }
}
