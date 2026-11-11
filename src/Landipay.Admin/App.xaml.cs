using System.Windows;
using Landipay.Domain.Enums;
using Landipay.Ui;

namespace Landipay.Admin;

/// <summary>
/// Executável de GESTÃO. Só monta o CompositionRoot com <see cref="Aplicativo.Admin"/>:
/// a navegação e o login já nascem restritos a este escopo, e o serviço de autorização
/// barra qualquer operação de caixa mesmo que alguém a invoque daqui.
/// </summary>
public partial class App : Application
{
    public static ComposicaoApp Servicos { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Servicos = new ComposicaoApp(Aplicativo.Admin);
        AplicarTema(Servicos.TemaAtual);
        Servicos.Shell.TemaAlternado += (_, _) => AlternarTema();

        var janela = new MainWindow { DataContext = Servicos.Shell };
        Servicos.Iniciar();
        janela.Show();
    }

    public static void AplicarTema(string tema)
    {
        var arquivo = tema == "light" ? "Theme.Light" : "Theme.Dark";
        var dicionarios = Current.Resources.MergedDictionaries;
        var novo = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Landipay.Theme;component/Themes/{arquivo}.xaml",
                UriKind.Absolute)
        };

        if (dicionarios.Count > 0) dicionarios[0] = novo;
        else dicionarios.Add(novo);
    }

    public static void AlternarTema()
    {
        var novo = Servicos.TemaAtual == "dark" ? "light" : "dark";
        Servicos.SalvarTema(novo);
        AplicarTema(novo);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Servicos?.Dispose();
        base.OnExit(e);
    }
}
