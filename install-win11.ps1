param(
    [switch]$Win10Menu,
    [string]$Exe = (Join-Path $PSScriptRoot 'publish\SuperRename.exe')
)

$ErrorActionPreference = 'Stop'

$menuKeys    = 'Software\Classes\*\shell\SuperRename', 'Software\Classes\Directory\shell\SuperRename'
$classicMenu = 'Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}'
$markerKey   = 'Software\SuperRename'

if (-not (Test-Path $Exe)) {
    Write-Host 'Building SuperRename...'
    dotnet publish (Join-Path $PSScriptRoot 'SuperRename') -c Release -r win-x64 --self-contained false `
        -p:PublishSingleFile=true -o (Join-Path $PSScriptRoot 'publish')
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
$Exe  = (Resolve-Path $Exe).Path
$hkcu = [Microsoft.Win32.Registry]::CurrentUser

foreach ($path in $menuKeys) {
    $key = $hkcu.CreateSubKey($path)
    $key.SetValue('', 'SuperRename')
    $key.SetValue('Icon', $Exe)
    $key.SetValue('MultiSelectModel', 'Player')
    $command = $key.CreateSubKey('command')
    $command.SetValue('', "`"$Exe`" `"%1`"")
    $command.Close()
    $key.Close()
}

$classicMenuIsOn   = $null -ne $hkcu.OpenSubKey("$classicMenu\InprocServer32")
$marker            = $hkcu.OpenSubKey($markerKey)
$classicMenuIsOurs = $null -ne $marker -and $marker.GetValue('ClassicMenu') -eq 1
$needsRestart      = $false

if ($Win10Menu -and -not $classicMenuIsOn) {
    $key = $hkcu.CreateSubKey("$classicMenu\InprocServer32"); $key.SetValue('', ''); $key.Close()
    $key = $hkcu.CreateSubKey($markerKey); $key.SetValue('ClassicMenu', 1, 'DWord'); $key.Close()
    $needsRestart = $true
} elseif (-not $Win10Menu -and $classicMenuIsOurs) {
    $hkcu.DeleteSubKeyTree($classicMenu, $false)
    $hkcu.DeleteSubKeyTree($markerKey, $false)
    $needsRestart = $true
} elseif (-not $Win10Menu -and $classicMenuIsOn) {
    Write-Host 'Note: this PC already uses the Windows 10 menu (set outside SuperRename). Left unchanged.'
}

Write-Host "Installed. Program: $Exe"
if ($Win10Menu) { Write-Host 'Right-click files or folders > SuperRename' }
else { Write-Host 'Right-click files or folders > Show more options > SuperRename' }

if ($needsRestart) {
    Write-Host 'File Explorer must restart for the menu style change. This closes open Explorer windows.'
    if ((Read-Host 'Restart File Explorer now? (y/n)') -eq 'y') {
        Stop-Process -Name explorer -Force
    } else {
        Write-Host 'The change applies after you sign out and back in, or restart the PC.'
    }
}
