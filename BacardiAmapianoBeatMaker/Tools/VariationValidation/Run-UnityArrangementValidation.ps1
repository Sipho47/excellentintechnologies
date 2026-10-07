param([string]$UnityEditor='C:/Program Files/Unity 6000.3.24f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$project=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $project
$testProject=Join-Path $project 'Tools/UnityArrangementProject'
New-Item -ItemType Directory -Force -Path "$testProject/Assets/Scripts","$testProject/Assets/Editor","$testProject/Packages","$testProject/ProjectSettings" | Out-Null
Copy-Item 'Assets/Scripts/*.cs' "$testProject/Assets/Scripts" -Force
Copy-Item 'Assets/Editor/BacardiArrangedSongRuntimeTest.cs' "$testProject/Assets/Editor" -Force
Copy-Item 'Tools/UnityRoutingProject/Packages/manifest.json' "$testProject/Packages/manifest.json" -Force
Copy-Item 'Tools/UnityRoutingProject/ProjectSettings/*.asset' "$testProject/ProjectSettings" -Force
Copy-Item 'ProjectSettings/ProjectVersion.txt' "$testProject/ProjectSettings/ProjectVersion.txt" -Force
$validationArgs='-batchmode -projectPath "'+$testProject+'" -executeMethod BacardiArrangedSongRuntimeTest.RunCommandLine -logFile "'+(Join-Path $PSScriptRoot 'arrangement-runtime.log')+'"'
$validationProcess=Start-Process $UnityEditor -ArgumentList $validationArgs -WindowStyle Hidden -PassThru
$validationProcess.WaitForExit()
if($validationProcess.ExitCode) { throw "Arranged runtime validation failed; see arrangement-runtime.log" }
Copy-Item "$testProject/Tools/ArrangementValidation/runtime-results.txt" "$PSScriptRoot/arrangement-runtime-results.txt" -Force
Get-Content "$PSScriptRoot/arrangement-runtime-results.txt"
