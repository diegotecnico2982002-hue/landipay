<#
  emitir-licenca.ps1 — emite um código de licença para a empresa que já existe no banco local.

  Por que existe: a licença é assinada para o ID (GUID) da empresa, e esse ID só nasce quando
  o instalador cria a empresa no passo 3. Então o fluxo real é: rodar o instalador até o passo 3,
  depois emitir o código aqui e colar no passo 4.

  Uso:
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\emitir-licenca.ps1
    powershell ... -File tools\emitir-licenca.ps1 -InstalacaoId "loja-centro-01" -Plano pro
    powershell ... -File tools\emitir-licenca.ps1 -Api http://127.0.0.1:5082 -AdminKey "<chave>"

  Requer: backend no ar e sqlite3 no PATH (ou ajuste $sqlite).
#>
param(
    [string]$Api = 'http://127.0.0.1:5082',
    [string]$AdminKey = $env:LANDIPAY_ADMIN_KEY,
    [string]$Banco = (Join-Path $env:APPDATA 'Landipay\landipay.db'),
    [string]$InstalacaoId = 'landipay-loja01',
    [string]$Plano = 'pro',
    [string]$sqlite = 'C:\Users\diego\platform-tools\sqlite3.exe'
)

$ErrorActionPreference = 'Stop'

if (-not $AdminKey) { throw 'Informe a chave administrativa: -AdminKey ou variável LANDIPAY_ADMIN_KEY.' }
if (-not (Test-Path $Banco)) { throw "Banco não encontrado: $Banco" }
if (-not (Test-Path $sqlite)) { throw "sqlite3 não encontrado em $sqlite (ajuste -sqlite)." }

# 1. Empresa instalada (a primeira do banco — o instalador trabalha com uma empresa por instalação)
$empresaId = & $sqlite $Banco "select Id from Empresas limit 1;"
if (-not $empresaId) { throw 'Nenhuma empresa no banco: rode o instalador até o passo 3 antes.' }
$empresaId = $empresaId.Trim()

Write-Host "Empresa : $empresaId"
Write-Host "API     : $Api"

# 2. Emitir
$corpo = @{ empresaId = $empresaId; instalacaoId = $InstalacaoId; plano = $Plano } | ConvertTo-Json
$resposta = Invoke-RestMethod -Method Post -Uri "$Api/v1/licencas" -ContentType 'application/json' `
    -Headers @{ 'X-Admin-Key' = $AdminKey } -Body $corpo

Write-Host "Expira  : $($resposta.expiraEm)"
Write-Host ''
Write-Host 'Cole este código no passo 4 do instalador:' -ForegroundColor Yellow
Write-Host $resposta.token
Write-Host ''
Write-Host 'Atalho: para já abrir o instalador com o código preenchido, use a variável LANDIPAY_LICENCA.'
