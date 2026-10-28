param([Parameter(Mandatory)][int]$BrowserPid, [Parameter(Mandatory)][string]$ProfilePath)
$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($ProfilePath)
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
if (!$target.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($target) -notmatch '^ankt-sandbox-executor-[a-zA-Z0-9-]+$') { throw 'Unexpected browser profile' }
$browser = Get-CimInstance Win32_Process -Filter "ProcessId = $BrowserPid"
if ($null -ne $browser) {
    if ([IO.Path]::GetFileName($browser.ExecutablePath) -ine 'chrome.exe' -or !$browser.CommandLine.Contains("--user-data-dir=$target")) { throw 'Browser ownership check failed' }
    Stop-Process -Id $BrowserPid -Force
    Wait-Process -Id $BrowserPid -Timeout 5 -ErrorAction SilentlyContinue
}
