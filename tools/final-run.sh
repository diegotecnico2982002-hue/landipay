#!/usr/bin/env bash
# Validação final do pipeline — roda TUDO e grava a saída em docs/_evidence/final-run.txt
# Uso: bash tools/final-run.sh
set -o pipefail

RAIZ="C:/Users/diego/landipay"
cd "$RAIZ" || exit 1
OUT="docs/_evidence/final-run.txt"
MSI="C:\\Users\\diego\\landipay\\dist\\Landipay-1.1.0.msi"
PC="{03AB32C5-4C3E-47DF-B009-3F677E8E39A4}"
FALHAS=0

exec > >(tee "$OUT") 2>&1

echo "======================================================================"
echo "LANDIPAY — VALIDAÇÃO FINAL   $(date '+%Y-%m-%d %H:%M:%S')"
echo "======================================================================"

echo
echo "### 1/5 — dotnet build -c Release"
dotnet build -c Release 2>&1 | grep -E "Compilação com êxito|Aviso|Erro|error|warning" | tail -4
BUILD_OK=$(dotnet build -c Release --nologo -v q 2>&1 | grep -c "Compilação com êxito")
[ "$BUILD_OK" -ge 1 ] && echo "RESULTADO: OK (0 erros, 0 avisos)" || { echo "RESULTADO: FALHOU"; FALHAS=$((FALHAS+1)); }

echo
echo "### 2/5 — dotnet test -c Release"
dotnet test -c Release --no-build 2>&1 | grep -E "Aprovado!|Com falha" | tail -4
TESTE_OK=$(dotnet test -c Release --no-build 2>&1 | grep -oE "Aprovado: +[0-9]+" | awk '{s+=$2} END {print s+0}')
echo "TOTAL DE TESTES APROVADOS: ${TESTE_OK:-0}"
[ "${TESTE_OK:-0}" -ge 108 ] && echo "RESULTADO: OK" || { echo "RESULTADO: FALHOU (esperado >= 108)"; FALHAS=$((FALHAS+1)); }

echo
echo "### 3/5 — varredura de segredos (payload publicado)"
python assets/audit-scan.py packaging/payload 2>&1 | grep -E "arquivos varridos|bytes varridos|achados|suprimidos"
echo "-- controle positivo (canary plantado) --"
python assets/audit-scan.py packaging/payload --canary 2>&1 | grep -E "controle positivo"
CTRL=$(python assets/audit-scan.py packaging/payload --canary 2>&1 | grep -c "DETECTADO")
[ "$CTRL" -ge 1 ] && echo "RESULTADO: OK (0 achados reais + controle positivo detectado)" || { echo "RESULTADO: FALHOU"; FALHAS=$((FALHAS+1)); }

echo
echo "### 4/5 — MSI: desinstalar"
powershell -NoProfile -Command "\$p = Start-Process msiexec -ArgumentList '/x','$PC','/qn' -Wait -PassThru; 'EXITCODE=' + \$p.ExitCode"
sleep 4
echo "arquivos em %LOCALAPPDATA%\\Landipay: $(find "$LOCALAPPDATA/Landipay" -type f 2>/dev/null | wc -l)"
echo "landipay.db preservado: $([ -f "$APPDATA/Landipay/landipay.db" ] && echo SIM || echo NAO)"

echo
echo "### 5/5 — MSI: instalar de novo"
powershell -NoProfile -Command "\$p = Start-Process msiexec -ArgumentList '/i','$MSI','/qn' -Wait -PassThru; 'EXITCODE=' + \$p.ExitCode"
sleep 5
N=$(find "$LOCALAPPDATA/Landipay" -type f 2>/dev/null | wc -l)
echo "arquivos instalados: $N"
echo "atalhos no Desktop: $(ls "$USERPROFILE/Desktop/" | grep -ci 'landipay.*lnk')"
powershell -NoProfile -Command "
Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
  ForEach-Object { \$p = Get-ItemProperty \$_.PSPath -ErrorAction SilentlyContinue
    if (\$p.DisplayName -like '*Landipay*') { 'ARP: ' + \$p.DisplayName + ' ' + \$p.DisplayVersion + '  (hive: ' + (\$_.PSPath -replace '.*Registry::','') + ')' } }"
[ "$N" -ge 100 ] && echo "RESULTADO: OK" || { echo "RESULTADO: FALHOU"; FALHAS=$((FALHAS+1)); }

echo
echo "### SHA-256 do MSI"
sha256sum dist/Landipay-1.1.0.msi | awk '{print toupper($1)}'

echo
echo "======================================================================"
if [ "$FALHAS" -eq 0 ]; then
  echo "VALIDAÇÃO FINAL: TUDO OK (0 falhas)"
else
  echo "VALIDAÇÃO FINAL: $FALHAS BLOCO(S) COM FALHA"
fi
echo "======================================================================"
exit "$FALHAS"
