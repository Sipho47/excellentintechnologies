param([string]$UnityData='C:/Program Files/Unity 6000.3.24f1/Editor/Data')
$ErrorActionPreference='Stop'
$project=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $project
$test=Join-Path $project 'Tools/UnityStrictLyricsUiProject'
New-Item -ItemType Directory -Force -Path "$test/Assets/Scripts","$test/Assets/Editor","$test/ProjectSettings","$test/Packages" | Out-Null
Copy-Item 'Assets/Scripts/*.cs' "$test/Assets/Scripts" -Force
Copy-Item 'Assets/Editor/BacardiStrictLyricsUiTest.cs' "$test/Assets/Editor" -Force
Copy-Item 'ProjectSettings/ProjectVersion.txt' "$test/ProjectSettings" -Force
Set-Content "$test/Packages/manifest.json" '{"dependencies":{"com.unity.modules.audio":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.jsonserialize":"1.0.0","com.unity.modules.ui":"1.0.0"}}'
$unity=Join-Path (Split-Path $UnityData -Parent) 'Unity.exe'
$arguments='-batchmode -projectPath "'+$test+'" -executeMethod BacardiStrictLyricsUiTest.RunCommandLine -logFile "'+(Join-Path $PSScriptRoot 'Strict-Lyrics-UI.log')+'"'
$process=Start-Process $unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(240000)) { Stop-Process -Id $process.Id; throw 'Unity UI test startup timed out; see Strict-Lyrics-UI.log.' }
if ($process.ExitCode) { throw 'Unity strict lyrics UI test failed; see Strict-Lyrics-UI.log.' }
Copy-Item "$test/Tools/VocalValidation/Strict-Lyrics-UI.txt" "$PSScriptRoot/Strict-Lyrics-UI.txt" -Force
Get-Content "$PSScriptRoot/Strict-Lyrics-UI.txt"
