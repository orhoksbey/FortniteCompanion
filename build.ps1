<#
.SYNOPSIS
  Fortnite Companion — tek komutla test + Release derleme + tek dosya EXE.

.DESCRIPTION
  Gereksinim: .NET 10 SDK (https://dotnet.microsoft.com/download/dotnet/10.0, "SDK", Windows x64).
  İlk derlemede NuGet paketleri internetten indirilir (Microsoft.Data.Sqlite, xUnit vb.).

  Kullanım (PowerShell):
    .\build.ps1                 # test + EXE (dist\FortniteCompanion.exe)
    .\build.ps1 -SkipTests      # testleri atla
    .\build.ps1 -FrameworkDependent   # küçük EXE (.NET 10 Desktop Runtime kurulu olmalı)
    .\build.ps1 -UpdateRepo kullanici/FortniteCompanion   # otomatik güncelleme kaynağını göm
#>
param(
  [switch]$SkipTests,
  [switch]$FrameworkDependent,
  # Otomatik güncelleme için GitHub deposu, ör. -UpdateRepo "kullanici/FortniteCompanion"
  [string]$UpdateRepo = "",
  # Ekip sunucusu (server/KURULUM.md): -SyncUrl https://script.google.com/macros/s/.../exec -SyncKey fc-...
  [string]$SyncUrl = "",
  [string]$SyncKey = ""
)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Fail($msg) { Write-Host "`nHATA: $msg" -ForegroundColor Red; exit 1 }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  Fail ".NET SDK bulunamadı. https://dotnet.microsoft.com/download/dotnet/10.0 adresinden '.NET 10 SDK (Windows x64)' kurun ve yeniden deneyin."
}
$sdkList = @(& dotnet --list-sdks)
if (-not ($sdkList | Where-Object { $_ -match '^10\.' })) {
  Fail (".NET 10 SDK gerekli. Kurulu SDK'lar:`n" + ($sdkList -join "`n"))
}

Write-Host "==> Paketler geri yükleniyor" -ForegroundColor Cyan
dotnet restore FortniteCompanion.sln
if ($LASTEXITCODE -ne 0) { Fail "restore başarısız" }

if (-not $SkipTests) {
  Write-Host "==> Testler çalıştırılıyor" -ForegroundColor Cyan
  dotnet test tests\FortniteCompanion.Tests\FortniteCompanion.Tests.csproj -c Release --nologo
  if ($LASTEXITCODE -ne 0) { Fail "testler başarısız" }
}

Write-Host "==> Release derleniyor" -ForegroundColor Cyan
$out = Join-Path $PSScriptRoot 'dist'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

$common = @(
  'src\FortniteCompanion\FortniteCompanion.csproj',
  '-c', 'Release',
  '-r', 'win-x64',
  '-p:DebugType=none',
  '-p:PublishSingleFile=true',
  '-o', $out
)
if ($UpdateRepo) { $common += "-p:UpdateRepo=$UpdateRepo" }
if ($SyncUrl) { $common += "-p:SyncUrl=$SyncUrl" }
if ($SyncKey) { $common += "-p:SyncKey=$SyncKey" }
if ($FrameworkDependent) {
  dotnet publish @common --self-contained false
} else {
  dotnet publish @common --self-contained true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
}
if ($LASTEXITCODE -ne 0) { Fail "publish başarısız" }

Write-Host "==> ReplayScan aracı derleniyor (isteğe bağlı doğrulama aracı)" -ForegroundColor Cyan
dotnet publish tools\ReplayScan\ReplayScan.csproj -c Release -r win-x64 --self-contained false -p:DebugType=none -o (Join-Path $out 'tools')
if ($LASTEXITCODE -ne 0) { Write-Host "ReplayScan derlenemedi (uygulama etkilenmez)." -ForegroundColor Yellow }

$exe = Join-Path $out 'FortniteCompanion.exe'
if (-not (Test-Path $exe)) { Fail "EXE oluşmadı: $exe" }
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "`nTAMAM: $exe ($size MB)" -ForegroundColor Green
