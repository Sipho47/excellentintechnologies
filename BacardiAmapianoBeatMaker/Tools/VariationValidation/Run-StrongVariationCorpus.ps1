$ErrorActionPreference='Stop'
$unityData='C:/Program Files/Unity 6000.3.24f1/Editor/Data'
$runtime=Join-Path $unityData 'NetCoreRuntime/shared/Microsoft.NETCore.App/6.0.21'
$dotnet=Join-Path $unityData 'NetCoreRuntime/dotnet.exe'
$csc=Join-Path $unityData 'DotNetSdkRoslyn/csc.dll'
$refs=Get-ChildItem $runtime -Filter *.dll | Where-Object { $_.Name -match '^(System\.(?!IO.Compression.Native)|mscorlib|netstandard|Microsoft.CSharp|Microsoft.VisualBasic)' } | ForEach-Object { '/r:"'+$_.FullName+'"' }
Set-Content 'Tools/VariationValidation/strong.rsp' (@('/nologo','/optimize+','/out:Tools/VariationValidation/StrongVariationCorpus.dll')+$refs+@('Tools/VariationValidation/StrongVariationCorpus.cs','Assets/Scripts/BacardiSongVariation.cs','Assets/Scripts/BacardiVariationEvents.cs'))
& $dotnet $csc '@Tools/VariationValidation/strong.rsp'
if($LASTEXITCODE){throw 'Corpus compilation failed'}
Copy-Item 'Tools/LufsValidation/Check.runtimeconfig.json' 'Tools/VariationValidation/StrongVariationCorpus.runtimeconfig.json' -Force
& $dotnet 'Tools/VariationValidation/StrongVariationCorpus.dll'
if($LASTEXITCODE){throw 'Corpus failed'}
