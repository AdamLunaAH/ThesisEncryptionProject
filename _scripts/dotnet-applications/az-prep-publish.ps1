# az-prep-publish.ps1
# PowerShell script to prepare and publish the application (translation of az-prep-publish.sh)

# Usage:
# .\az-prep-publish.ps1 ..\AppWebApi ..\Configuration\Configuration.csproj

param(
    [Parameter(Mandatory = $true)]
    [string]$ApplicationPath,
    [Parameter(Mandatory = $true)]
    [string]$ProjectFilePath
)

$ApplicationToPublish = Resolve-Path $ApplicationPath | Select-Object -ExpandProperty Path
$ProjectFile = Resolve-Path $ProjectFilePath | Select-Object -ExpandProperty Path

$SettingsFile = "$ApplicationToPublish\appsettings.json"
$ApplicationSettingsFile = Resolve-Path $SettingsFile | Select-Object -ExpandProperty Path

try {
    # Use regex to extract User Secret GUID from .csproj file
    $ProjectContent = Get-Content $ProjectFile -Raw
    $UsrSecId = [regex]::Match($ProjectContent, '<UserSecretsId>(.*?)</UserSecretsId>').Groups[1].Value

    # Construct the user secret file path
    $UserSecretBasePath = & "..\dotnet\user-secret-path.ps1"
    $UsrSecFile = "$UserSecretBasePath\$UsrSecId\secrets.json"

    # Check if AzureKeyVault:kvAccessParamsFile tag is set in the JSON file
    # If set, use that file as the kvAccessParamsFile
    # If not set, use the UsrSecFile as the kvAccessParamsFile
    if (Test-Path $UsrSecFile) {
        $UserSecretsContent = Get-Content $UsrSecFile -Raw | ConvertFrom-Json
        $kvAccessParamsFromJson = $UserSecretsContent.AzureKeyVault.kvAccessParamsFile
        if ($kvAccessParamsFromJson) {
            $kvAccessParamsFile = $kvAccessParamsFromJson
        } else {
            $kvAccessParamsFile = $UsrSecFile
        }
    } else {
        $kvAccessParamsFile = $UsrSecFile
    }

    # Helper function to get a value from JSON supporting both formats
    # Format 1: Nested objects (e.g., AzureKeyVault.kvAccessParams.kvUri)
    # Format 2: Flat colon-separated keys (e.g., "AzureKeyVault:kvAccessParams:kvUri")
    function Get-ConfigValue {
        param(
            [PSObject]$ConfigObject,
            [string]$NestedPath,
            [string]$FlatKeyPath
        )

        # Try nested object format first
        $value = $ConfigObject
        foreach ($part in $NestedPath.Split('.')) {
            $value = $value.$part
            if ($null -eq $value) { break }
        }

        # If nested format didn't work, try flat colon-separated keys
        if ($null -eq $value -or $value -eq '') {
            $value = $ConfigObject."$FlatKeyPath"
        }

        return $value
    }

    # Step1: Set the Azure Keyvault access parameters as operating system environment variables.
    Write-Host "`n`nSetting the azure key vault access as environment variables"
    Write-Host "using kvAccessParamsFile: $kvAccessParamsFile"

    # Read parameters from JSON file (supports both nested and flat key formats)
    $kvAccessContent = Get-Content $kvAccessParamsFile -Raw | ConvertFrom-Json

    $env:AzureKeyVault_kvAccessParams_readerSecrets_tenant = Get-ConfigValue -ConfigObject $kvAccessContent -NestedPath "AzureKeyVault.kvAccessParams.readerSecrets.tenant" -FlatKeyPath "AzureKeyVault:kvAccessParams:readerSecrets:tenant"
    $env:AzureKeyVault_kvAccessParams_kvUri = Get-ConfigValue -ConfigObject $kvAccessContent -NestedPath "AzureKeyVault.kvAccessParams.kvUri" -FlatKeyPath "AzureKeyVault:kvAccessParams:kvUri"
    $env:AzureKeyVault_kvAccessParams_kvSecret = Get-ConfigValue -ConfigObject $kvAccessContent -NestedPath "AzureKeyVault.kvAccessParams.kvSecret" -FlatKeyPath "AzureKeyVault:kvAccessParams:kvSecret"
    $env:AzureKeyVault_kvAccessParams_readerSecrets_appId = Get-ConfigValue -ConfigObject $kvAccessContent -NestedPath "AzureKeyVault.kvAccessParams.readerSecrets.appId" -FlatKeyPath "AzureKeyVault:kvAccessParams:readerSecrets:appId"
    $env:AzureKeyVault_kvAccessParams_readerSecrets_password = Get-ConfigValue -ConfigObject $kvAccessContent -NestedPath "AzureKeyVault.kvAccessParams.readerSecrets.password" -FlatKeyPath "AzureKeyVault:kvAccessParams:readerSecrets:password"

    # Verify environment variables
    Write-Host "AzureKeyVault_kvAccessParams_readerSecrets_tenant=" $env:AzureKeyVault_kvAccessParams_readerSecrets_tenant
    Write-Host "AzureKeyVault_kvAccessParams_kvUri=" $env:AzureKeyVault_kvAccessParams_kvUri
    Write-Host "AzureKeyVault_kvAccessParams_kvSecret=" $env:AzureKeyVault_kvAccessParams_kvSecret
    Write-Host "AzureKeyVault_kvAccessParams_readerSecrets_appId=" $env:AzureKeyVault_kvAccessParams_readerSecrets_appId
    Write-Host "AzureKeyVault_kvAccessParams_readerSecrets_password=" $env:AzureKeyVault_kvAccessParams_readerSecrets_password


    # Step2: Default data user must be gstusr in production and AzureKeyVault as Secret Storage
    Write-Host "`n`nPrepare appsettings.json for production..."
    $Content = Get-Content $ApplicationSettingsFile -Raw
    #$UpdatedContent = $Content -replace '"DefaultDataUser":\s*"[^"]*"', '"DefaultDataUser": "gstusr"'
    $UpdatedContent = $Content -replace '"SecretStorage":\s*"[^"]*"', '"SecretStorage": "AzureKeyVault"'
    Set-Content $ApplicationSettingsFile $UpdatedContent

    # Step3: Generate the release files
    Write-Host "`n`nPublish the webapi..."

    # Remove any previous publish
    $PublishPath = "$ApplicationToPublish\publish"
    if (Test-Path $PublishPath) {
        Remove-Item $PublishPath -Recurse -Force
    }

    Push-Location $ApplicationToPublish
    dotnet publish --configuration Release --output .\publish
    Pop-Location

    # Step4: Ensure any previous instances are stopped
    Write-Host "`n`nEnsure any previous instances are stopped..."

    # Kill any processes using port 5001
    try {
        $ProcessIds = Get-NetTCPConnection -LocalPort 5001 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess
        if ($ProcessIds) {
            foreach ($ProcessId in $ProcessIds) {
                Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
            }
            Write-Host "Stopped existing processes on port 5001"
        }
    }
    catch {
        Write-Host "No processes found on port 5001"
    }

    # Step5: Run the application from the folder containing the release files
    Write-Host "`n`nRun the webapi from the published directory..."
    Push-Location "$ApplicationToPublish\publish"

    $env:ASPNETCORE_URLS = "https://localhost:5001"

    # Find the executable file in the current directory
    # Look for .exe files that are not .dll, .json, etc.
    $ExecutableName = Get-ChildItem -Path . -File | Where-Object {
        $_.Extension -eq ".exe" -and
        $_.Name -notlike "*.dll" -and
        $_.Name -notlike "*.json" -and
        $_.Name -notlike "*.pdb" -and
        $_.Name -notlike "*.deps.json" -and
        $_.Name -notlike "*.runtimeconfig.json"
    } | Select-Object -First 1 -ExpandProperty Name

    if (-not $ExecutableName) {
        Write-Host "Error: No executable found in the current directory"
        Pop-Location
        exit 1
    }

    Write-Host "Executing application: $ExecutableName"
    & ".\$ExecutableName"

    Pop-Location
}
catch {
    Write-Host "`n`nError: $($_.Exception.Message)`n"
    exit 1
}
