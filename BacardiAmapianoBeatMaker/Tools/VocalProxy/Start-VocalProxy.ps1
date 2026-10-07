param([string]$UnityData='C:/Program Files/Unity 6000.3.24f1/Editor/Data',[string]$KeyFile,[switch]$SelfTest,[switch]$PromptForKey)
$ErrorActionPreference='Stop'
$project=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $project
$runtimePath=Join-Path $UnityData 'NetCoreRuntime/shared/Microsoft.NETCore.App/6.0.21'
$dotnet=Join-Path $UnityData 'NetCoreRuntime/dotnet.exe'
$csc=Join-Path $UnityData 'DotNetSdkRoslyn/csc.dll'
$refs=Get-ChildItem $runtimePath -Filter *.dll | Where-Object { $_.Name -match '^(System\.(?!IO.Compression.Native)|mscorlib|netstandard|Microsoft.CSharp|Microsoft.VisualBasic)' } | ForEach-Object { '/r:"'+$_.FullName+'"' }
$binaryName=if($SelfTest) { 'VocalProxy.SelfTest' } else { 'VocalProxy' }
Set-Content "$PSScriptRoot/build.rsp" (@('/nologo','/optimize+',('/out:Tools/VocalProxy/'+$binaryName+'.dll'))+$refs+@('Tools/VocalProxy/VocalProxy.cs','Tools/VocalProxy/VocalProxyTests.cs','Assets/Scripts/AiVocalFailure.cs'))
& $dotnet $csc '@Tools/VocalProxy/build.rsp'
if($LASTEXITCODE) { throw 'Vocal proxy compilation failed' }
Copy-Item 'Tools/LufsValidation/Check.runtimeconfig.json' "$PSScriptRoot/$binaryName.runtimeconfig.json"
if(!$env:VOCAL_FFMPEG_PATH) { $env:VOCAL_FFMPEG_PATH=Get-ChildItem 'Tools/ffmpeg' -Filter ffmpeg.exe -Recurse | Select-Object -First 1 -ExpandProperty FullName }
if($PromptForKey -and !$SelfTest -and !$KeyFile -and !$env:ELEVENLABS_API_KEY) {
    $masked=Read-Host 'ElevenLabs API key (masked; never saved to source)' -AsSecureString
    $credentialPointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($masked)
    try { $env:ELEVENLABS_API_KEY=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($credentialPointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($credentialPointer); $masked.Dispose() }
}
if($SelfTest) { & $dotnet "$PSScriptRoot/$binaryName.dll" --self-test }
elseif($KeyFile) { & $dotnet "$PSScriptRoot/VocalProxy.dll" --key-file $KeyFile }
else { & $dotnet "$PSScriptRoot/VocalProxy.dll" }
if($LASTEXITCODE) { throw 'Vocal proxy failed; inspect the error above.' }
