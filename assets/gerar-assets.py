#!/usr/bin/env python3
"""
Gera os assets de marca do Landipay a partir das logos originais (PNG com fundo branco).

Saída (src/Landipay.Theme/Assets):
  icone-256.png      símbolo quadrado, fundo transparente (base do .ico)
  app.ico            16/24/32/48/64/128/256 — ícone de janela, exe e MSI
  logo-horizontal.png símbolo + wordmark, transparente, para cabeçalho
  logo-completa.png   logo completa com assinatura, transparente, para telas grandes

Regra do alpha: o fundo é branco puro e as cores da marca são escuras/saturadas, então
alpha = 0 onde o canal mais alto é ~branco e alpha = 255 onde é claramente cor. Isso
preserva as cores originais (sem o "lavado" que o unpremultiply clássico causa) e mantém
as bordas suaves do antialiasing.

Uso:  python assets/gerar-assets.py
"""
from pathlib import Path
from PIL import Image

RAIZ = Path(__file__).resolve().parent.parent
ORIGEM = Path(r"C:\Users\diego\Desktop\PDV - Landipay\LANDIPAY")
DESTINO = RAIZ / "src" / "Landipay.Theme" / "Assets"

MINIMALISTA = ORIGEM / "Logo minimalista Landipay em verde.png"
COMPLETA = ORIGEM / "Logo Landipay_ Tecnologia que Conecta Pagamentos.png"

BRANCO_OPACO = 200   # canal máximo <= isto -> totalmente opaco
BRANCO_LIMPO = 248   # canal máximo >= isto -> totalmente transparente


def remover_branco(imagem: Image.Image) -> Image.Image:
    """Converte fundo branco em transparência preservando as cores da marca."""
    rgba = imagem.convert("RGBA")
    pixels = rgba.load()
    largura, altura = rgba.size

    for y in range(altura):
        for x in range(largura):
            r, g, b, _ = pixels[x, y]
            maior = max(r, g, b)

            if maior <= BRANCO_OPACO:
                alfa = 255
            elif maior >= BRANCO_LIMPO:
                alfa = 0
            else:
                alfa = int(round(255 * (BRANCO_LIMPO - maior) / (BRANCO_LIMPO - BRANCO_OPACO)))

            pixels[x, y] = (r, g, b, alfa)

    return rgba


def caixa_do_conteudo(imagem: Image.Image) -> tuple[int, int, int, int]:
    caixa = imagem.getbbox()
    if caixa is None:
        raise SystemExit("Imagem sem conteúdo após remover o fundo.")
    return caixa


def quadrado(imagem: Image.Image, margem: float = 0.10) -> Image.Image:
    """Recorta o conteúdo e centraliza num quadrado transparente com margem proporcional."""
    recorte = imagem.crop(caixa_do_conteudo(imagem))
    lado = int(max(recorte.size) * (1 + 2 * margem))

    tela = Image.new("RGBA", (lado, lado), (0, 0, 0, 0))
    x = (lado - recorte.width) // 2
    y = (lado - recorte.height) // 2
    tela.paste(recorte, (x, y), recorte)
    return tela


def aparar_horizontal(imagem: Image.Image, margem: int = 0) -> Image.Image:
    recorte = imagem.crop(caixa_do_conteudo(imagem))
    if margem:
        tela = Image.new("RGBA", (recorte.width + 2 * margem, recorte.height + 2 * margem), (0, 0, 0, 0))
        tela.paste(recorte, (margem, margem), recorte)
        return tela
    return recorte


def placa_arredondada(simbolo: Image.Image, tamanho: int = 256, cor=(240, 253, 244)) -> Image.Image:
    """
    Símbolo sobre placa clara de cantos arredondados.

    Necessário porque o paralelogramo da marca é verde-escuro (#0F3524) e desaparece em
    barra de tarefas escura — testado em 16/24/32px nos dois fundos. A placa garante o
    símbolo legível independentemente do tema do Windows.
    """
    from PIL import ImageDraw

    tela = Image.new("RGBA", (tamanho, tamanho), (0, 0, 0, 0))
    raio = int(tamanho * 0.22)
    ImageDraw.Draw(tela).rounded_rectangle((0, 0, tamanho - 1, tamanho - 1), raio, fill=cor + (255,))

    interno = int(tamanho * 0.74)
    marca = quadrado(simbolo, margem=0.02).resize((interno, interno), Image.LANCZOS)
    deslocamento = (tamanho - interno) // 2
    tela.paste(marca, (deslocamento, deslocamento), marca)
    return tela


def clarear_para_tema_escuro(imagem: Image.Image, alvo=(240, 253, 244), limite: int = 120) -> Image.Image:
    """
    Troca as partes escuras da arte (wordmark e paralelogramo) por quase-branco, mantendo o
    verde vivo. Sem isso, "landi" (#0F3524) fica invisível sobre o fundo escuro do produto.
    """
    saida = imagem.copy()
    pixels = saida.load()
    largura, altura = saida.size

    for y in range(altura):
        for x in range(largura):
            r, g, b, a = pixels[x, y]
            if a > 0 and max(r, g, b) < limite:
                pixels[x, y] = (alvo[0], alvo[1], alvo[2], a)

    return saida


def main() -> None:
    if not MINIMALISTA.exists() or not COMPLETA.exists():
        raise SystemExit(f"Logos não encontradas em {ORIGEM}")

    DESTINO.mkdir(parents=True, exist_ok=True)

    mini = remover_branco(Image.open(MINIMALISTA))
    comp = remover_branco(Image.open(COMPLETA))

    # Símbolo: a marca fica no terço esquerdo da arte, separada do wordmark.
    # Detecta o fim do símbolo pela primeira faixa vazia depois do início do conteúdo.
    largo, alto = mini.size
    esquerda = caixa_do_conteudo(mini)[0]
    coluna_vazia = None
    for x in range(esquerda, largo):
        if mini.crop((x, 0, x + 1, alto)).getbbox() is None:
            if x + 8 < largo and mini.crop((x, 0, x + 8, alto)).getbbox() is None:
                coluna_vazia = x
                break

    if coluna_vazia is None:
        coluna_vazia = int(largo * 0.34)

    simbolo = mini.crop((0, 0, coluna_vazia, alto))

    icone = placa_arredondada(simbolo, 256)
    icone.save(DESTINO / "icone-256.png")

    tamanhos = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    icone.save(DESTINO / "app.ico", format="ICO", sizes=tamanhos)

    # Variantes por tema: o XAML escolhe via DynamicResource, então trocar o tema troca a arte.
    horizontal = aparar_horizontal(mini, margem=6)
    horizontal.save(DESTINO / "logo-horizontal-claro.png")
    clarear_para_tema_escuro(horizontal).save(DESTINO / "logo-horizontal-escuro.png")

    completa = aparar_horizontal(comp, margem=6)
    completa.save(DESTINO / "logo-completa-claro.png")
    clarear_para_tema_escuro(completa).save(DESTINO / "logo-completa-escuro.png")

    for arquivo in ("icone-256.png", "app.ico", "logo-horizontal-claro.png", "logo-horizontal-escuro.png",
                    "logo-completa-claro.png", "logo-completa-escuro.png"):
        caminho = DESTINO / arquivo
        with Image.open(caminho) as im:
            print(f"  {arquivo:26} {im.size[0]}x{im.size[1]}  {caminho.stat().st_size / 1024:.1f} KB")

    print(f"\nSímbolo recortado em x<{coluna_vazia} (arte original {largo}x{alto}).")


if __name__ == "__main__":
    main()
