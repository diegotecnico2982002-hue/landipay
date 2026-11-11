using System.Text.Json;

namespace Landipay.Client.Configuracao;

public sealed record PreferenciasUsuario(string Tema = "dark", bool EnvioDeLogOptIn = false, string? UrlEnvioLog = null)
{
    public static bool TemaValido(string? tema) => tema is "dark" or "light";
}

/// <summary>
/// Preferências por usuário (não por máquina): dois operadores no mesmo terminal podem
/// ter temas diferentes. Arquivo em <c>%APPDATA%\Landipay\preferences.json</c>, sem segredo.
/// Corrompido ou ausente, volta ao padrão escuro — nunca impede a abertura do caixa.
/// </summary>
public sealed class ArmazemDePreferencias
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _caminho;

    public ArmazemDePreferencias(string? caminhoArquivo = null)
    {
        _caminho = caminhoArquivo
            ?? (Environment.GetEnvironmentVariable("LANDIPAY_DADOS") is { Length: > 0 } pasta
                ? Path.Combine(pasta, "preferences.json")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Landipay", "preferences.json"));
    }

    public string Caminho => _caminho;

    public PreferenciasUsuario Carregar()
    {
        try
        {
            if (!File.Exists(_caminho)) return new PreferenciasUsuario();
            var prefs = JsonSerializer.Deserialize<PreferenciasUsuario>(File.ReadAllText(_caminho), Json);
            if (prefs is null) return new PreferenciasUsuario();
            return PreferenciasUsuario.TemaValido(prefs.Tema) ? prefs : prefs with { Tema = "dark" };
        }
        catch (JsonException) { return new PreferenciasUsuario(); }
        catch (IOException) { return new PreferenciasUsuario(); }
    }

    public void Salvar(PreferenciasUsuario preferencias)
    {
        var tema = PreferenciasUsuario.TemaValido(preferencias.Tema) ? preferencias.Tema : "dark";
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        File.WriteAllText(_caminho, JsonSerializer.Serialize(preferencias with { Tema = tema }, Json));
    }
}
