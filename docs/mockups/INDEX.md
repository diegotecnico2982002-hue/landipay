# Mockups — índice de capturas

Regra deste índice: **cada arquivo diz o que realmente é**. Em `docs/mockups/` convivem dois tipos
de imagem e eles não se misturam:

- **captura real** — PNG tirado da janela WPF em execução (tem barra de título do Windows).
- **mockup HTML** — PNG gerado de `login.html` / `pdv.html` / `installer.html` (sem barra de
  título, com "v0.1.0 · dev" no rodapé). Serve para discutir design, **não** prova tela.

## 1. Os 10 arquivos exigidos no gate final

Os 10 são **capturas reais de execução**, conferidas uma a uma na hora de montar este índice.

| Arquivo | O que mostra | Origem (arquivo de captura) | Tipo |
|---|---|---|---|
| `theme-dark.png` | login do Caixa no tema **escuro**, logo no cabeçalho | `app-login-escuro-logo.png` | captura real |
| `theme-light.png` | login do Caixa no tema **claro**, logo no cabeçalho | `pos-login-claro.png` | captura real |
| `login.png` | login do Caixa, tema claro, logo aplicada | `app-login-claro-logo.png` | captura real |
| `caixa.png` | caixa com turno aberto, lista vazia, total R$ 0,00 | `pos-caixa-claro.png` | captura real |
| `venda.png` | venda finalizada — rodapé "Venda #1 finalizada: R$ 18.90" | `app-caixa-venda.png` | captura real |
| `fechamento.png` | turno fechado — "1 venda(s) · total R$ 18,90 · calculado R$ 118,90 · diferença R$ 0,00" | `app-turno-fechado.png` | captura real |
| `instalador-5-passos.png` | instalador com a trilha completa (Pré-requisitos → Banco → Empresa → Ativação → Administrador → Aplicativos) e "Instalação concluída" | `installer-aplicativos-licenca-caixa-concluido.png` | captura real |
| `instalador-reparo.png` | 2ª execução detectando instalação anterior: "Modo reparo: os dados existentes são mantidos e atualizados, não recriados" | `instalador-6-reparo-detectado.png` | captura real |
| `relatorios.png` | relatório "Hoje": faturamento **R$ 43,29**, 3 vendas, ticket médio **R$ 14,43**, Dinheiro **R$ 37,80**, PIX **R$ 5,49** | `app-relatorios.png` | captura real |
| `msi-instalado.png` | "Programas e Recursos" com `Landipay PDV`, versão `1.1.0`, 76,0 MB, instalado em 11/11/2026 | **capturada nesta sessão** (11/11/2026 18:35) de `_evidence/msi-instalado-full2.png`, recortada na janela | captura real |

> As nove primeiras são cópias sem retoque de capturas anteriores do binário — o arquivo de
> origem está na tabela. Apenas `msi-instalado.png` foi produzida nesta execução.

## 2. Capturas de apoio (não são as do gate)

| Grupo | Arquivos | O que documenta |
|---|---|---|
| Tema lado a lado | `pdv-light.png`, `pdv-dark.png`, `installer-light.png`, `installer-dark.png`, `pdv-compare.png`, `installer-compare.png` | troca de tema em runtime no caixa e no instalador |
| Admin (gestão) | `admin-login-claro.png`, `admin-dashboard-claro.png`, `admin-dashboard-escuro.png` | app de gestão separado do caixa |
| Primeiro acesso / licença | `app-primeiro-acesso.png`, `app-login-real.png` (tela "Criar administrador"), `app-licenca-bloqueada.png`, `acesso-negado-claro.png`, `acesso-negado-escuro.png` | criação do admin, bloqueio por licença, acesso negado por papel |
| Instalador passo a passo | `instalador-1-prerequisitos.png` … `instalador-6-reparo-detectado.png` | cada passo, incluindo o modo reparo |
| ARP / escolha de apps | `installer-aplicativos*.png` | entradas no Windows e escopo de aplicativos da licença |
| Marca | `app-icones-extraidos.png` | ícones extraídos do logo |
| **Mockups HTML** | `login-light.png`, `login-dark.png`, `login-compare.png`, `login.html`, `pdv.html`, `installer.html`, `tokens.css` | estudo de design **antes** do app — não são prova de tela |

## 3. Números que as capturas provam

- `fechamento.png`: abertura R$ 100,00 → calculado R$ 118,90 → informado R$ 118,90 → diferença R$ 0,00.
- `relatorios.png`: 43,29 / 14,43 / 37,80 / 5,49 — idêntico ao que o serviço devolve na execução
  registrada em `_evidence/etapa5-T3-fluxo-real.txt` §[6].
- `instalador-reparo.png`: 2ª execução em modo reparo, sem recriar dados.
- `msi-instalado.png`: produto registrado em x64 (não aparece em `WOW6432Node`), versão 1.1.0.
