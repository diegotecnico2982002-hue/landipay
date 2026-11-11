# 05 — MSI (WiX 5)

> Última atualização: 11/11/2026, por execução real nesta máquina (Windows 10 22H2, .NET SDK 8.0.428, WiX 5.0.2).

## 1. Identificação do artefato

| Item | Valor |
|---|---|
| Arquivo | `dist\Landipay-1.1.0.msi` |
| Tamanho | 18.808.832 bytes (17,9 MB) |
| **SHA-256** | **`180B85957701365F09CBA145C37F284FB091364A97BCCEFC6DC87A46BB701A20`** |
| Versão do produto | 1.1.0 |
| Fabricante (ARP) | Landipay |
| Nome no ARP | Landipay PDV |
| Escopo | `perUser` |
| Arquitetura | x64 (`wix build -arch x64`) |
| Upgrade | `UpgradeCode 6F2C1C93-3B7E-4C0E-9A61-2E0D5B7A8C41` + `MajorUpgrade` |
| Payload | 110 arquivos · 79.517.265 bytes · **0 `.pdb`** |

Como reconferir o hash:

```bash
cd C:/Users/diego/landipay
sha256sum dist/Landipay-1.1.0.msi
# 180b85957701365f09cba145c37f284fb091364a97bccefc6dc87a46bb701a20
```

## 2. Como o pacote é gerado

```powershell
# a partir da raiz do repositório
powershell -File packaging/build-msi.ps1
```

O script faz quatro coisas, nesta ordem (`packaging/build-msi.ps1`):

1. **Limpa** `packaging/payload` e `dist`.
2. **Publica** os três aplicativos em Release: `Landipay.Admin` e `Landipay.Pos` → `payload/app`, `Landipay.Installer` → `payload/installer`.
3. **Remove todo `.pdb`** do payload (R5: símbolo carrega caminho de origem e nome de arquivo de fonte — não vai para o cliente). Contagem impressa pelo script: `110 arquivos (75,8 MB), N .pdb removidos`.
4. **Compila o MSI** com `wix build Package.wxs -o dist\Landipay-<versao>.msi -arch x64 -ext WixToolset.UI.wixext`.

`-arch x64` não é detalhe: sem ele o pacote sai 32 bits e o Windows registra o produto em
`WOW6432Node`. Com ele, o registro fica em `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`.
Verificado por leitura de registro (§4).

## 3. Conteúdo instalado

| Caminho | Conteúdo |
|---|---|
| `%LOCALAPPDATA%\Landipay\` | binários do Admin, do Caixa e do Instalador + `runtimes/` (110 arquivos) |
| `%APPDATA%\Landipay\landipay.db` | **dados do usuário** — fora do pacote, criado no primeiro uso |
| `%APPDATA%\Landipay\preferences.json` | preferência de tema |
| Desktop | `Landipay Admin.lnk`, `Landipay Caixa.lnk` |
| Menu Iniciar | `Landipay Admin.lnk`, `Landipay Caixa.lnk`, `Landipay — Instalador.lnk` |
| ARP | `Landipay PDV 1.1.0` (ícone `ARPPRODUCTICON` = `IconeApp`) |

## 4. Ciclo instalar → desinstalar → instalar (execução real)

Log bruto: [`_evidence/etapa7-T6-msi-ciclo.txt`](_evidence/etapa7-T6-msi-ciclo.txt) ·
MSI logs: `dist/install-gate.txt`, `dist/uninstall-gate.txt`.

**Desinstalação** (`msiexec /x {ProductCode} /qn`) → `EXITCODE=0`

```
arquivos em %LOCALAPPDATA%\Landipay: 0
landipay.db preservado: SIM
atalhos no Desktop: 0
ARP existe: False
```

**Reinstalação** (`msiexec /i dist\Landipay-1.1.0.msi /qn`) → `EXITCODE=0`

```
arquivos instalados: 110
sha256 do landipay.db antes e depois: idêntico  (dados do usuário intactos)
atalhos Desktop: 2
ARP: Landipay PDV 1.1.0
WOW6432Node tem Landipay? False        <- x64 correto (R7)
```

O `ProductCode` é gerado a cada build (`Guid="*"`); a atualização entre versões é garantida
pelo `UpgradeCode` fixo + `MajorUpgrade`, não pelo `ProductCode`. Consequência observada: um
ciclo desinstalar/instalar produz um `ProductCode` novo — por isso o ARP é consultado pelo
nome, nunca por código fixo.

Screenshot do ARP com o produto instalado: [`mockups/msi-instalado.png`](mockups/msi-instalado.png)
(captura real desta máquina em 11/11/2026, mostrando `Landipay PDV`, versão `1.1.0`, 76,0 MB).

## 5. Pontos de atenção que ficam registrados

1. **Sem Authenticode.** O MSI não é assinado: em máquina de terceiro o SmartScreen avisa na
   primeira execução. É decisão de custo (certificado), não bug — ver `07-inferencias.md`.
2. **`DisplayIcon` vazio** na linha do ARP é comportamento do registro por usuário no
   Windows 10; o `ARPPRODUCTICON` está na tabela `Property` e o ícone aparece no item.
3. **Erro 1925:** não ocorre porque o escopo é `perUser` + `%LOCALAPPDATA%`. Instalar em
   `Program Files` sem elevação é justamente o cenário que produz o 1925.
