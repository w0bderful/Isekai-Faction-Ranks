param(
    [string]$RimWorld = 'D:\SteamLibrary\steamapps\common\RimWorld',
    [string]$Workshop = 'D:\SteamLibrary\steamapps\workshop\content\294100'
)
$ErrorActionPreference = 'Stop'
$managed = Join-Path $RimWorld 'RimWorldWin64_Data\Managed'
$refs = @('mscorlib.dll','System.dll','System.Core.dll','netstandard.dll','Assembly-CSharp.dll','UnityEngine.CoreModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.TextRenderingModule.dll') | ForEach-Object { Join-Path $managed $_ }
$refs += Join-Path $Workshop '2009463077\Current\Assemblies\0Harmony.dll'
$refs += Join-Path $Workshop '3657580708\Assemblies\IsekaiLeveling.dll'
New-Item -ItemType Directory -Force -Path "$PSScriptRoot\Assemblies" | Out-Null
$argsForCompiler = @('/nologo','/noconfig','/nostdlib+','/target:library','/optimize+','/langversion:5',"/out:$PSScriptRoot\Assemblies\IsekaiFactionRanks.dll")
$argsForCompiler += $refs | ForEach-Object { '/reference:' + $_ }
$argsForCompiler += "$PSScriptRoot\Source\FactionRanks.cs"
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" @argsForCompiler
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
