<#
.SYNOPSIS
    Publishes a local WebGL build to the gh-pages branch.

.DESCRIPTION
    GitHub Pages serves whatever sits at the root of gh-pages, so by default this replaces that
    branch's contents with Build/WebGL and pushes. History on gh-pages is not interesting - it is
    build output, not source - so each publish is a single commit rather than a growing pile of
    binary diffs.

    Pass -Subdirectory to publish a second build alongside the main one instead of over it: the
    build lands in that folder, everything already on the branch is left exactly as it is, and both
    are reachable at the same time (/ and /<subdirectory>/). That is what a work-in-progress branch
    wants - somewhere to be looked at without taking down the version people are already using.

    Build first:
      Unity.exe -batchmode -quit -projectPath . -buildTarget WebGL `
                -executeMethod ColosseumDuel.EditorTools.ProjectBootstrap.BuildWebGL

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/publish-webgl.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/publish-webgl.ps1 -Subdirectory iteration1
#>
param(
    [string]$BuildDir = "Build/WebGL",
    [string]$Branch = "gh-pages",
    [string]$Remote = "origin",
    [string]$Subdirectory = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (git rev-parse --show-toplevel)
if (-not $repoRoot) { Write-Error "Not inside a git repository." }
Set-Location $repoRoot

if ($Subdirectory -match '[\\/:]' ) {
    Write-Error "-Subdirectory must be a single folder name, not a path."
}

$buildPath = Join-Path $repoRoot $BuildDir
if (-not (Test-Path -LiteralPath (Join-Path $buildPath "index.html"))) {
    Write-Error "No index.html in $buildPath - build the game first (see the help in this file)."
}

$publishingBeside = -not [string]::IsNullOrWhiteSpace($Subdirectory)

# A worktree keeps the checked-out source tree untouched: no stashing, no risk of committing
# build output onto main by accident.
$worktree = Join-Path ([System.IO.Path]::GetTempPath()) ("colosseum-pages-" + [guid]::NewGuid().ToString("N").Substring(0, 8))

# Publishing to the root starts from an empty worktree on purpose: staging the fresh build then
# records every file the last publish left behind as a deletion, which is what "replace the site"
# means. Publishing beside it has to do the opposite and check the branch out, or the same `git
# add -A` would delete the build already sitting at the root.
$checkoutArgs = if ($publishingBeside) { @() } else { @("--no-checkout") }

$branchExistsRemotely = (git ls-remote --heads $Remote $Branch)
if ($branchExistsRemotely) {
    git fetch $Remote "${Branch}:refs/remotes/$Remote/$Branch" 2>&1 | Out-Null
    git worktree add @checkoutArgs -B $Branch $worktree "refs/remotes/$Remote/$Branch" | Out-Null
} elseif ($publishingBeside) {
    Write-Error "$Branch does not exist yet on $Remote - publish the main build to its root first."
} else {
    git worktree add --no-checkout --detach $worktree | Out-Null
    Push-Location $worktree
    git checkout --orphan $Branch | Out-Null
    Pop-Location
}

try {
    Push-Location $worktree

    $destination = $worktree
    if ($publishingBeside) {
        $destination = Join-Path $worktree $Subdirectory
        if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
        New-Item -ItemType Directory -Path $destination | Out-Null
    } else {
        Get-ChildItem -Force | Where-Object { $_.Name -ne ".git" } | Remove-Item -Recurse -Force
    }

    Copy-Item -Path (Join-Path $buildPath "*") -Destination $destination -Recurse -Force

    # Without this, Pages runs the build through Jekyll, which drops files it does not like.
    New-Item -ItemType File -Path (Join-Path $worktree ".nojekyll") -Force | Out-Null

    git add -A
    if (-not (git status --porcelain)) {
        Write-Host "Nothing changed since the last publish."
        return
    }

    $sourceCommit = (git -C $repoRoot rev-parse --short HEAD)
    $where = if ($publishingBeside) { "$Branch/$Subdirectory" } else { $Branch }
    git commit -q -m "Publish WebGL build from $sourceCommit to $where"
    git push -q $Remote $Branch
    Write-Host "Published $where from source commit $sourceCommit."
}
finally {
    Pop-Location -ErrorAction SilentlyContinue
    git worktree remove --force $worktree 2>&1 | Out-Null
}
