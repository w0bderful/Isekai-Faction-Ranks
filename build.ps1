param(
    [string]$RimWorld = 'D:\SteamLibrary\steamapps\common\RimWorld',
    [string]$Workshop = 'D:\SteamLibrary\steamapps\workshop\content\294100'
)
$ErrorActionPreference = 'Stop'
# RimWorld 1.7 runs on .NET 10; the 1.6 DLL in 1.6/Assemblies is kept as built for 1.6.
& dotnet build "$PSScriptRoot\Source\IsekaiFactionRanks.csproj" -c Release -nologo -v q "-p:RimWorld=$RimWorld" "-p:Workshop=$Workshop"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
