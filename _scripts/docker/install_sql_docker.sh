#!/bin/bash
# Bash script to install and run SQL Server, MariaDB, and PostgreSQL Docker containers
# Run this script with: sudo chmod +x ./install_sql_docker.sh && ./install_sql_docker.sh

# SQL SERVER 2022
##################
echo "Setting up SQL Server 2022 container..."

# Pull the container image to my computer
docker pull mcr.microsoft.com/mssql/server:2022-latest

# Install and run the container 
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=skYhgS@83#aQ" -p 14333:1433 --name sql2022container --hostname sql2022 -d mcr.microsoft.com/mssql/server:2022-latest

echo "SQL Server 2022 connection string:"
echo "Data Source=localhost,14333;Persist Security Info=True;User ID=sa;Pwd=skYhgS@83#aQ;Encrypt=False;"


# PostgreSQL
############
echo
echo "Setting up PostgreSQL container..."

# Pull the container image to my computer
docker pull postgres

# Create a database container and run the docker 
docker run --name postgrescontainer -e POSTGRES_PASSWORD=skYhgS@83#aQ -d -p 5432:5432 postgres

echo "PostgreSQL connection string:"
echo "Server=localhost;Port=5432;User Id=postgres;Password=skYhgS@83#aQ;"

echo
echo "All database containers have been created and started!"
echo "You can check the status with: docker ps"
