using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Landipay.Client.Seguranca;
using Landipay.Client.Servicos;
using Landipay.Domain.Common;
using Landipay.Domain.Entities;
using Landipay.Domain.Enums;
using Landipay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Landipay.Ui.ViewModels;

public sealed class ItemCaixa
{
    public required string Descricao { get; init; }
    public decimal Quantidade { get; init; }
    public decimal Unitario { get; init; }
    public decimal Total => Math.Round(Quantidade * Unitario, 2);

    public string QuantidadeFormatada => Quantidade.ToString("0.###");
    public string UnitarioFormatado => Unitario.ToString("C2");
    public string TotalFormatado => Total.ToString("C2");
}

public partial class CaixaViewModel : ObservableObject
{
    private readonly ServicoDeCaixa _caixa;
    private readonly LandipayDbContext _db;
    private readonly SessaoService _sessao;
    private readonly SessaoUsuario _usuario;
    private readonly Action _aoSair;

    private CaixaTurno? _turno;
    private Venda? _venda;

    [ObservableProperty] private string _codigo = string.Empty;
    [ObservableProperty] private string _mensagem = string.Empty;
    [ObservableProperty] private bool _mensagemDeErro = true;
    [ObservableProperty] private string _statusTurno = "Turno fechado";
    [ObservableProperty] private string _total = "R$ 0,00";
    [ObservableProperty] private string _subtotal = "R$ 0,00";
    [ObservableProperty] private string _pago = "R$ 0,00";
    [ObservableProperty] private string _nomeLoja = string.Empty;
    [ObservableProperty] private string _valorMovimento = string.Empty;
    [ObservableProperty] private string _motivoMovimento = string.Empty;
    [ObservableProperty] private string _valorFechamento = string.Empty;
    [ObservableProperty] private bool _turnoAberto;

    public ObservableCollection<ItemCaixa> Itens { get; } = new();

    public CaixaViewModel(ServicoDeCaixa caixa, LandipayDbContext db, SessaoService sessao,
        SessaoUsuario usuario, Action aoSair)
    {
        _caixa = caixa;
        _db = db;
        _sessao = sessao;
        _usuario = usuario;
        _aoSair = aoSair;

        var loja = _db.Lojas.FirstOrDefault(l => l.Id == usuario.LojaId) ?? _db.Lojas.FirstOrDefault();
        NomeLoja = loja?.Nome ?? "Loja";

        // Retoma turno aberto, se houver (queda de energia não pode perder o caixa).
        _turno = _db.CaixaTurnos.FirstOrDefault(t => t.LojaId == loja!.Id && t.Status == Landipay.Domain.Enums.StatusTurno.Aberto);
        if (_turno is not null)
        {
            TurnoAberto = true;
            StatusTurno = $"Turno #{_turno.Numero} aberto";
        }
    }

    private static decimal Converter(string texto)
    {
        var limpo = new string(texto.Where(c => char.IsDigit(c) || c == ',' || c == '.').ToArray())
            .Replace('.', ',');
        return decimal.TryParse(limpo, out var valor) ? valor : 0m;
    }

    private bool SessaoValida()
    {
        if (_sessao.RegistrarAtividade()) return true;
        MensagemDeErro = true;
        Mensagem = "Sessão expirada. Entre novamente.";
        _aoSair();
        return false;
    }

    [RelayCommand]
    private void AbrirTurno()
    {
        if (!SessaoValida()) return;
        try
        {
            var lojaId = _usuario.LojaId ?? _db.Lojas.Select(l => l.Id).First();
            _turno = _caixa.AbrirTurno(lojaId, _usuario.UsuarioId, Converter(ValorMovimento));
            TurnoAberto = true;
            StatusTurno = $"Turno #{_turno.Numero} aberto";
            MensagemDeErro = false;
            Mensagem = "Turno aberto.";
            ValorMovimento = string.Empty;
        }
        catch (DomainException ex) { Falha(ex.Message); }
    }

    private Venda VendaAberta()
    {
        if (_venda is null || _venda.Status != StatusVenda.Aberta)
        {
            _venda = _caixa.IniciarVenda(_turno!, _usuario.UsuarioId);
            Itens.Clear();
        }
        return _venda;
    }

    [RelayCommand]
    private void Adicionar()
    {
        if (!SessaoValida()) return;
        if (_turno is null) { Falha("Abra o turno antes de vender."); return; }
        if (string.IsNullOrWhiteSpace(Codigo)) return;

        try
        {
            var chave = Codigo.Trim();
            var produto = _db.Produtos.FirstOrDefault(p => p.Ativo && (p.CodigoBarras == chave || p.Sku == chave));
            if (produto is null) { Falha($"Produto '{chave}' não encontrado."); return; }

            VendaAberta().AdicionarItem(produto, 1);
            Itens.Add(new ItemCaixa
            {
                Descricao = produto.Nome,
                Quantidade = 1,
                Unitario = produto.PrecoVenda
            });

            AtualizarTotais();
            MensagemDeErro = false;
            Mensagem = $"{produto.Nome} adicionado.";
            Codigo = string.Empty;
        }
        catch (DomainException ex) { Falha(ex.Message); }
    }

    [RelayCommand]
    private void Pagar(string forma)
    {
        if (!SessaoValida()) return;
        if (_venda is null || _venda.Itens.Count == 0) { Falha("Nenhum item na venda."); return; }

        var restante = _venda.Total - _venda.TotalPago;
        if (restante <= 0) { Falha("Venda já está paga."); return; }

        var tipo = Enum.Parse<FormaPagamento>(forma);
        _venda.RegistrarPagamento(tipo, restante);
        AtualizarTotais();
        MensagemDeErro = false;
        Mensagem = $"Recebido {restante:C2} em {forma}.";
    }

    [RelayCommand]
    private void Finalizar()
    {
        if (!SessaoValida()) return;
        if (_venda is null) { Falha("Nenhuma venda em andamento."); return; }

        try
        {
            _caixa.FinalizarVenda(_venda);
            MensagemDeErro = false;
            Mensagem = $"Venda #{_venda.Numero} finalizada: {_venda.Total:C2}";
            _venda = null;
            Itens.Clear();
            AtualizarTotais();
        }
        catch (DomainException ex) { Falha(ex.Message); }
    }

    [RelayCommand]
    private void Sangria() => MovimentoCaixa(TipoMovimentoCaixa.Sangria);

    [RelayCommand]
    private void Suprimento() => MovimentoCaixa(TipoMovimentoCaixa.Suprimento);

    private void MovimentoCaixa(TipoMovimentoCaixa tipo)
    {
        if (!SessaoValida()) return;
        if (_turno is null) { Falha("Nenhum turno aberto."); return; }

        try
        {
            var valor = Converter(ValorMovimento);
            if (tipo == TipoMovimentoCaixa.Sangria)
                _caixa.RegistrarSangria(_turno, valor, MotivoMovimento, _usuario.UsuarioId);
            else
                _caixa.RegistrarSuprimento(_turno, valor, MotivoMovimento, _usuario.UsuarioId);

            MensagemDeErro = false;
            Mensagem = $"{tipo} de {valor:C2} registrado.";
            ValorMovimento = string.Empty;
            MotivoMovimento = string.Empty;
        }
        catch (DomainException ex) { Falha(ex.Message); }
    }

    [RelayCommand]
    private void FecharTurno()
    {
        if (!SessaoValida()) return;
        if (_turno is null) { Falha("Nenhum turno aberto."); return; }

        try
        {
            var resumo = _caixa.FecharTurno(_turno, _usuario.UsuarioId, Converter(ValorFechamento));
            MensagemDeErro = resumo.Diferenca != 0;
            Mensagem = $"Turno fechado · {resumo.QuantidadeVendas} venda(s) · total {resumo.TotalVendas:C2} · " +
                       $"calculado {resumo.ValorCalculado:C2} · diferença {resumo.Diferenca:C2}";
            _turno = null;
            TurnoAberto = false;
            StatusTurno = "Turno fechado";
            _venda = null;
            Itens.Clear();
            AtualizarTotais();
        }
        catch (DomainException ex) { Falha(ex.Message); }
    }

    private void AtualizarTotais()
    {
        Subtotal = (_venda?.Subtotal ?? 0m).ToString("C2");
        Total = (_venda?.Total ?? 0m).ToString("C2");
        Pago = (_venda?.TotalPago ?? 0m).ToString("C2");
    }

    private void Falha(string mensagem)
    {
        MensagemDeErro = true;
        Mensagem = mensagem;
    }
}
