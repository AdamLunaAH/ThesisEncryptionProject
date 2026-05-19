# PowerShell script to check for outdated NuGet packages
# Run this script with: .\nuget-update.ps1

Write-Host "Checking for outdated NuGet packages..." -ForegroundColor Green

# Change to parent directory
Set-Location ../..

Write-Host "`n=== Checking for outdated packages ===" -ForegroundColor Yellow
dotnet list package --outdated

Write-Host "`n=== Additional Commands (commented out) ===" -ForegroundColor Cyan
Write-Host "To clear local NuGet cache, run:" -ForegroundColor White
Write-Host "dotnet nuget locals all --clear" -ForegroundColor Gray

Write-Host "`nTo update Entity Framework Core tools, run:" -ForegroundColor White
Write-Host "dotnet tool update --global dotnet-ef" -ForegroundColor Gray

Write-Host "`nPackage check complete!" -ForegroundColor Green


#clear the local nuget cache
#dotnet nuget locals all --clear

#update efc
#dotnet tool update --global dotnet-ef
