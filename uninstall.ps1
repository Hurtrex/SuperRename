$menuKeys    = 'Software\Classes\*\shell\SuperRename', 'Software\Classes\Directory\shell\SuperRename'
$classicMenu = 'Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}'
$markerKey   = 'Software\SuperRename'
$hkcu        = [Microsoft.Win32.Registry]::CurrentUser

foreach ($path in $menuKeys) {
    $hkcu.DeleteSubKeyTree($path, $false)
}
Write-Host 'SuperRename removed from the right-click menu.'

$marker = $hkcu.OpenSubKey($markerKey)
if ($null -ne $marker -and $marker.GetValue('ClassicMenu') -eq 1) {
    $marker.Close()
    $hkcu.DeleteSubKeyTree($classicMenu, $false)
    $hkcu.DeleteSubKeyTree($markerKey, $false)
    Write-Host 'Windows 11 menu restored.'
    if ((Read-Host 'Restart File Explorer now to apply? This closes open Explorer windows. (y/n)') -eq 'y') {
        Stop-Process -Name explorer -Force
    }
}
