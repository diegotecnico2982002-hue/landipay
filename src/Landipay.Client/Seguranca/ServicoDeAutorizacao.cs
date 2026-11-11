using Landipay.Domain.Entities;
using Landipay.Domain.Enums;
using Landipay.Domain.Seguranca;
using Landipay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Landipay.Client.Seguranca;

/// <summary>Negativa de autorização. Carrega o código para a UI exibir sem inventar texto.</summary>
public sealed class AcessoNegadoException : Exception
{
    public string Codigo { get; }
    public AcessoNegadoException(string codigo, string mensagem) : base(mensagem) => Codigo = codigo;
}

/// <summary>
/// Autorização real, no serviço — não na tela.
///
/// Toda operação sensível chama <see cref="Exigir"/> antes de tocar em dados. A negativa
/// lança <see cref="AcessoNegadoException"/> e grava um evento <c>acesso.negado</c> em
/// <see cref="LogAuditoria"/> com usuário, permissão e papéis. Esconder o botão é consequência,
/// nunca a proteção.
/// </summary>
public sealed class ServicoDeAutorizacao
{
    private readonly LandipayDbContext _db;
    private readonly TimeProvider _relogio;

    public ServicoDeAutorizacao(LandipayDbContext db, TimeProvider? relogio = null)
    {
        _db = db;
        _relogio = relogio ?? TimeProvider.System;
    }

    public IReadOnlyList<string> PapeisDe(Guid usuarioId) =>
        (from up in _db.UsuarioPapeis
         join p in _db.Papeis on up.PapelId equals p.Id
         where up.UsuarioId == usuarioId
         select p.Nome).ToList();

    public IReadOnlyList<string> PermissoesDe(Guid usuarioId) =>
        CatalogoDeAcesso.PermissoesDe(PapeisDe(usuarioId));

    public bool Pode(Guid usuarioId, string permissao) =>
        PermissoesDe(usuarioId).Contains(permissao, StringComparer.OrdinalIgnoreCase);

    public bool PodeAcessarApp(Guid usuarioId, Aplicativo app) =>
        PapeisDe(usuarioId).Any(p => CatalogoDeAcesso.PapelAcessaApp(p, app));

    /// <summary>Exige a permissão; lança e audita quando negada.</summary>
    public void Exigir(Guid usuarioId, string permissao)
    {
        if (Pode(usuarioId, permissao)) return;

        var papeis = PapeisDe(usuarioId);
        var usuario = _db.Usuarios.FirstOrDefault(u => u.Id == usuarioId);

        _db.Auditoria.Add(LogAuditoria.Registrar(
            "acesso.negado", "Permissao",
            usuarioId, usuario?.EmpresaId, usuario?.LojaId, usuarioId.ToString(),
            dadosJson: $"{{\"permissao\":\"{permissao}\",\"papeis\":\"{string.Join(',', papeis)}\"}}",
            origem: OrigemLog.Operador, quando: _relogio.GetUtcNow(), maquina: Environment.MachineName));
        _db.SaveChanges();

        throw new AcessoNegadoException(permissao,
            $"Acesso negado: o perfil {string.Join(", ", papeis.DefaultIfEmpty("(sem papel)"))} " +
            $"não tem a permissão '{permissao}'.");
    }

    /// <summary>Usado pelo login: o papel precisa permitir o aplicativo, não só a permissão.</summary>
    public void ExigirApp(Guid usuarioId, Aplicativo app)
    {
        if (PodeAcessarApp(usuarioId, app)) return;

        var papeis = PapeisDe(usuarioId);
        var usuario = _db.Usuarios.FirstOrDefault(u => u.Id == usuarioId);

        _db.Auditoria.Add(LogAuditoria.Registrar(
            "acesso.negado", "Aplicativo",
            usuarioId, usuario?.EmpresaId, usuario?.LojaId, usuarioId.ToString(),
            dadosJson: $"{{\"app\":\"{app}\",\"papeis\":\"{string.Join(',', papeis)}\"}}",
            origem: OrigemLog.Operador, quando: _relogio.GetUtcNow(), maquina: Environment.MachineName));
        _db.SaveChanges();

        throw new AcessoNegadoException("app_nao_autorizado",
            $"O perfil {string.Join(", ", papeis.DefaultIfEmpty("(sem papel)"))} não tem acesso ao " +
            $"{CatalogoDeAcesso.NomeDoApp(app)}.");
    }
}
