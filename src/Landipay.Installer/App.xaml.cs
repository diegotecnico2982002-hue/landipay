using System.IO;
using System.Net.Http;
using System.Windows;
using Landipay.Client.Servicos;
using Landipay.Infrastructure.Persistence;
using Landipay.Installer.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Landipay.Installer;

public partial class App : Application
{
    private static bool _claro;

    /// <summary>Troca de tema em tempo real (mesmo contrato dos apps: tokens, nunca hex).</summary>
    public static void AlternarTema()
    {
        _claro = !_claro;
        var arquivo = _claro ? "Theme.Light" : "Theme.Dark";
        var dicionarios = Current.Resources.MergedDictionaries;
        var novo = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Landipay.Theme;component/Themes/{arquivo}.xaml",
                UriKind.Absolute)
        };
        if (dicionarios.Count > 0) dicionarios[0] = novo;
        else dicionarios.Add(novo);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"Falha inesperada no instalador:\n\n{args.Exception.Message}",
                "Landipay — instalador", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };

        var pasta = Environment.GetEnvironmentVariable("LANDIPAY_DADOS") is { Length: > 0 } p
            ? p
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Landipay");

        Directory.CreateDirectory(pasta);

        // O banco NÃO é migrado aqui: aplicar schema é o passo 2, com resultado visível.
        var opcoes = new DbContextOptionsBuilder<LandipayDbContext>()
            .UseSqlite($"Data Source={Path.Combine(pasta, "landipay.db")}")
            .Options;

        var db = new LandipayDbContext(opcoes);
        var servico = new ServicoInstalador(db, pasta);

        // Chave pública da licença (SPKI base64). Sem ela, a ativação falha com mensagem clara
        // em vez de "aceitar qualquer coisa" — pinagem obrigatória.
        byte[]? chaveLicenca = null;
        var chaveBase64 = Environment.GetEnvironmentVariable("LANDIPAY_CHAVE_LICENCA");
        if (!string.IsNullOrWhiteSpace(chaveBase64))
        {
            try { chaveLicenca = Convert.FromBase64String(chaveBase64); }
            catch (FormatException) { chaveLicenca = null; }
        }

        var urlApi = Environment.GetEnvironmentVariable("LANDIPAY_API") ?? "http://localhost:5080";
        var tokenInicial = Environment.GetEnvironmentVariable("LANDIPAY_LICENCA");
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        var vm = new InstaladorViewModel(servico, http, chaveLicenca, urlApi, tokenInicial);
        var janela = new JanelaInstalador { DataContext = vm };

        MainWindow = janela;
        janela.Show();
    }
}
