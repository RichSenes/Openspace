<#
Starts, stops and restarts everything Openspace needs on Windows: the API, the web page
and the OpenCode server, each in the background with its log in the state folder.
The same commands as scripts/openspace on Linux and macOS.

  scripts\openspace.ps1 start            start what is not running yet
  scripts\openspace.ps1 restart api      after an update: rebuild and restart the API only
  scripts\openspace.ps1 stop
  scripts\openspace.ps1 status
  scripts\openspace.ps1 logs api         follow a log

Ports and folders change with environment variables, e.g. OPENSPACE_API_PORT. Anything
else in the environment (ConnectionStrings__Openspace, Settings__Path...) reaches the API.
If PowerShell refuses to run scripts: powershell -ExecutionPolicy Bypass -File scripts\openspace.ps1 start
#>
param(
    [Parameter(Position = 0)][string]$Command = 'help',
    [Parameter(Position = 1, ValueFromRemainingArguments = $true)][string[]]$Services = @()
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$ApiDir = Join-Path $Root 'src\backend\Openspace.Api'
$WebDir = Join-Path $Root 'src\frontend'
$Ports = @{
    api      = if ($env:OPENSPACE_API_PORT) { [int]$env:OPENSPACE_API_PORT } else { 5145 }
    web      = if ($env:OPENSPACE_WEB_PORT) { [int]$env:OPENSPACE_WEB_PORT } else { 5173 }
    opencode = if ($env:OPENSPACE_OPENCODE_PORT) { [int]$env:OPENSPACE_OPENCODE_PORT } else { 4096 }
}
$State = if ($env:OPENSPACE_STATE) { $env:OPENSPACE_STATE } else { Join-Path $env:LOCALAPPDATA 'openspace' }
$Logs = Join-Path $State 'logs'
$All = @('opencode', 'api', 'web')
New-Item -ItemType Directory -Force -Path $Logs | Out-Null

function Say([string]$Text) { Write-Host $Text }
function Fail([string]$Text) { Write-Host "openspace: $Text" -ForegroundColor Red; exit 1 }

function Pick([string[]]$Names) {
    if (-not $Names -or $Names.Count -eq 0) { return $All }
    foreach ($n in $Names) { if ($All -notcontains $n) { Fail "unknown service '$n': use api, web or opencode" } }
    return $Names
}

function Listening([int]$Port) {
    $client = New-Object System.Net.Sockets.TcpClient
    try { $client.Connect('127.0.0.1', $Port); return $true } catch { return $false } finally { $client.Dispose() }
}

function Ready([string]$Name) {
    $url = switch ($Name) {
        'api' { "http://localhost:$($Ports.api)/health" }
        'opencode' { "http://127.0.0.1:$($Ports.opencode)/config" }
        default { $null }
    }
    if (-not $url) { return (Listening $Ports.web) }
    try { Invoke-WebRequest -UseBasicParsing -TimeoutSec 2 $url | Out-Null; return $true } catch { return $false }
}

# The process this script started for a service, while it still runs.
function ProcessOf([string]$Name) {
    $file = Join-Path $State "$Name.pid"
    if (-not (Test-Path $file)) { return $null }
    $process = Get-Process -Id ([int](Get-Content $file)) -ErrorAction SilentlyContinue
    if (-not $process) { Remove-Item $file -Force }
    return $process
}

# Starts a command hidden, its output in the service's log, and keeps its process id.
function Launch([string]$Name, [string]$Folder, [string]$Line, [hashtable]$Environment = @{}) {
    foreach ($key in $Environment.Keys) { Set-Item -Path "env:$key" -Value $Environment[$key] }
    $log = Join-Path $Logs "$Name.log"
    $process = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', "$Line > `"$log`" 2>&1" `
        -WorkingDirectory $Folder -WindowStyle Hidden -PassThru
    Set-Content -Path (Join-Path $State "$Name.pid") -Value $process.Id
}

function Start-One([string]$Name) {
    $port = $Ports[$Name]
    if (ProcessOf $Name) { Say "${Name}: already running on port $port"; return }
    if (Listening $port) { Say "${Name}: port $port is taken by something not started here; leaving it alone"; return }

    switch ($Name) {
        'opencode' {
            if (-not (Get-Command opencode -ErrorAction SilentlyContinue)) { Fail 'opencode is not installed (npm install -g opencode-ai)' }
            Launch 'opencode' $HOME "opencode serve --port $($Ports.opencode)"
        }
        'api' {
            Say 'api: building...'
            Push-Location $ApiDir
            try {
                dotnet build -v q -nologo *> (Join-Path $Logs 'api-build.log')
                if ($LASTEXITCODE -ne 0) { Fail "the API does not build: see $(Join-Path $Logs 'api-build.log')" }
            } finally { Pop-Location }
            $environment = @{
                ASPNETCORE_ENVIRONMENT = if ($env:ASPNETCORE_ENVIRONMENT) { $env:ASPNETCORE_ENVIRONMENT } else { 'Development' }
                ASPNETCORE_URLS        = "http://localhost:$($Ports.api)"
                OpenCode__BaseUrl      = "http://127.0.0.1:$($Ports.opencode)"
                OpenCode__ToolsUrl     = "http://127.0.0.1:$($Ports.api)/mcp"
            }
            Launch 'api' $ApiDir 'dotnet run --no-build --no-launch-profile' $environment
        }
        'web' {
            # Packages changed since the last install (the lock file says so): install them first.
            $lock = (Get-FileHash (Join-Path $WebDir 'package-lock.json') -Algorithm SHA256).Hash
            $stamp = Join-Path $WebDir 'node_modules\.openspace-lock'
            if (-not (Test-Path (Join-Path $WebDir 'node_modules')) -or ((Test-Path $stamp) -and (Get-Content $stamp) -ne $lock)) {
                Say 'web: installing packages...'
                Push-Location $WebDir
                try {
                    npm ci --silent *> (Join-Path $Logs 'web-install.log')
                    if ($LASTEXITCODE -ne 0) { Fail "npm ci failed: see $(Join-Path $Logs 'web-install.log')" }
                } finally { Pop-Location }
            }
            Set-Content -Path $stamp -Value $lock
            Launch 'web' $WebDir "npx vite --port $($Ports.web) --strictPort" @{ OPENSPACE_API = "http://localhost:$($Ports.api)" }
        }
    }

    for ($i = 0; $i -lt 90; $i++) {
        if (Ready $Name) { Say "${Name}: ready on port $port"; return }
        if (-not (ProcessOf $Name)) { Fail "$Name stopped while starting: see $(Join-Path $Logs "$Name.log")" }
        Start-Sleep -Seconds 1
    }
    Fail "$Name is not answering after 90 seconds: see $(Join-Path $Logs "$Name.log")"
}

function Stop-One([string]$Name) {
    $process = ProcessOf $Name
    if (-not $process) { Say "${Name}: not running (from here)"; return }
    # The whole tree: cmd.exe, and the dotnet, node or opencode it started.
    & taskkill.exe /PID $process.Id /T /F | Out-Null
    Remove-Item (Join-Path $State "$Name.pid") -Force -ErrorAction SilentlyContinue
    Say "${Name}: stopped"
}

function Status-One([string]$Name) {
    $port = $Ports[$Name]
    if (ProcessOf $Name) { $how = if (Ready $Name) { 'ready' } else { 'starting' }; Say "${Name}: running ($how) on port $port" }
    elseif (Listening $port) { Say "${Name}: port $port answers, but it was not started here" }
    else { Say "${Name}: stopped" }
}

switch ($Command) {
    'start' { foreach ($s in (Pick $Services)) { Start-One $s }; Say "Open http://localhost:$($Ports.web)" }
    'stop' { $list = Pick $Services; [array]::Reverse($list); foreach ($s in $list) { Stop-One $s } }
    'restart' {
        $list = @(Pick $Services); $back = $list.Clone(); [array]::Reverse($back)
        foreach ($s in $back) { Stop-One $s }
        foreach ($s in $list) { Start-One $s }
    }
    'status' { foreach ($s in $All) { Status-One $s } }
    'logs' { $s = (Pick @($(if ($Services.Count) { $Services[0] } else { 'api' })))[0]; Get-Content -Path (Join-Path $Logs "$s.log") -Tail 100 -Wait }
    default { (Get-Content $PSCommandPath)[1..14] | ForEach-Object { Write-Host $_ }; exit 2 }
}
