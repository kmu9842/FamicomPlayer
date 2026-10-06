$ErrorActionPreference = 'Stop'
$installDirectory = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'FamicomPlayerChrome'))
$manifestPath = Join-Path $installDirectory 'com.famicomplayer.player.json'
foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)) {
    $registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $view)
    try {
        $keyPath = 'Software\Google\Chrome\NativeMessagingHosts\com.famicomplayer.player'
        $key = $registry.OpenSubKey($keyPath)
        $owned = $false
        if ($key) {
            try { $owned = $key.GetValue('') -eq $manifestPath }
            finally { $key.Dispose() }
        }
        if ($owned) { $registry.DeleteSubKey($keyPath, $false) }
    } finally { $registry.Dispose() }
}
if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath -Force }
# Keep the loaded extension files until the user removes it in Chrome. Never touch the app or its settings.
Write-Output 'Disconnected. Remove FamicomPlayer from chrome://extensions.'
Write-Output "After removing it in Chrome, you can delete: $installDirectory"
