# 於「每台 client」安裝內網 CA 憑證 —— 裝一次，之後連本系統 HTTPS 永久免警告
#
# 前置：把 ScadaEngine-CA.crt 與本腳本一起複製到該 client 的同一資料夾
# 執行：以「系統管理員」開 PowerShell 跑  ./install-ca-on-client.ps1
#       （寫入本機信任的根憑證存放區需要系統管理員權限）
#
# 只裝「公開」的 CA 憑證（.crt），不含任何私鑰 —— 這不會讓 client 能冒充別人，
# 僅代表「這台電腦信任由本內網 CA 簽發的憑證」。
#
# 移除：於系統管理員 PowerShell 執行
#   Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -like '*ScadaEngine Internal CA*' } | Remove-Item

$ErrorActionPreference = "Stop"

$szCrt = Join-Path $PSScriptRoot "ScadaEngine-CA.crt"
if (-not (Test-Path $szCrt)) {
    throw "找不到 ScadaEngine-CA.crt（應與本腳本放在同一資料夾）"
}

# 確認具系統管理員權限
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "請以『系統管理員』身分執行 PowerShell 再跑本腳本"
}

$cert = Import-Certificate -FilePath $szCrt -CertStoreLocation "Cert:\LocalMachine\Root"

Write-Host "已安裝內網 CA 至『受信任的根憑證授權單位』"
Write-Host "  主體：$($cert.Subject)"
Write-Host "  指紋：$($cert.Thumbprint)"
Write-Host "請『完全關閉並重開瀏覽器』後，以 https://<伺服器IP>:7189 連線，應不再跳警告。"
