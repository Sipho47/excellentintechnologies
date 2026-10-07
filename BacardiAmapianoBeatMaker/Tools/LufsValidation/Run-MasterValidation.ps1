param([string]$UnityData='C:/Program Files/Unity 6000.3.24f1/Editor/Data')
$ErrorActionPreference='Stop'
$project=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $project
$runtime=Join-Path $UnityData 'NetCoreRuntime/shared/Microsoft.NETCore.App/6.0.21'
$dotnet=Join-Path $UnityData 'NetCoreRuntime/dotnet.exe'
$csc=Join-Path $UnityData 'DotNetSdkRoslyn/csc.dll'
$refs=Get-ChildItem $runtime -Filter *.dll | Where-Object { $_.Name -match '^(System\.(?!IO.Compression.Native)|mscorlib|netstandard|Microsoft.CSharp|Microsoft.VisualBasic)' } | ForEach-Object { '/r:"'+$_.FullName+'"' }
$unityRefs=Get-ChildItem (Join-Path $UnityData 'Managed/UnityEngine') -Filter *.dll | ForEach-Object { '/r:"'+$_.FullName+'"' }
Set-Content "$PSScriptRoot/managed.rsp" (@('/nologo','/optimize+','/target:library','/out:Tools/LufsValidation/Runtime.dll')+$refs+$unityRefs+@(Get-ChildItem Assets/Scripts -Filter *.cs | ForEach-Object { '"'+$_.FullName+'"' }))
& $dotnet $csc '@Tools/LufsValidation/managed.rsp'
if($LASTEXITCODE) { throw 'Runtime compilation failed' }
Set-Content "$PSScriptRoot/check.rsp" (@('/nologo','/optimize+','/out:Tools/LufsValidation/MasterCheck.dll')+$refs+@('Tools/LufsValidation/MasterCheck.cs','Assets/Scripts/BacardiMastering.cs','Assets/Scripts/BacardiLufsEngine.cs','Assets/Scripts/BacardiMasterComparison.cs'))
& $dotnet $csc '@Tools/LufsValidation/check.rsp'
if($LASTEXITCODE) { throw 'Validation compilation failed' }
Copy-Item "$PSScriptRoot/Check.runtimeconfig.json" "$PSScriptRoot/MasterCheck.runtimeconfig.json"
& $dotnet 'Tools/LufsValidation/MasterCheck.dll'
if($LASTEXITCODE) { throw 'DSP validation failed' }
$ff=Get-ChildItem Tools/ffmpeg -Filter ffmpeg.exe -Recurse | Select-Object -First 1 -ExpandProperty FullName
foreach($name in @('Safe Run 1','Safe Run 2','Safe Run 3','Loop Run 1','Loop Run 2','Loop Run 3','Forced')) {
 $process=New-Object System.Diagnostics.Process
 $process.StartInfo=New-Object System.Diagnostics.ProcessStartInfo($ff,('-hide_banner -nostdin -i "'+$PSScriptRoot+'/'+$name+' Master.wav" -af ebur128=peak=true -f null -'))
 $process.StartInfo.UseShellExecute=$false
 $process.StartInfo.CreateNoWindow=$true
 $process.StartInfo.RedirectStandardError=$true
 [void]$process.Start(); $log=$process.StandardError.ReadToEnd(); $process.WaitForExit()
 if($process.ExitCode) { throw 'FFmpeg failed' }
 $summary=$log.Substring($log.LastIndexOf('Summary:'))
 Set-Content "$PSScriptRoot/$name-measurement.txt" $summary
 $i=[double]::Parse([regex]::Match($summary,'I:\s*([-+\d.]+) LUFS').Groups[1].Value,[cultureinfo]::InvariantCulture)
 $tp=[double]::Parse([regex]::Match($summary,'Peak:\s*([-+\d.]+) dBFS').Groups[1].Value,[cultureinfo]::InvariantCulture)
 if($tp -gt -1) { throw "$name exceeded true peak ceiling: $tp" }
 if($name -eq 'Forced' -and [Math]::Abs($i+14) -gt .2) { throw "$name missed loudness target: $i" }
 "$name verified: $i LUFS-I / $tp dBTP"
}





