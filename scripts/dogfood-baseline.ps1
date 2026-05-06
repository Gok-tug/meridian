[CmdletBinding()]
param(
    [string]$DogfoodRoot = ".dogfood",
    [string]$OutputRoot = (Join-Path (Join-Path "artifacts" "dogfood") (Get-Date -Format "yyyyMMdd-HHmmss")),
    [string]$Configuration = "Release",
    [switch]$IncludeTests,
    [switch]$TrustProject
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$DogfoodRootPath = if ([IO.Path]::IsPathRooted($DogfoodRoot)) { $DogfoodRoot } else { Join-Path $RepositoryRoot $DogfoodRoot }
$OutputRootPath = if ([IO.Path]::IsPathRooted($OutputRoot)) { $OutputRoot } else { Join-Path $RepositoryRoot $OutputRoot }
$CliProject = Join-Path (Join-Path (Join-Path $RepositoryRoot "src") "Meridian.Cli") "Meridian.Cli.csproj"

$Cases = @(
    [pscustomobject]@{
        Name = "CleanArchitecture"
        Repository = "https://github.com/ardalis/CleanArchitecture.git"
        Directory = "CleanArchitecture"
        Commit = "d79c69852db4ddf80efe0be53355720e38820211"
        Target = "Clean.Architecture.slnx"
    },
    [pscustomobject]@{
        Name = "eShopOnWeb"
        Repository = "https://github.com/dotnet-architecture/eShopOnWeb.git"
        Directory = "eShopOnWeb"
        Commit = "4da8212117e87d808d4bbc7da6286fd2147ce606"
        Target = "eShopOnWeb.sln"
    },
    [pscustomobject]@{
        Name = "CrossMacro"
        Repository = "https://github.com/alper-han/CrossMacro.git"
        Directory = "CrossMacro"
        Commit = "fbe4fd52cd8b76449fc1f771fcc39834114540d5"
        Target = "CrossMacro.sln"
    }
)

function Invoke-CheckedCommand {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$WorkingDirectory = $RepositoryRoot
    )

    Push-Location $WorkingDirectory
    try {
        & $FilePath @ArgumentList
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($ArgumentList -join ' ')"
        }
    }
    finally {
        Pop-Location
    }
}

function Invoke-LoggedCommand {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$WorkingDirectory,
        [string]$StdoutPath,
        [string]$StderrPath
    )

    Push-Location $WorkingDirectory
    try {
        & $FilePath @ArgumentList > $StdoutPath 2> $StderrPath
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        throw "Command failed with exit code ${exitCode}: $FilePath $($ArgumentList -join ' '). See $StdoutPath and $StderrPath."
    }
}

function Ensure-Repository {
    param(
        [pscustomobject]$Case
    )

    $repoPath = Join-Path $DogfoodRootPath $Case.Directory
    if (-not (Test-Path $repoPath)) {
        Invoke-CheckedCommand "git" @("clone", $Case.Repository, $repoPath)
    }
    elseif (-not (Test-Path (Join-Path $repoPath ".git"))) {
        throw "Existing path is not a git repository: $repoPath"
    }

    $dirty = & git -C $repoPath status --porcelain
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect git status for $repoPath."
    }

    if ($dirty) {
        throw "$repoPath has uncommitted changes. Commit, stash, or remove them before running the dogfood baseline."
    }

    Invoke-CheckedCommand "git" @("-C", $repoPath, "fetch", "--all", "--tags")
    Invoke-CheckedCommand "git" @("-C", $repoPath, "checkout", "--detach", $Case.Commit)

    return $repoPath
}

New-Item -ItemType Directory -Force -Path $DogfoodRootPath | Out-Null
New-Item -ItemType Directory -Force -Path $OutputRootPath | Out-Null

Write-Host "Building Meridian CLI ($Configuration)..."
Invoke-CheckedCommand "dotnet" @("build", $CliProject, "-c", $Configuration)

$Rows = foreach ($case in $Cases) {
    Write-Host "Running dogfood baseline: $($case.Name)"
    $repoPath = Ensure-Repository $case
    $targetPath = Join-Path $repoPath $case.Target
    $caseOutput = Join-Path $OutputRootPath $case.Name
    New-Item -ItemType Directory -Force -Path $caseOutput | Out-Null

    Invoke-LoggedCommand "dotnet" @("restore", $targetPath) $repoPath (Join-Path $caseOutput "restore.stdout.log") (Join-Path $caseOutput "restore.stderr.log")

    $scanArgs = @("run", "--project", $CliProject, "-c", $Configuration, "--", "scan", $targetPath, "--output", $caseOutput, "--metrics")
    if ($IncludeTests) {
        $scanArgs += "--include-tests"
    }
    if ($TrustProject) {
        $scanArgs += "--trust-project"
    }

    Invoke-LoggedCommand "dotnet" $scanArgs $RepositoryRoot (Join-Path $caseOutput "scan.stdout.log") (Join-Path $caseOutput "scan.stderr.log")

    $graphPath = Join-Path $caseOutput "graph.json"
    $metricsPath = Join-Path $caseOutput "metrics.json"
    $summaryArgs = @("run", "--project", $CliProject, "-c", $Configuration, "--", "agent-summary", "--graph", $graphPath, "--budget", "compact")
    Invoke-LoggedCommand "dotnet" $summaryArgs $RepositoryRoot (Join-Path $caseOutput "agent-summary.txt") (Join-Path $caseOutput "agent-summary.stderr.log")

    $metrics = Get-Content $metricsPath -Raw | ConvertFrom-Json
    [pscustomobject]@{
        Case = $case.Name
        Nodes = [int]$metrics.node_count
        Edges = [int]$metrics.edge_count
        Diagnostics = [int]$metrics.diagnostic_count
        TotalMs = [int64]$metrics.total_ms
        Graph = $graphPath
        Metrics = $metricsPath
    }
}

$Rows | Format-Table -AutoSize
