#!/usr/bin/env python3
"""G5.5 / R8 — audita Commands de ViewModel e seu Binding no XAML.

Regra do briefing: "todo Command do ViewModel precisa ter botão visível na tela".
O CommunityToolkit.Mvvm gera o Command a partir do atributo [RelayCommand] sobre um
método: Foo() -> FooCommand, e FooAsync() -> FooCommand (o sufixo Async é removido).
Um override por CommandName= é respeitado.
"""
import re
import sys
from pathlib import Path

RAIZ = Path(sys.argv[1] if len(sys.argv) > 1 else ".")
SRC = RAIZ / "src"

rx_attr = re.compile(r"\[RelayCommand(?:\(([^)]*)\))?\]")
rx_metodo = re.compile(r"\b(?:public|private|internal|protected)?\s*(?:async\s+)?(?:Task(?:<[^>]+>)?|void|bool)\s+([A-Za-z_]\w*)\s*\(")
rx_xaml = re.compile(r"Command\w*\s*=\s*\"\{[^}]*?Binding\s+(?:Path\s*=\s*)?([A-Za-z_][\w.]*)")
rx_qualquer = re.compile(r"\b([A-Za-z_]\w*Command)\b")

comandos = {}   # nome do command -> arquivo:linha do metodo
for cs in SRC.rglob("*.cs"):
    if any(p in cs.parts for p in ("bin", "obj")):
        continue
    linhas = cs.read_text(encoding="utf-8", errors="ignore").splitlines()
    for i, linha in enumerate(linhas):
        m = rx_attr.search(linha)
        if not m:
            continue
        # o método vem nas próximas linhas (atributo e assinatura em linhas separadas)
        alvo = None
        for j in range(i, min(i + 4, len(linhas))):
            mm = rx_metodo.search(linhas[j])
            if mm and not linhas[j].strip().startswith("//"):
                alvo = mm.group(1)
                break
        if not alvo:
            continue
        nome = alvo[:-5] if alvo.endswith("Async") else alvo
        argumentos = m.group(1) or ""
        cm = re.search(r"CommandName\s*=\s*\"?([A-Za-z_]\w*)", argumentos)
        cmd = (cm.group(1) if cm else nome) + "Command"
        comandos.setdefault(cmd, f"{cs.relative_to(RAIZ)}:{i + 1} (metodo {alvo})")

bindings = set()
ocorrencias = {}
for xaml in SRC.rglob("*.xaml"):
    if any(p in xaml.parts for p in ("bin", "obj")):
        continue
    texto = xaml.read_text(encoding="utf-8", errors="ignore")
    for b in rx_xaml.findall(texto):
        bindings.add(b.split(".")[-1])
    for c in rx_qualquer.findall(texto):
        ocorrencias.setdefault(c, []).append(str(xaml.relative_to(RAIZ)))

orfaos = {c: onde for c, onde in comandos.items() if c not in bindings and c not in ocorrencias}
usados = {c: onde for c, onde in comandos.items() if c in bindings or c in ocorrencias}

print(f"commands declarados via [RelayCommand]: {len(comandos)}")
print(f"bindings de Command no XAML        : {len(bindings)}")
print(f"commands com referencia no XAML    : {len(usados)}")
print(f"commands ORFAOS                    : {len(orfaos)}")
print()
print("--- detalhe ---")
for c in sorted(comandos):
    estado = "ORFAO" if c in orfaos else ("binding" if c in bindings else "referencia")
    print(f"  {estado:10} {c:32} {comandos[c]}")
    if c in orfaos:
        print(f"             -> nenhum botao/menu no XAML")

sys.exit(1 if orfaos else 0)
