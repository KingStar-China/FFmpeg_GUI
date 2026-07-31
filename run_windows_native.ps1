param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$Paths
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $repoRoot 'src\FFmpegGui.App\FFmpegGui.App.csproj'

& dotnet run --project $project --configuration Debug -- @Paths
exit $LASTEXITCODE
