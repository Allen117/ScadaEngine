<#
.SYNOPSIS
    ScadaEngine 資料庫一次性安裝腳本（idempotent，可重複執行）。

.DESCRIPTION
    以「執行者的 Windows 帳號」透過 Integrated Security 連線 SQL Server（安裝 SQL Server 時
    安裝者帳號預設即為 sysadmin），全程不需 sa 密碼。執行內容：

      1. 建立資料夾 C:\Scada\Database、C:\Scada\Backup（路徑讀 DbMaintenanceSetting.json）
         並授予 SQL Server 服務帳號寫入權限（CREATE DATABASE / BACKUP 的檔案 I/O 由 SQL 服務進程執行）
      2. 資料庫不存在 → 建立於指定路徑 + 設 RECOVERY SIMPLE
         資料庫已存在 → 跳過建立；若復原模式為 FULL 印出警告（不自動更改既有 DB 狀態）
      3. 建立應用程式 SQL login（讀 dbSetting.json 的帳密）並授予目標 DB 的 db_owner

    既有環境（DB 已手動建立）也應執行一次本腳本，以補齊備份資料夾與服務帳號權限。

.NOTES
    需以「系統管理員身分」執行，且執行者的 Windows 帳號需為 SQL Server sysadmin。
    參數不給時自動讀取同資料夾的 dbSetting.json / DbMaintenanceSetting.json。
#>
[CmdletBinding()]
param(
    [string]$ServerInstance,   # 例: "localhost" 或 "localhost\SQLEXPRESS"，預設讀 dbSetting.json 的 DatabaseAddress
    [string]$DatabaseName,     # 預設讀 dbSetting.json 的 DataBaseName
    [string]$AppLogin,         # 預設讀 dbSetting.json 的 DataBaseAccount
    [string]$AppPassword,      # 預設讀 dbSetting.json 的 DataBasePassword
    [string]$DataFolder,       # 預設讀 DbMaintenanceSetting.json 的 DataFileFolder
    [string]$BackupFolder      # 預設讀 DbMaintenanceSetting.json 的 BackupFolder
)

$ErrorActionPreference = 'Stop'

function Read-JsonFile([string]$path) {
    if (Test-Path $path) { return (Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json) }
    return $null
}

# ─── 1. 讀取設定檔補齊參數 ───────────────────────────────────────────────
$dbSetting    = Read-JsonFile (Join-Path $PSScriptRoot 'dbSetting.json')
$maintSetting = Read-JsonFile (Join-Path $PSScriptRoot 'DbMaintenanceSetting.json')

if (-not $ServerInstance) { $ServerInstance = if ($dbSetting) { $dbSetting.DatabaseAddress } else { 'localhost' } }
if (-not $DatabaseName)   { $DatabaseName   = if ($dbSetting) { $dbSetting.DataBaseName }   else { 'wsnCsharp' } }
if (-not $AppLogin)       { $AppLogin       = if ($dbSetting) { $dbSetting.DataBaseAccount } else { 'wsn' } }
if (-not $AppPassword)    { $AppPassword    = if ($dbSetting) { $dbSetting.DataBasePassword } else { '' } }
if (-not $DataFolder)     { $DataFolder     = if ($maintSetting) { $maintSetting.DataFileFolder } else { 'C:\Scada\Database' } }
if (-not $BackupFolder)   { $BackupFolder   = if ($maintSetting) { $maintSetting.BackupFolder }   else { 'C:\Scada\Backup' } }

Write-Host "=== ScadaEngine 資料庫安裝 ===" -ForegroundColor Cyan
Write-Host "SQL Server : $ServerInstance"
Write-Host "資料庫     : $DatabaseName"
Write-Host "應用帳號   : $AppLogin"
Write-Host "資料檔路徑 : $DataFolder"
Write-Host "備份路徑   : $BackupFolder"
Write-Host ""

# ─── 2. 偵測 SQL Server 服務帳號並授權資料夾 ────────────────────────────
# 預設實例服務名 MSSQLSERVER；具名實例（如 SQLEXPRESS）為 MSSQL$SQLEXPRESS
$instanceName = $null
if ($ServerInstance -match '\\(.+)$') { $instanceName = $Matches[1] }
$svcName = if ($instanceName) { "MSSQL`$$instanceName" } else { 'MSSQLSERVER' }

$svc = Get-CimInstance Win32_Service -Filter "Name='$($svcName.Replace("'","''"))'" -ErrorAction SilentlyContinue
if (-not $svc) {
    # 指定服務名找不到時，掃描本機所有 SQL 實例服務
    $candidates = @(Get-CimInstance Win32_Service | Where-Object { $_.Name -eq 'MSSQLSERVER' -or $_.Name -like 'MSSQL$*' })
    if ($candidates.Count -eq 1) {
        $svc = $candidates[0]
        Write-Host "找不到服務 $svcName，自動改用本機唯一 SQL 實例服務: $($svc.Name)" -ForegroundColor Yellow
    }
}

if ($svc) {
    $svcAccount = $svc.StartName   # 例: NT Service\MSSQLSERVER
    Write-Host "SQL 服務帳號: $svcAccount ($($svc.Name))"

    foreach ($folder in @($DataFolder, $BackupFolder)) {
        if (-not (Test-Path $folder)) {
            New-Item -ItemType Directory -Force -Path $folder | Out-Null
            Write-Host "已建立資料夾: $folder" -ForegroundColor Green
        } else {
            Write-Host "資料夾已存在: $folder"
        }
        # (OI)(CI)M = 資料夾 + 子檔案繼承的「修改」權限
        icacls $folder /grant "${svcAccount}:(OI)(CI)M" | Out-Null
        Write-Host "已授予 $svcAccount 寫入權限: $folder" -ForegroundColor Green
    }
} else {
    Write-Host "警告: 本機找不到 SQL Server 服務（$svcName）— 若 SQL Server 在遠端主機，資料夾與權限請於該主機自行建立" -ForegroundColor Yellow
}

# ─── 3. SSPI 連線 master ────────────────────────────────────────────────
# 整合式驗證改走本機實例名（shared memory）：位址是 IP 時 SSPI 查不到 SPN 退 NTLM，
# 工作群組環境會報「登入來自未信任的網域」。服務名格式為安裝程式強制規格
# （預設實例=MSSQLSERVER、具名=MSSQL$實例名），反推實例名是確定性的。
# SQL 驗證（應用帳號）不經此協商，dbSetting.json 的 IP 位址不需要改。
$sspiTarget = $ServerInstance
$addressPart = ($ServerInstance -split '\\')[0]
$isLocalAddress = $addressPart -in @('127.0.0.1', 'localhost', '.', $env:COMPUTERNAME)
if ($svc -and $isLocalAddress) {
    if ($svc.Name -eq 'MSSQLSERVER') { $sspiTarget = '.' }
    else { $sspiTarget = '.\' + $svc.Name.Substring(6) }   # MSSQL$XYZ → .\XYZ
}
Write-Host "SSPI 連線目標: $sspiTarget"
$conn = New-Object System.Data.SqlClient.SqlConnection(
    "Server=$sspiTarget;Database=master;Integrated Security=SSPI;TrustServerCertificate=true")
$conn.Open()

function Invoke-Sql([string]$sql) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 300
    [void]$cmd.ExecuteNonQuery()
}
function Get-SqlScalar([string]$sql) {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 60
    return $cmd.ExecuteScalar()
}

try {
    $safeDb   = $DatabaseName.Replace(']', ']]')
    $quotedDb = $DatabaseName.Replace("'", "''")

    # ─── 4. 建立資料庫（不存在時）────────────────────────────────────────
    $dbId = Get-SqlScalar "SELECT DB_ID(N'$quotedDb')"
    if ($dbId -is [System.DBNull]) {
        $mdf = (Join-Path $DataFolder "$DatabaseName.mdf").Replace("'", "''")
        $ldf = (Join-Path $DataFolder ($DatabaseName + '_log.ldf')).Replace("'", "''")
        Invoke-Sql "CREATE DATABASE [$safeDb] ON (NAME = N'$safeDb', FILENAME = N'$mdf') LOG ON (NAME = N'${safeDb}_log', FILENAME = N'$ldf')"
        Invoke-Sql "ALTER DATABASE [$safeDb] SET RECOVERY SIMPLE"
        Write-Host "已建立資料庫 $DatabaseName（RECOVERY SIMPLE，路徑: $DataFolder）" -ForegroundColor Green
    } else {
        Write-Host "資料庫 $DatabaseName 已存在，跳過建立"
        $recovery = Get-SqlScalar "SELECT recovery_model_desc FROM sys.databases WHERE name = N'$quotedDb'"
        if ($recovery -eq 'FULL') {
            Write-Host "警告: 既有資料庫復原模式為 FULL — 本系統僅做每週完整備份、不做交易記錄備份，FULL 模式下交易記錄檔將持續成長。" -ForegroundColor Yellow
            Write-Host "      建議由 DBA 評估後手動切換: ALTER DATABASE [$safeDb] SET RECOVERY SIMPLE" -ForegroundColor Yellow
        } else {
            Write-Host "復原模式: $recovery"
        }
    }

    # ─── 5. 建立應用程式 login / user / db_owner ─────────────────────────
    if (-not $AppPassword) {
        Write-Host "警告: 未提供應用帳號密碼（dbSetting.json 無值），跳過 login 建立" -ForegroundColor Yellow
    } else {
        $safeLogin   = $AppLogin.Replace(']', ']]')
        $quotedLogin = $AppLogin.Replace("'", "''")
        $quotedPwd   = $AppPassword.Replace("'", "''")

        $loginExists = Get-SqlScalar "SELECT COUNT(*) FROM sys.server_principals WHERE name = N'$quotedLogin'"
        if ([int]$loginExists -eq 0) {
            Invoke-Sql "CREATE LOGIN [$safeLogin] WITH PASSWORD = N'$quotedPwd', CHECK_POLICY = OFF, DEFAULT_DATABASE = [$safeDb]"
            Write-Host "已建立 SQL login: $AppLogin" -ForegroundColor Green
        } else {
            Write-Host "SQL login $AppLogin 已存在，跳過建立"
        }

        Invoke-Sql @"
USE [$safeDb];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$quotedLogin')
    CREATE USER [$safeLogin] FOR LOGIN [$safeLogin];
ALTER ROLE db_owner ADD MEMBER [$safeLogin];
"@
        Write-Host "已確認 $AppLogin 為 $DatabaseName 的 db_owner" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "=== 安裝完成 ===" -ForegroundColor Cyan
    Write-Host "後續: 啟動 ScadaEngine.Engine 會自動建表與補欄位；每週備份依 DbMaintenanceSetting.json 排程執行。"
}
finally {
    $conn.Close()
}
