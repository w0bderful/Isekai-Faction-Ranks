$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& "$root\build.ps1"
# Stock .NET 10 lacks RimWorld 1.7's UnityEngine* -> UnityStubs redirect; build forwarding shims.
New-Item -ItemType Directory -Force -Path "$PSScriptRoot\obj\shims" | Out-Null
& dotnet run --file "$PSScriptRoot\UnityShim\Generate.cs" -- 'D:\SteamLibrary\steamapps\common\RimWorld\UnityStubs.dll' "$PSScriptRoot\obj\shims\Forwards.cs"
if ($LASTEXITCODE -ne 0) { throw 'Shim generation failed' }
foreach ($name in 'UnityEngine.CoreModule','UnityEngine.TextRenderingModule','UnityEngine.IMGUIModule','UnityEngine.AudioModule','UnityEngine.ImageConversionModule') {
    & dotnet build "$PSScriptRoot\UnityShim\UnityShim.csproj" -nologo -v q "-p:ShimName=$name"
    if ($LASTEXITCODE -ne 0) { throw "Shim build failed: $name" }
}
& dotnet build "$PSScriptRoot\Validate.csproj" -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& dotnet "$PSScriptRoot\bin\Validate.dll" $root
if ($LASTEXITCODE -ne 0) { throw 'Validation failed' }
