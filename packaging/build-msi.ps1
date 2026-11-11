# build-msi.ps1 — gera dist\Landipay-<versao>.msi do zero, de forma reproduzível.
#
# Uso (na raiz do repositório):
#   powershell -NoProfile -ExecutionPolicy Bypass -File packaging\build-msi.ps1
#
# Requisitos: .NET SDK 8 e WiX v5 (`dotnet tool install --global wix --version 5.0.2`
# e `wix extension add -g WixToolset.UI.wixext/5.0.2`).
# NÃO use WiX v7+: ele exige aceitar o contrato de manutenção paga (OSMF).

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $raiz

$versao = '1.1.0'
$payload = Join-Path $raiz 'packaging\payload'
$dist    = Join-Path $raiz 'dist'

Write-Host "== 1/4 limpando payload e dist ==" -ForegroundColor Cyan
Remove-Item $payload -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path "$payload\app", "$payload\installer", $dist -Force | Out-Null

Write-Host "== 2/4 publicando em Release ==" -ForegroundColor Cyan
# Os dois aplicativos publicam na MESMA pasta porque compartilham Landipay.Ui/Client/Theme:
# publicar em pastas separadas duplicaria as DLLs e o MSI levaria duas cópias de cada.
dotnet publish src\Landipay.Admin -c Release -o "$payload\app" --nologo | Out-Null
dotnet publish src\Landipay.Pos   -c Release -o "$payload\app" --nologo | Out-Null
dotnet publish src\Landipay.Installer -c Release -o "$payload\installer" --nologo | Out-Null

# O .ico do executável não é copiado pelo publish (é ApplicationIcon); o MSI precisa dele
# como fonte para ARPPRODUCTICON e para os atalhos.
Copy-Item (Join-Path $raiz 'src/Landipay.Theme/Assets/app.ico') (Join-Path $payload 'app/app.ico') -Force

# .pdb carrega caminho de origem e símbolos: não vai para o cliente.
$pdbs = Get-ChildItem -Path $payload -Filter *.pdb -Recurse
$pdbs | Remove-Item -Force
Write-Host ("    binarios: {0} arquivos ({1:N1} MB), {2} .pdb removidos" -f `
    (Get-ChildItem $payload -Recurse -File).Count,
    ((Get-ChildItem $payload -Recurse -File | Measure-Object Length -Sum).Sum / 1MB),
    $pdbs.Count)

Write-Host "== 3/4 compilando o MSI (WiX 5) ==" -ForegroundColor Cyan
Push-Location (Join-Path $raiz 'packaging')
try {
    # O RTF da licenca e o payload sao resolvidos a partir DESTA pasta.
    # -arch x64 é obrigatório: sem ele o pacote sai 32 bits e o registro em
    # "Aplicativos instalados" cai em WOW6432Node.
    wix build Package.wxs -o (Join-Path $dist "Landipay-$versao.msi") -arch x64 -ext WixToolset.UI.wixext
    if ($LASTEXITCODE -ne 0) { throw "wix build falhou (exit $LASTEXITCODE)" }
}
finally { Pop-Location }

$msi = Join-Path $dist "Landipay-$versao.msi"
$info = Get-Item $msi
$hash = (Get-FileHash $msi -Algorithm SHA256).Hash

Write-Host "== 4/4 pronto ==" -ForegroundColor Green
Write-Host ("    {0}" -f $msi)
Write-Host ("    tamanho: {0:N1} MB" -f ($info.Length / 1MB))
Write-Host ("    SHA-256: {0}" -f $hash)

Write-Host ""
Write-Host "Instalar (por usuario, sem administrador):" -ForegroundColor Yellow
Write-Host "    msiexec /i `"$msi`""
Write-Host "Silencioso:"
Write-Host "    msiexec /i `"$msi`" /qn /l*v `"$dist\install.log`""
Write-Host "Desinstalar:"
Write-Host "    msiexec /x `"$msi`" /qn"
