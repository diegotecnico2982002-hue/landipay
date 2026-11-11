# 07 — Inferências e limites declarados

> Regra deste documento: **tudo que não foi validado por execução nesta máquina aparece aqui
> marcado como `(inferência)`**. Nada aqui é usado como prova de gate.

## 1. Pontos não validados em execução

### 1.1 Volume de dados nos relatórios `(inferência)`
Os relatórios (`ServicoDeRelatorios`) filtram o período **em memória**, de propósito: o EF Core
sobre SQLite não traduz comparação de `DateTimeOffset` para SQL, e uma tradução parcial
devolveria número errado em silêncio. O recorte por loja vai para o SQL; data e somas ficam em
memória.
**`(inferência)`** para milhares de vendas/mês (o volume de uma loja de PDV) isso mantém os
relatórios rápidos; acima de centenas de milhares de vendas o caminho correto é uma coluna de
data **indexada** com filtro no SQL — não um `ToList()` maior. Não foi medido com volume
grande: a maior base exercitada nesta entrega tem 4 vendas.

### 1.2 Distribuição para terceiros sem Authenticode `(inferência)`
O MSI e os executáveis **não são assinados** (sem certificado de code signing).
**`(inferência)`** de que, em máquina de terceiro, o Windows SmartScreen exibirá "Windows
protegeu o seu PC" na primeira execução e que o antivírus pode colocar o instalador em
quarentena por reputação baixa. Isso não foi observado nesta máquina (onde o binário é local e
o SmartScreen não se aplica da mesma forma). Mitigação de custo: certificado OV/EV.

### 1.3 `DisplayIcon` vazio no ARP `(inferência)`
A entrada `Landipay PDV` em "Aplicativos instalados" aparece com `DisplayIcon` vazio.
**`(inferência)`** de que é comportamento do registro por usuário (`perUser`) no Windows 10: o
`ARPPRODUCTICON` está na tabela `Property` do MSI e é o que o Windows usa para o ícone do item;
o `DisplayIcon` só é preenchido por pacotes `perMachine`. O ícone do item foi conferido
visualmente na captura (`mockups/msi-instalado.png`); a causa raiz é inferida, não provada em
outra versão do Windows.

## 2. Limites de escopo (não são inferências — são decisões)

| Limite | Situação | Onde |
|---|---|---|
| Sem fiscal (NFC-e/NF-e/SAT) | fora do 1.0 | `08 §4` |
| Sem entrada de estoque / inventário | fora do 1.0 (só baixa na venda) | `08 §4` |
| Sem multi-loja sincronizada | `Outbox` pronto, transporte não implementado | `docs/02` |
| Sem assinatura de binário | decisão de custo | `05-msi.md §5` |
| Suporte remoto sem agente | opt-in por código de 6 dígitos, TTL 30 min | `docs/04` |

## 3. O que NÃO foi feito nesta execução (registro honesto)

1. **Árvore de namespaces dos binários do Gestor**: o `ilspycmd` (CLI) não instala neste
   ambiente — o SDK 8.0.428 recusa o pacote ("versão do formato superior à compatível"). A
   árvore obtida é a do `SyntaxInstaller.exe` **decompilado**, que existe em disco. Registrado
   em `_evidence/ilspy-tree.txt`.
2. **Nenhum binário do legado foi executado** (regra do briefing) — a análise é estática.
3. **Screenshots de UI**: `login`, `caixa`, `venda`, `fechamento`, `relatórios`, `instalador` e
   `msi-instalado` são capturas reais da aplicação; o `msi-instalado.png` foi capturado nesta
   sessão. Os demais vêm de execuções anteriores do mesmo binário e estão mapeados em
   `mockups/INDEX.md` com a origem de cada arquivo.
4. **`dotnet publish` para ARM64** não foi testado — só x64.
