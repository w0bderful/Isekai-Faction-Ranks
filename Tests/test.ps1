$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& "$root\build.ps1"
$managed = 'D:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed'
$workshop = 'D:\SteamLibrary\steamapps\workshop\content\294100'
$refs = @("$managed\Assembly-CSharp.dll", "$managed\UnityEngine.CoreModule.dll", "$managed\netstandard.dll", "$workshop\3657580708\Assemblies\IsekaiLeveling.dll", "$workshop\2009463077\Current\Assemblies\0Harmony.dll", "$root\Assemblies\IsekaiFactionRanks.dll")
$argsForCompiler = @('/nologo','/target:exe','/langversion:5',"/out:$PSScriptRoot\Validate.exe")
$argsForCompiler += $refs | ForEach-Object { '/reference:' + $_ }
$argsForCompiler += "$PSScriptRoot\Validate.cs"
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" @argsForCompiler
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& "$PSScriptRoot\Validate.exe" $root
if ($LASTEXITCODE -ne 0) { throw 'Validation failed' }
