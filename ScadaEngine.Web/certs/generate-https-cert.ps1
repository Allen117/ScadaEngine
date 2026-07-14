# 產生內網 HTTPS 憑證（CA 簽發架構 — 方案 B：client 裝一次 CA 後永久免警告）
#
# 產物：
#   ca.pfx              內網 CA 的私鑰（機密，留在本機，用來簽 / 重簽伺服器憑證，不進 git）
#   ScadaEngine-CA.crt  內網 CA 的「公開」憑證 —— 要發給每台 client 安裝（見 install-ca-on-client.ps1）
#   scada-web.pfx       伺服器憑證（由 CA 簽發，含本機 IP，Kestrel 載入，不進 git）
#
# 首次執行：自動建立 CA + 伺服器憑證。之後把 ScadaEngine-CA.crt 發到每台 client 裝一次。
# IP 變了重跑：CA 不變（沿用既有 ca.pfx），只重簽 scada-web.pfx —— client 不必重裝 CA。
# 想重建 CA：刪掉 ca.pfx 再跑（但所有 client 都要重裝新的 ScadaEngine-CA.crt）。
#
# 執行： 於本資料夾開 PowerShell 跑  ./generate-https-cert.ps1   （無需系統管理員）

param(
    [string]$ServerPassword = "ScadaWeb",   # 須與 Program.cs 內載入 scada-web.pfx 的密碼一致
    [string]$CaPassword     = "ScadaCA"
)

$ErrorActionPreference = "Stop"

$szCaPfx    = Join-Path $PSScriptRoot "ca.pfx"
$szCaCer    = Join-Path $PSScriptRoot "ScadaEngine-CA.crt"
$szSrvPfx   = Join-Path $PSScriptRoot "scada-web.pfx"
$store      = "Cert:\CurrentUser\My"
$caSecure   = ConvertTo-SecureString -String $CaPassword     -Force -AsPlainText
$srvSecure  = ConvertTo-SecureString -String $ServerPassword -Force -AsPlainText

# 自動偵測本機 LAN IPv4（排除 loopback 與 APIPA 169.254.*）
$szIp = Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -notlike '169.*' -and $_.IPAddress -ne '127.0.0.1' -and $_.PrefixOrigin -ne 'WellKnown' } |
    Select-Object -First 1 -ExpandProperty IPAddress
if (-not $szIp) { throw "找不到本機 LAN IPv4 位址" }
Write-Host "偵測到本機 IP：$szIp"

# ── 1. 取得 / 建立內網 CA ──────────────────────────────────────────────
if (Test-Path $szCaPfx) {
    Write-Host "沿用既有 CA（ca.pfx）"
    $ca = Import-PfxCertificate -FilePath $szCaPfx -CertStoreLocation $store -Password $caSecure -Exportable
} else {
    Write-Host "首次執行：建立內網 CA"
    $ca = New-SelfSignedCertificate `
        -Subject "CN=ScadaEngine Internal CA, O=ScadaEngine, C=TW" `
        -KeyUsage CertSign, CRLSign `
        -KeyLength 2048 -KeyAlgorithm RSA -HashAlgorithm SHA256 `
        -NotAfter (Get-Date).AddYears(10) `
        -CertStoreLocation $store `
        -KeyExportPolicy Exportable `
        -TextExtension @("2.5.29.19={text}CA=true&pathlength=0")

    # 匯出 CA 私鑰（機密）與公開憑證（發給 client）
    Export-PfxCertificate  -Cert $ca -FilePath $szCaPfx -Password $caSecure | Out-Null
    Export-Certificate     -Cert $ca -FilePath $szCaCer -Type CERT          | Out-Null
    Write-Host "已建立 CA：$szCaCer （請發給每台 client 安裝）"
}

# ── 2. 用 CA 簽發伺服器憑證（含本機 IP 的 SAN，供 client 驗證） ─────────
$server = New-SelfSignedCertificate `
    -Subject "CN=ScadaEngine Web" `
    -Signer $ca `
    -KeyLength 2048 -KeyAlgorithm RSA -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddYears(5) `
    -CertStoreLocation $store `
    -KeyExportPolicy Exportable `
    -TextExtension @(
        "2.5.29.37={text}1.3.6.1.5.5.7.3.1",                                  # EKU: serverAuth
        "2.5.29.17={text}DNS=localhost&IPAddress=127.0.0.1&IPAddress=$szIp"    # SAN: localhost / 127.0.0.1 / 本機 IP
    )

Export-PfxCertificate -Cert $server -FilePath $szSrvPfx -Password $srvSecure -ChainOption BuildChain | Out-Null

# ── 3. 清掉暫存於個人存放區的憑證（私鑰已存進 pfx 檔） ────────────────
Remove-Item -Path ("Cert:\CurrentUser\My\" + $server.Thumbprint) -Force
Remove-Item -Path ("Cert:\CurrentUser\My\" + $ca.Thumbprint)     -Force

Write-Host ""
Write-Host "完成 ──────────────────────────────────"
Write-Host "伺服器憑證：$szSrvPfx （SAN: localhost, 127.0.0.1, $szIp）"
Write-Host "CA 公開憑證：$szCaCer"
Write-Host "下一步：把 ScadaEngine-CA.crt 複製到每台 client，執行 install-ca-on-client.ps1（系統管理員）"
