# 安裝「小咩 / YangMieh」程式碼簽章憑證為受信任 → CyberPaste.exe 會顯示為「已驗證的發行者」。
# 請以「系統管理員」身分執行:對著本檔右鍵 → 以 PowerShell 執行(或在系統管理員 PowerShell 跑)。
#
# Install the "YangMieh / 小咩" code-signing certificate as trusted so CyberPaste.exe
# shows as a verified publisher. Run as Administrator.
#
# 注意/Note: 只在你信任的自有電腦上安裝(安裝根憑證是安全相關動作)。
#            Only install on machines you own and trust; installing a root certificate is a security-relevant action.

$ErrorActionPreference = "Stop"
$cer = Join-Path $PSScriptRoot "CyberPaste-YangMieh.cer"
if (-not (Test-Path $cer)) { Write-Host "找不到 CyberPaste-YangMieh.cer" -ForegroundColor Red; exit 1 }

try {
    Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\LocalMachine\TrustedPublisher" | Out-Null
    Write-Host "已安裝憑證信任。CyberPaste 現在會顯示發行者:小咩 / YangMieh (已驗證)。" -ForegroundColor Green
    Write-Host "Installed. CyberPaste will now show publisher: YangMieh / 小咩 (verified)." -ForegroundColor Green
} catch {
    Write-Host "安裝失敗(是否忘了用系統管理員執行?): $_" -ForegroundColor Red
    exit 1
}
