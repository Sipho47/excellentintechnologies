param([string]$UnityEditor='C:/Program Files/Unity 6000.3.24f1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$project=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $project
$sourceHashes=@{}
Get-ChildItem -LiteralPath (Join-Path $project 'Assets/Audio') -Recurse -Filter '*.wav' | ForEach-Object { $sourceHashes[$_.FullName]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
$test=Join-Path $project 'Tools/UnityVariationProject'
New-Item -ItemType Directory -Force -Path "$test/Assets/Scripts","$test/Assets/Editor","$test/Packages","$test/ProjectSettings" | Out-Null
Copy-Item 'Assets/Scripts/*.cs' "$test/Assets/Scripts" -Force
Copy-Item 'Assets/Editor/BacardiSongVariationRuntimeTest.cs' "$test/Assets/Editor" -Force
Copy-Item 'Tools/UnityRoutingProject/Packages/manifest.json' "$test/Packages/manifest.json" -Force
Copy-Item 'Tools/UnityRoutingProject/ProjectSettings/*.asset' "$test/ProjectSettings" -Force
Copy-Item 'ProjectSettings/ProjectVersion.txt' "$test/ProjectSettings/ProjectVersion.txt" -Force
$args='-batchmode -projectPath "'+$test+'" -executeMethod BacardiSongVariationRuntimeTest.RunCommandLine -logFile "'+(Join-Path $PSScriptRoot 'unity.log')+'"'
$process=Start-Process $UnityEditor -ArgumentList $args -WindowStyle Hidden -PassThru
$process.WaitForExit()
if($process.ExitCode) { throw "Unity variation validation failed ($($process.ExitCode)); see Tools/VariationValidation/unity.log" }
Copy-Item "$test/Tools/VariationValidation/unity-results.txt" "$PSScriptRoot/unity-results.txt" -Force
Get-Content "$PSScriptRoot/unity-results.txt"
foreach($sourcePath in $sourceHashes.Keys) {
    if((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne $sourceHashes[$sourcePath]) { throw "Source WAV changed: $sourcePath" }
}
"PASS: $($sourceHashes.Count) source WAV SHA256 hashes unchanged" | Tee-Object -FilePath "$PSScriptRoot/unity-results.txt" -Append
