#!/usr/bin/env python3
"""Landipay — varredura de segredos no binário publicado.

Por que existe: a regra R2 do briefing ("zero segredos no binário") não pode ser
provada por inspeção visual de 110 arquivos de DLL. E uma varredura que "não acha
nada" pode estar simplesmente quebrada — por isso o CONTROLE POSITIVO: o mesmo
scanner, apontado para um arquivo com segredo plantado, TEM de detectar.

Uso:
    python assets/audit-scan.py <diretorio>                  # varredura normal
    python assets/audit-scan.py <diretorio> --canary          # planta canary, exige deteccao
    python assets/audit-scan.py <diretorio> --canary --keep   # idem, mas nao apaga o canary

Saida: relatorio legivel no stdout + JSON em --json <arquivo>.
Codigo de saida: 0 = limpo (e controle positivo OK, quando pedido)
                 1 = achados encontrados, ou controle positivo NAO detectado
                 2 = erro de uso

Padroes (P1..P10) do briefing da Fase 5.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time

# --------------------------------------------------------------------------
# P1..P10 — os 10 padroes exigidos no briefing
# --------------------------------------------------------------------------
PADROES: list[tuple[str, str, str]] = [
    ("P1", "PEM PRIVATE KEY", r"-----BEGIN (?:RSA |EC |OPENSSH |ENCRYPTED )?PRIVATE KEY-----"),
    ("P2", "password=", r"password\s*=\s*[^\s;\"']{3,}"),
    ("P3", "Authorization literal", r"Authorization\s*[:=]\s*[\"']?(?:Basic|Bearer|Token)\s+[A-Za-z0-9+/=._-]{8,}"),
    ("P4", "AWS access key", r"AKIA[0-9A-Z]{16}"),
    ("P5", "chave estilo OpenAI", r"sk-[A-Za-z0-9]{20,}"),
    ("P6", "PAT do GitHub", r"github_pat_[A-Za-z0-9_]{20,}"),
    ("P7", "JWT", r"eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}"),
    ("P8", "URL com credencial", r"[a-z][a-z0-9+.-]*://[^\s/:@\"']+:[^\s/@\"']+@[^\s/\"']+"),
    ("P9", "connection string com Password=", r"(?:Password|Pwd)\s*=\s*[^;\s\"']{3,}\s*;"),
    ("P10", "private key OpenSSH", r"-----BEGIN OPENSSH PRIVATE KEY-----"),
]

# Falsos positivos conhecidos e deliberados no binário:
#  - a propria documentacao/teste que cita os padroes como exemplo;
#  - strings de bibliotecas de terceiros que so NOMEIAM o parametro.
# Cada supressao precisa de justificativa escrita aqui — nada de "ignore tudo".
SUPRESSOES: list[tuple[str, str, str]] = [
    # (regex do caminho, id do padrao, motivo)
    (r"[\\/]_canary\.txt$", "*", "arquivo do controle positivo, criado e apagado pelo proprio scanner"),
]


def _suprimido(caminho: str, pid: str) -> str | None:
    baixo = caminho.replace("\\", "/")
    for rx, padrao, motivo in SUPRESSOES:
        if re.search(rx, baixo, re.IGNORECASE) and padrao in ("*", pid):
            return motivo
    return None


def _texto(dados: bytes) -> str:
    """Decodifica sem estourar em binario: latin-1 cobre todos os 256 bytes.

    Isso e essencial: segredo em DLL .NET mora em #US (UTF-16LE) e em ASCII.
    latin-1 preserva ASCII byte a byte, e as strings UTF-16 aparecem como
    sequencias legiveis intercaladas por \\x00 — por isso o texto tambem e
    "desintercalado" antes do match (ver _texto_utf16).
    """
    return dados.decode("latin-1", errors="ignore")


def _texto_utf16(dados: bytes) -> str:
    """Extrai o que parece UTF-16LE: bytes pares legiveis, impares zerados."""
    try:
        return dados.decode("utf-16-le", errors="ignore")
    except Exception:  # pragma: no cover - latin-1 ja cobre o fallback
        return ""


def varrer(raiz: str) -> dict:
    arquivos = 0
    bytes_lidos = 0
    achados: list[dict] = []
    suprimidos: list[dict] = []
    brutos: set[str] = set()
    inicio = time.time()

    for pasta, _dirs, nomes in os.walk(raiz):
        for nome in sorted(nomes):
            caminho = os.path.join(pasta, nome)
            try:
                with open(caminho, "rb") as fh:
                    dados = fh.read()
            except OSError as exc:
                achados.append({"padrao": "ERRO", "arquivo": caminho, "detalhe": str(exc)})
                continue

            arquivos += 1
            bytes_lidos += len(dados)
            texto = _texto(dados)
            texto16 = _texto_utf16(dados) if len(dados) % 2 == 0 else ""

            for pid, rotulo, rx in PADROES:
                motivo = _suprimido(caminho, pid)
                for fonte in (texto, texto16) if texto16 else (texto,):
                    m = re.search(rx, fonte, re.IGNORECASE)
                    if not m:
                        continue
                    # registra ANTES da supressao: o controle positivo precisa contar
                    # mesmo quando o arquivo do canary esta deliberadamente suprimido.
                    brutos.add(pid)
                    registro = {
                        "padrao": pid,
                        "rotulo": rotulo,
                        "arquivo": os.path.relpath(caminho, raiz).replace("\\", "/"),
                        "offset": m.start(),
                        # nunca ecoar o segredo inteiro no relatorio
                        "amostra": (m.group(0)[:6] + "…") if len(m.group(0)) > 6 else m.group(0),
                    }
                    if motivo:
                        registro["suprimido"] = motivo
                        suprimidos.append(registro)
                    else:
                        achados.append(registro)
                    break

    return {
        "raiz": raiz,
        "arquivos": arquivos,
        "bytes": bytes_lidos,
        "achados": achados,
        "suprimidos": suprimidos,
        "padroes_detectados": sorted(brutos),
        "duracao_s": round(time.time() - inicio, 2),
    }


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description="Varredura de segredos do binário Landipay")
    ap.add_argument("diretorio")
    ap.add_argument("--json", dest="json_out")
    ap.add_argument("--canary", action="store_true", help="planta segredo e exige deteccao")
    ap.add_argument("--keep", action="store_true", help="nao apaga o canary ao final")
    args = ap.parse_args(argv)

    raiz = os.path.abspath(args.diretorio)
    if not os.path.isdir(raiz):
        print(f"erro: nao e diretorio: {raiz}", file=sys.stderr)
        return 2

    canary = os.path.join(raiz, "_canary.txt")
    controle_ok: bool | None = None

    if args.canary:
        with open(canary, "w", encoding="utf-8") as fh:
            fh.write("password=CANARY123\n")
        print(f"controle positivo: canary plantado em {canary}")

    try:
        rel = varrer(raiz)
    finally:
        if args.canary and not args.keep and os.path.exists(canary):
            os.remove(canary)
            print("controle positivo: canary removido")

    if args.canary:
        # o controle positivo vale sobre TODAS as ocorrencias brutas, inclusive
        # as suprimidas (o canary e justamente um arquivo suprimido).
        controle_ok = "P2" in rel["padroes_detectados"]
        rel["controle_positivo"] = controle_ok
        # o achado do canary nao e um segredo real do produto
        rel["achados"] = [a for a in rel["achados"] if a.get("arquivo") != "_canary.txt"]

    print()
    print("=" * 68)
    print("VARREDURA DE SEGREDOS — Landipay")
    print("=" * 68)
    print(f"raiz:             {rel['raiz']}")
    print(f"arquivos varridos: {rel['arquivos']}")
    print(f"bytes varridos:    {rel['bytes']:,}".replace(",", "."))
    print(f"duracao:           {rel['duracao_s']}s")
    print(f"achados:           {len(rel['achados'])}")
    print(f"suprimidos:        {len(rel['suprimidos'])}")
    if args.canary:
        print(f"controle positivo: {'DETECTADO' if controle_ok else 'NAO DETECTADO (varredura invalida)'}")
    print("-" * 68)
    for a in rel["achados"][:50]:
        print(f"  [{a['padrao']}] {a.get('rotulo', a.get('padrao'))}: {a.get('arquivo')} @ {a.get('offset')}")
    for s in rel["suprimidos"][:10]:
        print(f"  (suprimido) [{s['padrao']}] {s.get('arquivo')} — {s.get('suprimido')}")
    print("=" * 68)

    if args.json_out:
        with open(args.json_out, "w", encoding="utf-8") as fh:
            json.dump(rel, fh, indent=2, ensure_ascii=False)
        print(f"json: {args.json_out}")

    if args.canary and not controle_ok:
        return 1
    return 1 if rel["achados"] else 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
