param([string]$UnityData='C:/Program Files/Unity 6000.3.24f1/Editor/Data')
$ErrorActionPreference='Stop'
$project=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $project
$runtime=Join-Path $UnityData 'NetCoreRuntime/shared/Microsoft.NETCore.App/6.0.21'
$dotnet=Join-Path $UnityData 'NetCoreRuntime/dotnet.exe'
$csc=Join-Path $UnityData 'DotNetSdkRoslyn/csc.dll'
$refs=Get-ChildItem $runtime -Filter *.dll | Where-Object { $_.Name -match '^(System\.(?!IO.Compression.Native)|mscorlib|netstandard|Microsoft.CSharp|Microsoft.VisualBasic)' } | ForEach-Object { '/r:"'+$_.FullName+'"' }
Set-Content "$PSScriptRoot/check.rsp" (@('/nologo','/optimize+','/out:Tools/VariationValidation/VariationCheck.dll')+$refs+@('Tools/VariationValidation/VariationCheck.cs','Assets/Scripts/BacardiSongVariation.cs','Assets/Scripts/BacardiVariationEvents.cs'))
& $dotnet $csc '@Tools/VariationValidation/check.rsp'
if($LASTEXITCODE) { throw 'Variation test compilation failed' }
Copy-Item 'Tools/LufsValidation/Check.runtimeconfig.json' "$PSScriptRoot/VariationCheck.runtimeconfig.json" -Force
& $dotnet 'Tools/VariationValidation/VariationCheck.dll' | Tee-Object "$PSScriptRoot/results.txt"
if($LASTEXITCODE) { throw 'Variation tests failed' }
