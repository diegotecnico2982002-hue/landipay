using Landipay.Domain.Entities;
using Landipay.Domain.Enums;
using Landipay.Domain.Seguranca;
using Landipay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Landipay.Client.Seguranca;

/// <summary>
/// Cria permissões, papéis e vínculos a partir do <see cref="CatalogoDeAcesso"/>.
///
/// É idempotente de propósito: roda no instalador, na abertura dos apps e nos testes.
/// Assim uma base criada antes da separação Admin/Caixa ganha os papéis sem migração manual —
/// e reinstalar nunca duplica permissão nem quebra vínculo.
/// </summary>
public static class SemeadorDeSeguranca
{
    public static void Garantir(LandipayDbContext db)
    {
        var agora = DateTimeOffset.UtcNow;

        // 1. Permissões
        var existentes = db.Permissoes.ToList();
        foreach (var (codigo, descricao) in CatalogoDeAcesso.Descricoes)
        {
            if (existentes.Any(p => p.Codigo.Equals(codigo, StringComparison.OrdinalIgnoreCase))) continue;
            db.Permissoes.Add(Permissao.Criar(codigo, descricao));
        }
        db.SaveChanges();

        // 2. Papéis
        var papeis = db.Papeis.ToList();
        foreach (var nome in CatalogoDeAcesso.PermissoesPorPapel.Keys)
        {
            if (papeis.Any(p => p.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase))) continue;
            var novo = Papel.Criar(nome, $"Papel de sistema: {nome}");
            db.Papeis.Add(novo);
            papeis.Add(novo);
        }
        db.SaveChanges();

        // 3. Papel × permissão
        var permissoes = db.Permissoes.ToList();
        var vinculos = db.PapelPermissoes.ToList();

        foreach (var papel in papeis)
        {
            foreach (var codigo in CatalogoDeAcesso.PermissoesDoPapel(papel.Nome))
            {
                var permissao = permissoes.FirstOrDefault(p => p.Codigo.Equals(codigo, StringComparison.OrdinalIgnoreCase));
                if (permissao is null) continue;
                if (vinculos.Any(v => v.PapelId == papel.Id && v.PermissaoId == permissao.Id)) continue;

                var novo = new PapelPermissao(papel.Id, permissao.Id);
                db.PapelPermissoes.Add(novo);
                vinculos.Add(novo);
            }
        }
        db.SaveChanges();

        // 4. Todo usuário ativo precisa de papel: quem não tem recebe OperadorCaixa
        //    (o menor privilégio) — nunca Admin por omissão.
        var semPapel = db.Usuarios
            .Where(u => !db.UsuarioPapeis.Any(up => up.UsuarioId == u.Id))
            .Select(u => u.Id)
            .ToList();

        if (semPapel.Count > 0)
        {
            var operador = papeis.First(p => p.Nome == CatalogoDeAcesso.PapelOperadorCaixa);
            foreach (var usuarioId in semPapel)
                db.UsuarioPapeis.Add(new UsuarioPapel(usuarioId, operador.Id));
            db.SaveChanges();
        }
    }

    /// <summary>Concede um papel a um usuário, sem duplicar.</summary>
    public static void ConcederPapel(LandipayDbContext db, Guid usuarioId, string nomeDoPapel)
    {
        var papel = db.Papeis.FirstOrDefault(p => p.Nome == nomeDoPapel)
                    ?? throw new InvalidOperationException($"Papel '{nomeDoPapel}' não existe. Rode Garantir() antes.");

        if (db.UsuarioPapeis.Any(up => up.UsuarioId == usuarioId && up.PapelId == papel.Id)) return;

        db.UsuarioPapeis.Add(new UsuarioPapel(usuarioId, papel.Id));
        db.SaveChanges();
    }

    /// <summary>Troca os papéis do usuário pelos informados (usado na gestão de usuários).</summary>
    public static void DefinirPapeis(LandipayDbContext db, Guid usuarioId, IEnumerable<string> nomes)
    {
        var alvo = db.Papeis.Where(p => nomes.Contains(p.Nome)).Select(p => p.Id).ToList();
        var atuais = db.UsuarioPapeis.Where(up => up.UsuarioId == usuarioId).ToList();

        foreach (var atual in atuais.Where(a => !alvo.Contains(a.PapelId)))
            db.UsuarioPapeis.Remove(atual);

        foreach (var papelId in alvo.Where(id => atuais.All(a => a.PapelId != id)))
            db.UsuarioPapeis.Add(new UsuarioPapel(usuarioId, papelId));

        db.SaveChanges();
    }
}
