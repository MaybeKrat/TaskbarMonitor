# Собирает TaskbarMonitor, публикует в папку $Out и перезапускает.
# Работающая копия останавливается только после успешной сборки.
param([string]$Out = 'C:\Workspace\publish\TaskbarMonitor')
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'TaskbarMonitor.csproj'
$exe = Join-Path $Out 'TaskbarMonitor.exe'

dotnet build $project -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась' }

$running = Get-Process TaskbarMonitor -ErrorAction SilentlyContinue
if ($running) {
    $running | Stop-Process -Force
    foreach ($p in $running) { [void]$p.WaitForExit(10000) }
}

dotnet publish $project -c Release --no-build -o $Out --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Публикация не удалась' }

# Запуск через WMI: программа не зависит от окна, из которого запущен скрипт
$result = Invoke-CimMethod Win32_Process -MethodName Create -Arguments @{ CommandLine = "`"$exe`""; CurrentDirectory = $Out }
if ($result.ReturnValue -ne 0) { throw "Не удалось запустить TaskbarMonitor: код $($result.ReturnValue)" }
