# Teste de fumaça da UI (local): servidor + app + bot sem áudio, dirigido por UI Automation.
# Requer: dotnet build Coconut.slnx. Abre o app na área de trabalho por ~20 s e captura o microfone
# durante a chamada de teste (o áudio só vai ao bot, em loopback, e é descartado). Nada é gravado.
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File prototypes\Tools.UiSmoke\ui-smoke.ps1
param([string]$Repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path)
$ErrorActionPreference = "Stop"
$tools = $PSScriptRoot
$out = Join-Path $PSScriptRoot ("runs\" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force $out | Out-Null
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]

function Find-Named($root, $name, $timeoutSec = 10) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $e = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($e) { return $e }
        Start-Sleep -Milliseconds 200
    }
    throw "Elemento '$name' não encontrado"
}
function Invoke-Named($root, $name) {
    (Find-Named $root $name).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Shot($proc, $file) { & (Join-Path $tools "capture-window.ps1") -ProcessId $proc.Id -Output (Join-Path $out $file) }

# 1. Servidor de sinalização local
Get-Process Coconut -ErrorAction SilentlyContinue | Stop-Process -Force
$serverDll = Join-Path $Repo "src\Coconut.Signaling.Server\bin\Debug\net10.0\Coconut.Signaling.Server.dll"
$server = Start-Process dotnet -ArgumentList @($serverDll, "--urls", "http://127.0.0.1:5080") -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $out "server.out.txt") -RedirectStandardError (Join-Path $out "server.err.txt")
$ok = $false
for ($i = 0; $i -lt 50 -and -not $ok; $i++) { try { $ok = (Invoke-WebRequest -UseBasicParsing http://127.0.0.1:5080/healthz).StatusCode -eq 200 } catch { Start-Sleep -Milliseconds 200 } }
"servidor: $ok"

try {
    # 2. App
    $app = Start-Process (Join-Path $Repo "src\Coconut.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\Coconut.exe") -PassThru
    Start-Sleep -Seconds 4
    $app.Refresh()
    $win = $AE::FromHandle($app.MainWindowHandle)

    # 3. Criar sala
    (Find-Named $win "Seu nome").GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("Teste")
    Invoke-Named $win "Criar sala"
    Start-Sleep -Seconds 3
    Shot $app "1-sala-criada.png"

    # 4. Convite exibido na tela
    $textCondition = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $invite = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition) |
        ForEach-Object { $_.Current.Name } | Where-Object { $_ -match '^[0-9A-Z]{5}-[0-9A-Z]{5}$' } | Select-Object -First 1
    "convite na tela: $(if ($invite) { 'sim' } else { 'NÃO' })"

    # 5. Segundo participante: bot sem áudio (nome sem espaços: Start-Process não põe aspas nos argumentos)
    $botDll = Join-Path $Repo "prototypes\Tools.CallBot\bin\Debug\net10.0\Tools.CallBot.dll"
    $bot = Start-Process dotnet -ArgumentList @($botDll, "ws://127.0.0.1:5080/ws", $invite, "BotTeste", "40") -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $out "bot.out.txt") -RedirectStandardError (Join-Path $out "bot.err.txt")
    Start-Sleep -Seconds 4
    Shot $app "2-com-bot.png"

    # 6. Administrador remove o bot pela UI
    Invoke-Named $win "Remover da sala"
    Start-Sleep -Seconds 2
    Shot $app "3-bot-removido.png"

    # 7. Sair
    Invoke-Named $win "Sair"
    Start-Sleep -Seconds 2
    Shot $app "4-inicio.png"

    $bot.WaitForExit(5000) | Out-Null
    "bot:"; Get-Content (Join-Path $out "bot.out.txt")
    $app.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2; if (-not $app.HasExited) { $app.Kill() }
    "app encerrado: $($app.HasExited)"
    "capturas em $out"
}
finally {
    Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
}
