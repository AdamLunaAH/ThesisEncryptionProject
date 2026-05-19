# PowerShell Script to Generate and Trust Development Certificate for HTTPS
# This script sets up a development certificate for ASP.NET Core applications
# to work with HTTPS in browsers like Edge, Chrome, etc.

# EXECUTION POLICY FIX - Run one of these commands first if you get execution policy errors:
# Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

# exec example
#.\generate-dev-certificate.ps1

Write-Host "Starting development certificate setup..." -ForegroundColor Green

# Clean any existing certificates
Write-Host "Cleaning existing development certificates..." -ForegroundColor Yellow
dotnet dev-certs https --clean

# Generate and trust new certificate
Write-Host "Generating and trusting new development certificate..." -ForegroundColor Yellow
dotnet dev-certs https --trust

# Verify the certificate
Write-Host "Verifying the certificate installation..." -ForegroundColor Yellow
dotnet dev-certs https --check

Write-Host "Development certificate setup completed!" -ForegroundColor Green
Write-Host "Your ASP.NET Core application should now work with HTTPS in Edge and other browsers." -ForegroundColor Cyan
