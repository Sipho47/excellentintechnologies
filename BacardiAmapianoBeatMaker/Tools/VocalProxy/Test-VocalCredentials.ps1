$ErrorActionPreference = 'Stop'
# Local health only. Never reads the key or contacts ElevenLabs.
$health = Invoke-RestMethod -Uri 'http://127.0.0.1:8765/health' -TimeoutSec 5
if ($health.provider -ne 'ElevenLabs Music' -or $health.configured -isnot [bool]) {
    throw 'Unexpected VocalProxy health response.'
}
if ($health.configured) { Write-Output 'CREDENTIALS CONFIGURED' }
else { Write-Output 'CREDENTIALS MISSING' }
