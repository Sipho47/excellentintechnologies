param([string]$UnityData='C:/Program Files/Unity 6000.3.24f1/Editor/Data')
$ErrorActionPreference='Stop'
$project=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $project
$runtime=Join-Path $UnityData 'NetCoreRuntime/shared/Microsoft.NETCore.App/6.0.21'
$dotnet=Join-Path $UnityData 'NetCoreRuntime/dotnet.exe'
$csc=Join-Path $UnityData 'DotNetSdkRoslyn/csc.dll'
$refs=Get-ChildItem $runtime -Filter *.dll | Where-Object { $_.Name -match '^(System\.(?!IO.Compression.Native)|mscorlib|netstandard|Microsoft.CSharp|Microsoft.VisualBasic)' } | ForEach-Object { '/r:"'+$_.FullName+'"' }
$unityRefs=Get-ChildItem (Join-Path $UnityData 'Managed/UnityEngine') -Filter *.dll | ForEach-Object { '/r:"'+$_.FullName+'"' }
Set-Content "$PSScriptRoot/stereo-runtime.rsp" (@('/nologo','/target:library','/define:UNITY_STANDALONE_WIN','/out:Tools/LufsValidation/StereoRuntime.dll')+$refs+$unityRefs+@(Get-ChildItem Assets/Scripts -Filter *.cs | ForEach-Object { '"'+$_.FullName+'"' }))
& $dotnet $csc '@Tools/LufsValidation/stereo-runtime.rsp'
if($LASTEXITCODE) { throw 'Runtime compilation failed' }
Set-Content "$PSScriptRoot/stereo-check.rsp" (@('/nologo','/optimize+','/out:Tools/LufsValidation/StereoCheck.dll')+$refs+@('Tools/LufsValidation/StereoCheck.cs','Assets/Scripts/BacardiStereoMaster.cs','Assets/Scripts/BacardiMastering.cs','Assets/Scripts/BacardiLufsEngine.cs'))
& $dotnet $csc '@Tools/LufsValidation/stereo-check.rsp'
if($LASTEXITCODE) { throw 'Test compilation failed' }
Copy-Item "$PSScriptRoot/Check.runtimeconfig.json" "$PSScriptRoot/StereoCheck.runtimeconfig.json" -Force
& $dotnet 'Tools/LufsValidation/StereoCheck.dll'
if($LASTEXITCODE) { throw 'Stereo tests failed' }
$ff=Get-ChildItem Tools/ffmpeg -Filter ffmpeg.exe -Recurse | Select-Object -First 1 -ExpandProperty FullName
$fixture=Join-Path $PSScriptRoot 'StereoFixtures/Low-LUFS High-Peak Master.wav'
$process=New-Object System.Diagnostics.Process
$process.StartInfo=New-Object System.Diagnostics.ProcessStartInfo($ff,('-hide_banner -nostdin -i "'+$fixture+'" -af ebur128=peak=true -f null -'))
$process.StartInfo.UseShellExecute=$false
$process.StartInfo.CreateNoWindow=$true
$process.StartInfo.RedirectStandardError=$true
[void]$process.Start(); $log=$process.StandardError.ReadToEnd(); $process.WaitForExit()
if($process.ExitCode) { throw 'Independent FFmpeg measurement failed' }
$summary=$log.Substring($log.LastIndexOf('Summary:'))
Set-Content "$PSScriptRoot/Stereo-High-Crest-Independent.txt" $summary
$integrated=[double]::Parse([regex]::Match($summary,'I:\s*([-+\d.]+) LUFS').Groups[1].Value,[cultureinfo]::InvariantCulture)
$peak=[double]::Parse([regex]::Match($summary,'Peak:\s*([-+\d.]+) dBFS').Groups[1].Value,[cultureinfo]::InvariantCulture)
if([Math]::Abs($integrated+14) -gt .2 -or $peak -gt -1) { throw "Independent validation failed: $integrated LUFS / $peak dBTP" }
"PASS: Independent FFmpeg exported WAV: $integrated LUFS-I / $peak dBTP"
