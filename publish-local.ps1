# Publishes Spat with the win-x64 publish profile into %USERPROFILE%\Apps\Spat.exe.
# The publish is signed by build\WindowsSign.Build.targets, which needs the Sign CLI
# global tool and the "CN=Bitbound" certificate in the current user's Personal store.
# Pass -p:EnableSigning=false to skip signing.
$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'Spat\Spat.csproj'

# The same date-based version the release workflow stamps, so a local build is
# comparable to released ones and the in-app updater still parses it.
$version = (Get-Date).ToUniversalTime().ToString('yyyy.M.d.HHmm')

dotnet publish $project -c Release -p:PublishProfile=win-x64 "-p:Version=$version"

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Published $(Join-Path $env:USERPROFILE 'Apps\Spat.exe') (version $version)"
