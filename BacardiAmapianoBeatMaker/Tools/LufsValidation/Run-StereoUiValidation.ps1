param([string]$UnityEditor='C:/Program Files/Unity 6000.3.24f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$project=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $project
$testProject=Join-Path $project 'Tools/UnityStereoUiProject'
$results=Join-Path $project 'Tools/StereoUiValidation'
New-Item -ItemType Directory -Force -Path "$testProject/Assets/Scripts","$testProject/Assets/Editor","$testProject/Packages","$testProject/ProjectSettings",$results | Out-Null
foreach($name in @('BacardiStereoMasterPanel','BacardiStereoMaster','BacardiMastering','BacardiLufsEngine')) {
    Copy-Item -LiteralPath "Assets/Scripts/$name.cs" -Destination "$testProject/Assets/Scripts" -Force
}
Copy-Item -LiteralPath 'Assets/Editor/BacardiStereoMasterUiTest.cs' -Destination "$testProject/Assets/Editor" -Force
Copy-Item -LiteralPath 'Tools/UnityRoutingProject/Packages/manifest.json' -Destination "$testProject/Packages/manifest.json" -Force
$manifest=Get-Content "$testProject/Packages/manifest.json" -Raw | ConvertFrom-Json
$manifest.dependencies | Add-Member -NotePropertyName 'com.unity.modules.imageconversion' -NotePropertyValue '1.0.0' -Force
$manifest | ConvertTo-Json -Depth 10 | Set-Content "$testProject/Packages/manifest.json"
Copy-Item 'Tools/UnityRoutingProject/ProjectSettings/*.asset' "$testProject/ProjectSettings" -Force
Copy-Item -LiteralPath 'ProjectSettings/ProjectVersion.txt' -Destination "$testProject/ProjectSettings/ProjectVersion.txt" -Force
$uiArgs='-batchmode -projectPath "'+$testProject+'" -executeMethod BacardiStereoMasterUiTest.RunCommandLine -logFile "'+(Join-Path $results 'runtime-ui.log')+'"'
$uiProcess=Start-Process $UnityEditor -ArgumentList $uiArgs -WindowStyle Hidden -PassThru
$uiProcess.WaitForExit()
Copy-Item "$testProject/Tools/StereoUiValidation/*.txt" $results -Force
Copy-Item "$testProject/Tools/StereoUiValidation/*.png" $results -Force
Get-Content "$results/runtime-ui-results.txt"
if($uiProcess.ExitCode) { throw 'Stereo runtime UI validation failed; see Tools/StereoUiValidation/runtime-ui.log' }
