param([switch]$Runtime,[string]$UnityData='C:/Program Files/Unity 6000.3.24f1/Editor/Data')
$ErrorActionPreference='Stop'
$project=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $project
$runtimePath=Join-Path $UnityData 'NetCoreRuntime/shared/Microsoft.NETCore.App/6.0.21'
$dotnet=Join-Path $UnityData 'NetCoreRuntime/dotnet.exe'
$csc=Join-Path $UnityData 'DotNetSdkRoslyn/csc.dll'
$refs=Get-ChildItem $runtimePath -Filter *.dll | Where-Object { $_.Name -match '^(System\.(?!IO.Compression.Native)|mscorlib|netstandard|Microsoft.CSharp|Microsoft.VisualBasic)' } | ForEach-Object { '/r:"'+$_.FullName+'"' }
$unityRefs=Get-ChildItem (Join-Path $UnityData 'Managed/UnityEngine') -Filter *.dll | ForEach-Object { '/r:"'+$_.FullName+'"' }
Set-Content "$PSScriptRoot/runtime.rsp" (@('/nologo','/optimize+','/target:library','/out:Tools/VocalValidation/Runtime.dll')+$refs+$unityRefs+@(Get-ChildItem Assets/Scripts -Filter *.cs | ForEach-Object { '"'+$_.FullName+'"' }))
& $dotnet $csc '@Tools/VocalValidation/runtime.rsp'
if($LASTEXITCODE) { throw 'Runtime compilation failed' }
Set-Content "$PSScriptRoot/check.rsp" (@('/nologo','/optimize+','/out:Tools/VocalValidation/VocalCheck.dll')+$refs+@('Tools/VocalValidation/VocalCheck.cs','Assets/Scripts/BacardiHookLyrics.cs','Assets/Scripts/AiVocalProvider.cs','Assets/Scripts/BacardiVocalTimeline.cs','Assets/Scripts/BacardiMastering.cs','Assets/Scripts/BacardiLufsEngine.cs'))
Copy-Item 'Tools/LufsValidation/Check.runtimeconfig.json' "$PSScriptRoot/VocalCheck.runtimeconfig.json"
& $dotnet $csc '@Tools/VocalValidation/check.rsp'
if($LASTEXITCODE) { throw 'Vocal test compilation failed' }
& $dotnet 'Tools/VocalValidation/VocalCheck.dll'
if($LASTEXITCODE) { throw 'Managed vocal validation failed' }
if($Runtime) {
    $test=Join-Path $project 'Tools/UnityRoutingProject'
    if(!(Test-Path (Join-Path $test 'ProjectSettings/ProjectVersion.txt'))) { throw 'Isolated UnityRoutingProject missing.' }
    Copy-Item 'Assets/Scripts/*.cs' (Join-Path $test 'Assets/Scripts') -Force
    Copy-Item 'Assets/Editor/BacardiVocalRuntimeTest.cs' (Join-Path $test 'Assets/Editor') -Force
    Copy-Item 'Assets/Editor/BacardiRealVocalAcceptance.cs' (Join-Path $test 'Assets/Editor') -Force
    Copy-Item 'Assets/Scenes/BacardiBeatMaker.unity' (Join-Path $test 'Assets/Scenes') -Force
    $unity=Join-Path (Split-Path $UnityData -Parent) 'Unity.exe'
    $args='-batchmode -projectPath "'+$test+'" -executeMethod BacardiVocalRuntimeTest.RunCommandLine -logFile "'+(Join-Path $PSScriptRoot 'Unity-Vocal.log')+'"'
    $process=Start-Process $unity -ArgumentList $args -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if($process.ExitCode) { throw "Unity validation failed. See Tools/VocalValidation/Unity-Vocal.log" }
    Copy-Item (Join-Path $test 'Tools/VocalValidation/Unity-Vocal-Runtime.txt') "$PSScriptRoot/Unity-Vocal-Runtime.txt" -Force
    Get-Content "$PSScriptRoot/Unity-Vocal-Runtime.txt"
}
