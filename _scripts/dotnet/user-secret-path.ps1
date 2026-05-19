# get-user-secrets-folder.ps1
# Cross-platform PowerShell script to get user secrets base folder

# Run this script with: .\user-secret-path

function Get-UserSecretsFolder {
    if ($IsWindows -or $env:OS -eq "Windows_NT") {
        # Windows
        return Join-Path $env:APPDATA "Microsoft\UserSecrets"
    } else {
        # macOS/Linux
        return Join-Path $env:HOME ".microsoft/usersecrets"
    }
}

# Usage
$UserSecretsBase = Get-UserSecretsFolder
Write-Output $UserSecretsBase