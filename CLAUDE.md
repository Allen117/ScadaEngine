# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

始終用繁體中文說明

依照 馬斯克 第一性原理

## Project Overview

**Grimo** is a Kotlin Multiplatform (KMP) desktop AI assistant 
application. Currently targeting macOS with 
architecture ready for Android/iOS expansion.

An AI-powered task management and orchestration platform 
designed to coordinate AI CLI tools...

## Technology Stack

## Important Notes

- Always use SQLDelight's type-safe queries instead of raw SQL
- Keep business logic in the `shared` module
- Platform-specific code only in respective source sets
- Follow MVI pattern for state management


## Build & Run Commands

```bash
# Run Engine (Modbus collector + MQTT publisher) — from repo root
cd ScadaEngine.Engine && dotnet run

# Run Web (ASP.NET Core dashboard) — from repo root
cd ScadaEngine.Web && dotnet run
# Or use the convenience script:
啟動登入頁.bat        # opens browser + starts Web

# Build entire solution
dotnet build ScadaEngine.sln

# Kill a stuck Web process on Windows
powershell.exe -Command "Get-Process -Name 'ScadaEngine.Web' | Stop-Process -Force"
```

Web runs on **http://localhost:5038** (HTTP) / **https://localhost:7189** (HTTPS).
Engine has no HTTP endpoint — it is a Windows Service / Console background process.

> **Important**: Razor views are precompiled. Any `.cshtml` change requires a **rebuild** (`dotnet build`) to take effect.

---

## Solution Architecture

```
ScadaEngine.sln
├── ScadaEngine.Common     — Shared models & DB config service (class library)
├── ScadaEngine.Algorithm  — Algorithm utilities (class library, currently minimal)
├── ScadaEngine.Engine     — .NET 8 Worker Service (Modbus → MQTT publisher)
└── ScadaEngine.Web        — .NET 8 ASP.NET Core MVC (dashboard, http://localhost:5038)
```

### Data Flow

```
Modbus TCP Devices
    ↓ FC01/02/03/04 polling (FluentModbus)
ScadaEngine.Engine / ModbusCommunicationService
    ↓ raw value × Ratio → engineering value, quality = Good/Bad
MqttPublishService  (Topic: SCADA/Realtime/{coordinatorName}/{SID}, QoS=1, Retain=true)
    ↓
MQTT Broker (127.0.0.1:1883, no auth)
    ↓ subscribe SCADA/Realtime/+/+
ScadaEngine.Web / MqttRealtimeSubscriberService
    ↓ ConcurrentDictionary<SID, RealtimeDataItemModel>
RealtimeController → /RealTime page
```

Engine also writes to SQL Server: `HistoryData` (time-series) and `LatestData` (upsert).

---

## Key Configuration Files

| File | Purpose |
|------|---------|
| `ScadaEngine.Engine/Setting/dbSetting.json` | SQL Server connection (host, DB, user, pass) |
| `ScadaEngine.Engine/MqttSetting/MqttSetting.json` | MQTT broker IP/port/topic/retain |
| `ScadaEngine.Engine/Modbus/Modbus.json` | Modbus device definitions (IP, port, tags) |
| `ScadaEngine.Engine/DatabaseSchema/DatabaseSchema.json` | DB table schema for auto-init |

Web reads Engine's `dbSetting.json` via a relative path `../ScadaEngine.Engine/Setting/dbSetting.json` — both projects must run from their own directories.

---

## Database Schema (SQL Server: `wsnCsharp`)

| Table | Key Columns | Purpose |
|-------|-------------|---------|
| `ModbusCoordinator` | Id, Name, ModbusID, DelayTime, MonitorEnabled | Device registry (sidebar source) |
| `ModbusPoints` | SID (PK), Name, Address, DataType, Ratio, Unit | Point configuration |
| `HistoryData` | SID+Timestamp (PK), Value, Quality | Time-series history |
| `LatestData` | SID (PK), Value, Timestamp, Quality | Last known value per point |
| `Users` | UserID, Username, PasswordHash, Role, IsActive | Web login (SHA256 hex password) |

SID format: `{ModbusID}-S{SequenceNumber}` e.g. `196865-S1`.

---

## Web Project Structure

The Web project uses a **Features** folder layout alongside the conventional `Views/` folder:

```
ScadaEngine.Web/
├── Features/
│   ├── _ViewImports.cshtml          ← MUST exist for Tag Helpers to work in Features/
│   ├── Login/
│   │   ├── Controllers/LoginController.cs
│   │   ├── Models/LoginModel.cs
│   │   └── Views/Index.cshtml
│   └── Realtime/
│       ├── Controllers/RealtimeController.cs
│       ├── Models/RealtimeMonitorViewModel.cs
│       └── Views/Index.cshtml
├── Views/
│   ├── _ViewImports.cshtml          ← Only applies to Views/ subdirectory
│   └── Shared/_Layout.cshtml
├── Services/
│   ├── MqttRealtimeSubscriberService.cs   ← Singleton BackgroundService, MQTT subscriber
│   └── WebDatabaseService.cs
└── Program.cs
```

**Critical**: `_ViewImports.cshtml` in `Views/` does NOT apply to `Features/` views. The `Features/_ViewImports.cshtml` file is required for Tag Helpers (`asp-for`, `asp-action`, etc.) to work in Feature views.

View discovery is configured in `Program.cs` to look in both `/Views/{1}/{0}.cshtml` and `/Features/{1}/Views/{0}.cshtml`.

---

## Authentication

- Cookie-based auth (`ScadaAuth` cookie, 4-hour expiry with sliding)
- Login at `/Login` → on success redirects to `/RealTime`
- Root `/` redirects to `/Login`
- Logout: POST to `/Login/Logout` (requires AntiForgeryToken — use a hidden form, not `<a href>`)
- Default credentials when `Users` table is **empty**: `ITRI / ITRI` (plain text comparison)
- When `Users` has rows: password validated as `SHA256(plaintext).ToLower()` compared against `PasswordHash` column

---

## Naming Conventions

This codebase uses Hungarian notation throughout:

| Prefix | Type | Example |
|--------|------|---------|
| `sz` | string | `szName`, `szBrokerIp` |
| `n` | int | `nPort`, `nTotalPoints` |
| `f` | float | `fValue`, `fRatio` |
| `d` | double | `dValue` |
| `dt` | DateTime | `dtTimestamp`, `dtLastUpdated` |
| `is` | bool | `isConnected`, `isMonitorEnabled` |
| `_` prefix | private field | `_logger`, `_mqttClient` |

---

## Key Patterns & Pitfalls

### Dapper Column Mapping
`CoordinatorModel` and other models use Hungarian property names (`szName`, `szModbusID`) that don't match DB column names (`Name`, `ModbusID`). Dapper maps by property name by default — the `[Column]` attribute is NOT used. Always use SQL aliases:
```sql
SELECT Name AS szName, ModbusID AS szModbusID, ...
FROM ModbusCoordinator
```

### MQTT JSON Parsing
The Web subscriber uses case-insensitive dictionary parsing to handle PascalCase/camelCase variations in the payload:
```csharp
var props = jsonDoc.RootElement.EnumerateObject()
    .ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
```

### MqttRealtimeSubscriberService
Registered as both `AddSingleton` and `AddHostedService` so it can be injected into controllers by type and also run as a background service. Pre-fills cache from `ModbusPoints` table with `hasData=false` placeholders on startup so all configured points appear in the UI even before MQTT data arrives.

### MQTT Retain Flag
Engine publishes with `Retain=true`. When restarting Engine, old retained messages (without `name` field) remain on the broker. A full restart of both Engine and broker clears stale retained messages.

### IDataRepository (Scoped)
Defined in `ScadaEngine.Engine` but used by both Engine and Web. Web registers `SqlServerDataRepository` as Scoped. `MqttRealtimeSubscriberService` (Singleton) accesses it via `IServiceProvider.CreateScope()`.
