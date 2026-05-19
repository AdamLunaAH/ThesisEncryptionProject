# Script to remove all bin and obj folders from the solution

# Cleanup commands:
# 1. To preview only (default): .\cleanup.ps1
# 2. To actually delete folders: .\cleanup.ps1 -NoDryRun
# 3. To skip confirmation: .\cleanup.ps1 -NoDryRun -Force
# 4. To not wait before exit: .\cleanup.ps1 -NoDryRun -NoPause
# 5. To set max search depth: .\cleanup.ps1 -NoDryRun -Depth 5
# Depth means how many levels to search upwards for the solution root.
# 6. To show help: .\cleanup.ps1 -Help

# Run this to delete the bin and obj folders in all projects: .\cleanup.ps1 -NoDryRun
# Note: It might auto-generate the files again or not able to delete the files if you have the solution open in Visual Studio or VS Code. To fix this, run the script from a terminal in the cleanup folder before running this script, eg. run it in PowerShell in file explorer.

param(
    [switch]$NoDryRun,
    [switch]$DryRun,
    [switch]$Force,
    [switch]$NoPause,
    [int]$Depth = 4,
    [switch]$Help
)

# =========================
# HELP
# =========================

if ($Help) {
    Write-Host "Usage: .\cleanup.ps1 [options]" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Options:"
    Write-Host "  -DryRun        Preview only (default)"
    Write-Host "  -NoDryRun      Actually delete folders"
    Write-Host "  -Force         Skip confirmation prompt"
    Write-Host "  -NoPause       Do not wait before exit"
    Write-Host "  -Depth <n>     Max levels to search upwards (default: 4)"
    Write-Host "  -Help          Show this help message"
    return
}

# =========================
# CONFIGURATION
# =========================

$DRY_RUN = $true
if ($NoDryRun) { $DRY_RUN = $false }
if ($DryRun) { $DRY_RUN = $true }

$REQUIRE_CONFIRMATION = -not $Force
$PAUSE_ON_EXIT = -not $NoPause
$MAX_SEARCH_DEPTH = $Depth

$FOLDERS = @("bin", "obj")

# =========================
# FIND SOLUTION ROOT
# =========================

$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-SolutionRoot {
    param (
        [string]$StartDir,
        [int]$MaxDepth
    )

    $dir = $StartDir
    $depth = 0

    while ($dir -and $depth -le $MaxDepth) {

        $hasSln = Get-ChildItem -Path $dir -Filter *.sln -ErrorAction SilentlyContinue
        $hasSlnx = Get-ChildItem -Path $dir -Filter *.slnx -ErrorAction SilentlyContinue
        $hasGit = Test-Path (Join-Path $dir ".git")

        if ($hasSln -or $hasSlnx -or $hasGit) {
            return $dir
        }

        $parent = Split-Path -Parent $dir
        if ($parent -eq $dir) { break }

        $dir = $parent
        $depth++
    }

    Write-Warning "Solution root not found within depth ($MaxDepth). Using script directory."
    return $StartDir
}

$ROOT_DIR = Find-SolutionRoot -StartDir $SCRIPT_DIR -MaxDepth $MAX_SEARCH_DEPTH

# =========================
# START
# =========================

Write-Host "=== Solution Cleaner ===" -ForegroundColor Cyan
Write-Host "Root directory: $ROOT_DIR"
Write-Host "Max search depth: $MAX_SEARCH_DEPTH"

if ($DRY_RUN) {
    Write-Host "Mode: DRY RUN (no files will be deleted)" -ForegroundColor Yellow
} else {
    Write-Host "Mode: LIVE (files will be deleted)" -ForegroundColor Red
}

# Confirmation
if ($REQUIRE_CONFIRMATION) {
    $response = Read-Host "Proceed with cleaning? (y/n)"
    if ($response -ne "y") {
        Write-Host "Operation cancelled." -ForegroundColor Red
        if ($PAUSE_ON_EXIT) { Read-Host "Press Enter to exit" }
        return
    }
}

$deletedCount = 0

foreach ($folder in $FOLDERS) {

    Get-ChildItem -Path $ROOT_DIR -Recurse -Directory -Filter $folder -ErrorAction SilentlyContinue |
    Where-Object {
        $_.FullName -notmatch "\\.git\\"
    } |
    ForEach-Object {

        if ($DRY_RUN) {
            Write-Host "[DRY RUN] Would remove: $($_.FullName)" -ForegroundColor DarkYellow
        }
        else {
            try {
                Write-Host "Removing: $($_.FullName)" -ForegroundColor Yellow
                Remove-Item $_.FullName -Recurse -Force -ErrorAction Stop
                $deletedCount++
            }
            catch {
                Write-Host "Failed to remove: $($_.FullName)" -ForegroundColor Red
            }
        }
    }
}

if (-not $DRY_RUN) {
    Write-Host "Deleted $deletedCount folders." -ForegroundColor Green
}
else {
    Write-Host "Dry run complete. No folders were deleted." -ForegroundColor Cyan
}

Write-Host "=== Done ===" -ForegroundColor Cyan

# Prevent console from closing
if ($PAUSE_ON_EXIT) {
    Read-Host "Press Enter to exit"
}