param([string]$UnityEditor='C:/Program Files/Unity 6000.3.24f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$project=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $project
$testProject=Join-Path $project 'Tools/UnityVariationUiProject'
New-Item -ItemType Directory -Force -Path "$testProject/Assets/Scripts","$testProject/Assets/Editor","$testProject/Packages","$testProject/ProjectSettings" | Out-Null
Copy-Item 'Assets/Scripts/*.cs' "$testProject/Assets/Scripts" -Force
Copy-Item 'Assets/Editor/BacardiSongVariationUiTest.cs' "$testProject/Assets/Editor" -Force
Copy-Item 'Tools/UnityRoutingProject/Packages/manifest.json' "$testProject/Packages/manifest.json" -Force
$manifest=Get-Content "$testProject/Packages/manifest.json" -Raw | ConvertFrom-Json
$manifest.dependencies | Add-Member -NotePropertyName 'com.unity.modules.imageconversion' -NotePropertyValue '1.0.0' -Force
$manifest | ConvertTo-Json -Depth 10 | Set-Content "$testProject/Packages/manifest.json"
Copy-Item 'Tools/UnityRoutingProject/ProjectSettings/*.asset' "$testProject/ProjectSettings" -Force
Copy-Item 'ProjectSettings/ProjectVersion.txt' "$testProject/ProjectSettings/ProjectVersion.txt" -Force
$uiArgs='-batchmode -projectPath "'+$testProject+'" -executeMethod BacardiSongVariationUiTest.RunCommandLine -logFile "'+(Join-Path $PSScriptRoot 'runtime-ui.log')+'"'
$uiProcess=Start-Process $UnityEditor -ArgumentList $uiArgs -WindowStyle Hidden -PassThru
$uiProcess.WaitForExit()
Copy-Item "$testProject/Tools/VariationValidation/runtime-ui-results.txt" "$PSScriptRoot/runtime-ui-results.txt" -Force
if(Test-Path "$testProject/Tools/VariationValidation/song-variation-before-planning.png") {
    Copy-Item "$testProject/Tools/VariationValidation/song-variation-before-planning.png" "$PSScriptRoot/song-variation-before-planning.png" -Force
}
Get-Content "$PSScriptRoot/runtime-ui-results.txt"
if($uiProcess.ExitCode) { throw "Runtime variation UI validation failed; see runtime-ui.log" }
