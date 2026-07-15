param(
  [string]$ExePath = "$env:USERPROFILE\Desktop\tiny\TinyPix-Pro-3.5.1-Windows-x64-Portable.exe",
  [int]$Port = 9222
)

$ErrorActionPreference = 'Stop'
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$Port"
Get-Process '*TinyPix*' -ErrorAction SilentlyContinue | Stop-Process -Force
$launcher = Start-Process $ExePath -PassThru
try {
  for ($i = 0; $i -lt 40; $i++) {
    try {
      $targets = Invoke-RestMethod "http://127.0.0.1:$Port/json"
      $targets | Select-Object id,title,url,webSocketDebuggerUrl | ConvertTo-Json -Depth 3
      exit 0
    } catch {
      Start-Sleep -Milliseconds 250
    }
  }
  throw 'WebView2 CDP endpoint did not become ready.'
} finally {
  Get-Process '*TinyPix*' -ErrorAction SilentlyContinue | Stop-Process -Force
  if (!$launcher.HasExited) { $launcher.Kill() }
}
