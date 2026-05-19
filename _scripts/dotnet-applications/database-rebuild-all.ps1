# För att göra .ps1-filen körbar, kör följande kommando i PowerShell (Behöver bara köras första gången):
# Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser


#If EFC tools needs update use:
#dotnet tool update --global dotnet-ef

# To execute:
# .\database-rebuild-all.ps1 databasename [sqlserver|mysql|postgresql] [docker|azure|loopia] [root|dbo|supusr|usr|gstusr] [appsettingsFolder] [dbContextFolder]

# ./database-rebuild-all.ps1 sql-encryption postgresql docker dbo ../../0.App.WebApiEncryption ../../3b.DataAccess.DbContext

# example:
# .\database-rebuild-all.ps1 sql-encryption sqlserver docker dbo ../AppWebApi ../DbContext
# .\database-rebuild-all.ps1 sql-encryption sqlserver docker dbo ../AppRazor ../DbContext
# .\database-rebuild-all.ps1 sql-encryption sqlserver docker dbo ../AppMvc ../DbContext

param(
    [Parameter(Mandatory=$true)]
    [string]$DatabaseName,

    [Parameter(Mandatory=$true)]
    [ValidateSet("postgresql")]
    [string]$DatabaseType,

    [Parameter(Mandatory=$true)]
    [ValidateSet("docker")]
    [string]$DeploymentTarget,

    [Parameter(Mandatory=$true)]
    [ValidateSet("root", "dbo", "supusr", "usr", "gstusr")]
    [string]$UserLevel,

    [Parameter(Mandatory=$true)]
    [string]$AppSettingsFolder,

    [Parameter(Mandatory=$true)]
    [string]$DbContextFolder
)

# Resolve absolute path for AppSettingsFolder
$AppSettingsFolder = Resolve-Path $AppSettingsFolder | Select-Object -ExpandProperty Path

# Resolve absolute path for DbContextFolder
$DbContextFolder = Resolve-Path $DbContextFolder | Select-Object -ExpandProperty Path

# folder that contains assembly output
$EfcAssemblyFolder = Join-Path $AppSettingsFolder "bin/Debug/net10.0"

#Set Database Context
switch ($DatabaseType) {
    "postgresql" { $DBContext = "PostgresDbContext" }
}

#set UseDataSetWithTag to "<db_name>.<db_type>.<env>" in appsettings.json
$AppSettingsPath = Join-Path $AppSettingsFolder "appsettings.json"
$pattern = '"UseDataSetWithTag"\s*:\s*"[^"]*"'
$replacement = '"UseDataSetWithTag": "' + $DatabaseName + '.' + $DatabaseType + '.' + $DeploymentTarget + '"'
(Get-Content -Path $AppSettingsPath) -replace $pattern, $replacement | Set-Content -Path $AppSettingsPath

#set DefaultDataUser to specified user level in appsettings.json
$Content = Get-Content $AppSettingsPath -Raw
$UpdatedContent = $Content -replace '"DefaultDataUser":\s*"[^"]*"', ('"DefaultDataUser": "' + $UserLevel + '"')
Set-Content $AppSettingsPath $UpdatedContent

if ($DeploymentTarget -eq "docker") {
    #drop any database
    $env:EFC_AppSettingsFolder = $AppSettingsFolder
    $env:EFC_AssemblyFolder = $EfcAssemblyFolder
    dotnet ef database drop -f -c $DBContext -p $DbContextFolder -s $DbContextFolder
}

#remove any migration
Remove-Item -Recurse -Force (Join-Path $DbContextFolder "Migrations/$DBContext") -ErrorAction SilentlyContinue

#make a full new migration
$env:EFC_AppSettingsFolder = $AppSettingsFolder
$env:EFC_AssemblyFolder = $EfcAssemblyFolder
dotnet ef migrations add miInitial -c $DBContext -p $DbContextFolder -s $DbContextFolder -o (Join-Path $DbContextFolder "Migrations/$DBContext")

#update the database from the migration
$env:EFC_AppSettingsFolder = $AppSettingsFolder
$env:EFC_AssemblyFolder = $EfcAssemblyFolder
dotnet ef database update -c $DBContext -p $DbContextFolder -s $DbContextFolder

#to initialize the database you need to run the sql scripts
#../DbContext/SqlScripts/<db_type>/initDatabase.sql
# ../../3b.DataAccess.DbContext/SqlScripts/postgres/initDatabase.sql

#to initialize the database in postgres you need to run the sql scripts
#../DbContext/SqlScripts/postgres/initDatabaseWithOrleans.sql

