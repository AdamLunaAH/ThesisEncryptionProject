# CLI Scripts

Script runs related to the application.

## Azure Key Vault

Run in the `_scripts/dotnet-applications` folder

### Update Azure Key Vault

```bash
#App.Worker
./az-kv-update.sh ../azure/az-resource-params/lia2026 '/Users/Martin/Development/projects/LIA_0226/SeidoSwStack/0.App.Worker/App.Worker.csproj'

#App.AzureFunction
./az-kv-update.sh ../azure/az-resource-params/lia2026 '/Users/Martin/Development/projects/LIA_0226/SeidoSwStack/0.App.AzureFunction/App.AzureFunction.csproj'

#App.WebApiEncryption
./az-kv-update.sh ../azure/az-resource-params/lia2026 '/Users/Martin/Development/projects/LIA_0226/SeidoSwStack/0.App.WebApiEncryption/App.WebApiEncryption.csproj'

```

## Database

Run in the `_scripts/dotnet-applications` folder

### Build a code first database using EFC with default dataaccess user

```bash
#sqlserver docker
./database-rebuild-all.sh  sqlserver docker dbo '/Users/Martin/Development/projects/LIA_0226/SeidoSwStack/0.App.Worker' '/Users/Martin/Development/projects/LIA_0226/SeidoSwStack/3b.DataAccess.DbContext'

#sqlserver azure
./database-rebuild-all.sh  sqlserver azure dbo '/Users/Martin/Development/projects/LIA_0226/SeidoSwStack/0.App.Worker' '/Users/Martin/Development/projects/LIA_0226/SeidoSwStack/3b.DataAccess.DbContext'

#postgresql
./database-rebuild-all.sh  postgresql docker dbo '/Users/Martin/Development/projects/LIA_0226/SeidoSwStack/0.App.Worker' '/Users/Martin/Development/projects/LIA_0226/SeidoSwStack/3b.DataAccess.DbContext'

#postgresql azure
./database-rebuild-all.sh  postgresql azure dbo '/Users/Martin/Development/projects/LIA_0226/SeidoSwStack/0.App.Worker' '/Users/Martin/Development/projects/LIA_0226/SeidoSwStack/3b.DataAccess.DbContext'

#mysql does not work until pomelo team updated nuget for .net10

```

```powershell
#sqlserver
.\database-rebuild-all.ps1  sqlserver docker dbo 'path to 0.App.Worker' 'path to 3b.DataAccess.DbContext'

#sqlserver azure
.\database-rebuild-all.ps1  sqlserver azure dbo 'path to 0.App.Worker' 'path to 3b.DataAccess.DbContext'

#postgresql
.\database-rebuild-all.ps1  postgresql docker dbo 'path to 0.App.Worker' 'path to 3b.DataAccess.DbContext'

#postgresql azure
.\database-rebuild-all.ps1  postgresql azure dbo 'path to 0.App.Worker' 'path to 3b.DataAccess.DbContext'


#mysql does not work until pomelo team updated nuget for .net10

```

## NuGet

Run in the `_scripts/dotnet` folder

### NuGet update

```bash
./nuget-update.sh '/Users/Martin/Development/projects/LIA_0226/SeidoSwStack'
```

```powershell
.\nuget-update.ps1 'path to SeidoSwStack'
```

## Production mode

Run in the `_scripts/dotnet-applications` folder

### Run in Production mode

```bash
#App.Worker
./az-prep-publish.sh '/Users/Martin/Development/projects/LIA_0226/SeidoSwStack/0.App.Worker' '/Users/Martin/Development/projects/LIA_0226/SeidoSwStack/0.App.Worker/App.Worker.csproj'

#App.WebApiEncryption
./az-prep-publish.sh 'C:\_dev\SeidoSwStackClean\SeidoSwStack\0.App.WebApiEncryption' 'C:\_dev\SeidoSwStackClean\SeidoSwStack\0.App.WebApiEncryption/App.WebApiEncryption.csproj'

```

## Other

Run in any terminal

### Close ports

```bash
lsof -ti:7071 | xargs kill -9
lsof -i :7258 | grep LISTEN | awk '{print $2}' | xargs kill -9
```
