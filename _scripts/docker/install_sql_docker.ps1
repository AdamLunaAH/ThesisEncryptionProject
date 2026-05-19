# PowerShell script to install and run SQL Server, MariaDB, and PostgreSQL Docker containers
# Run this script with: .\install_sql_docker.ps1

# PostgreSQL
############
Write-Host "`nSetting up PostgreSQL container..." -ForegroundColor Green

# Pull the container image to my computer
docker pull postgres

# Create a database container and run the docker
docker run --name postgresencryptioncontainer -e POSTGRES_PASSWORD=skYhgS@83#aQ -d -p 5432:5432 postgres

Write-Host "PostgreSQL connection string:" -ForegroundColor Yellow
Write-Host "Server=localhost;Port=5432;User Id=postgres;Password=skYhgS@83#aQ;" -ForegroundColor Cyan

Write-Host "`nAll database containers have been created and started!" -ForegroundColor Green
Write-Host "You can check the status with: docker ps" -ForegroundColor White
