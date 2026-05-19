#!/bin/bash
#To make the .sh file executable
#sudo chmod +x ./az-prep-publish.sh

# To execute:
# ./az-prep-publish.sh ../AppWebApi ../Configuration/Configuration.csproj

# Exit immediately if any command fails
set -e

#Check inital parameters
if [ -z "$1" ] || [ -z "$2" ]; then
    printf "\nParameter error\n"
    exit 1
fi


ApplicationToPublish=$(realpath "$1")
ProjectFile=$(realpath "$2")

SettingsFile="$ApplicationToPublish/appsettings.json"
ApplicationSettingsFile=$(realpath $SettingsFile)


#use sed to extract User Secret GUID from cs.proj
UsrSecId=$(sed -n 's:.*<UserSecretsId>\(.*\)</UserSecretsId>.*:\1:p' "$ProjectFile")

#construct the user secret file path
UsrSecFile=$(../dotnet/user-secret-path.sh)/$UsrSecId/secrets.json

# Check if AzureKeyVault:kvAccessParamsFile tag is set in the JSON file
# If set, use that file as the kvAccessParamsFile
# If not set, use the UsrSecFile as the kvAccessParamsFile
if [[ -f "$UsrSecFile" ]]; then
    kvAccessParamsFromJson=$(jq -r '.AzureKeyVault.kvAccessParamsFile // empty' "$UsrSecFile")
    if [[ -n "$kvAccessParamsFromJson" ]]; then
        kvAccessParamsFile="$kvAccessParamsFromJson"
    else
        kvAccessParamsFile=$UsrSecFile
    fi
else
    kvAccessParamsFile=$UsrSecFile
fi

#Step1: Set the Azure Keyvault access parameters as operating system environment variables.
printf "\n\nSetting the azure key vault access as environent variables"
printf "\nusing kvAccessParamsFile: $kvAccessParamsFile\n"
export AzureKeyVault_kvAccessParams_readerSecrets_tenant=$(jq -r '.AzureKeyVault.kvAccessParams.readerSecrets.tenant // empty' "$kvAccessParamsFile")
export AzureKeyVault_kvAccessParams_kvUri=$(jq -r '.AzureKeyVault.kvAccessParams.kvUri // empty' "$kvAccessParamsFile")
export AzureKeyVault_kvAccessParams_kvSecret=$(jq -r '.AzureKeyVault.kvAccessParams.kvSecret // empty' "$kvAccessParamsFile")

export AzureKeyVault_kvAccessParams_readerSecrets_appId=$(jq -r '.AzureKeyVault.kvAccessParams.readerSecrets.appId // empty' "$kvAccessParamsFile")
export AzureKeyVault_kvAccessParams_readerSecrets_password=$(jq -r '.AzureKeyVault.kvAccessParams.readerSecrets.password // empty' "$kvAccessParamsFile")
cd $PWDIR

#verify environment variables
echo "AzureKeyVault_kvAccessParams_readerSecrets_tenant=" $AzureKeyVault_kvAccessParams_readerSecrets_tenant
echo "AzureKeyVault_kvAccessParams_kvUri=" $AzureKeyVault_kvAccessParams_kvUri
echo "AzureKeyVault_kvAccessParams_kvSecret=" $AzureKeyVault_kvAccessParams_kvSecret
echo "AzureKeyVault_kvAccessParams_readerSecrets_appId=" $AzureKeyVault_kvAccessParams_readerSecrets_appId
echo "AzureKeyVault_kvAccessParams_readerSecrets_password=" $AzureKeyVault_kvAccessParams_readerSecrets_password

#Step2: Default data user must be gstusr in production and AzureKeyVault as Secret Storage
printf "\n\nPrepare appsettings.json for production...\n"
#sed -i '' 's/"DefaultDataUser":[[:space:]]*"[^"]*"/"DefaultDataUser": "gstusr"/g' $ApplicationSettingsFile
sed -i '' 's/"SecretStorage":[[:space:]]*"[^"]*"/"SecretStorage": "AzureKeyVault"/g' $ApplicationSettingsFile

#Step3: Generate the release files
printf "\n\nPublish the webapi...\n"
# #remove any previous publish
rm -rf $ApplicationToPublish/publish

PWDIR=$(pwd)
echo $PWDIR
cd $ApplicationToPublish
dotnet publish --configuration Release --output ./publish

#Step4: Run the application from the folder containing the release files.
printf "\n\nEnsure any previous instances are stopped...\n"
lsof -ti tcp:5001 | xargs kill 

#Step5: Run the application from the folder containing the release files.
printf "\n\nRun the webapi from the published directory...\n"
cd ./publish

export ASPNETCORE_URLS="https://localhost:5001"

# Find the executable file in the current directory
# Look for files without extensions that are not .dll, .json, etc.
ExecutableName=$(find . -maxdepth 1 -type f -perm +111 \( ! -name "*.dll" ! -name "*.json" ! -name "*.pdb" ! -name "*.so" ! -name "*.dylib" ! -name "*.deps.json" ! -name "*.runtimeconfig.json" \) | head -1 | sed 's|^\./||')

if [ -z "$ExecutableName" ]; then
    echo "Error: No executable found in the current directory"
    exit 1
fi

echo "Executing application: $ExecutableName"
./$ExecutableName

cd $PWDIR