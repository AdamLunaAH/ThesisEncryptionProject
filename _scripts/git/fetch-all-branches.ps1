# PowerShell script to fetch all remote branches and create local tracking branches
# Run this script with: .\fetch-all-branches.ps1

Write-Host "Fetching all remote branches..." -ForegroundColor Green

# Fetch all remote branches
git fetch --all

Write-Host "`nCreating local tracking branches for all remote branches..." -ForegroundColor Yellow

# Get all remote branches (excluding HEAD pointer)
$remoteBranches = git branch -r | Where-Object { $_ -notmatch 'HEAD' }

foreach ($remoteBranch in $remoteBranches) {
    # Clean up the branch name (remove whitespace and 'origin/' prefix)
    $branchName = $remoteBranch.Trim().Replace('origin/', '')
    
    # Check if local branch already exists
    $localBranchExists = git branch --list $branchName
    
    if ($localBranchExists) {
        Write-Host "Local branch '$branchName' already exists, skipping..." -ForegroundColor Cyan
    } else {
        Write-Host "Creating local branch '$branchName' tracking 'origin/$branchName'..." -ForegroundColor White
        git checkout -b $branchName "origin/$branchName"
    }
}

Write-Host "`nSwitching back to main branch..." -ForegroundColor Yellow
git checkout main

Write-Host "`nAll local branches:" -ForegroundColor Cyan
git branch

Write-Host "`nScript complete! All remote branches now have local tracking branches." -ForegroundColor Green
