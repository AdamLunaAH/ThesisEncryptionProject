# curl-flow1.ps1
# PowerShell script to perform the first step of the curl flow   (translation of az-prep-publish.sh)

# Usage: curl-flow1.ps1
# 

$response = curl.exe -s -X 'POST' `
  'https://lia2026-webapi-public-57875114.azurewebsites.net/api/Users/login' `
  -H 'accept: */*' `
  -H 'Content-Type: application/json' `
  -d '{\"email\": \"martin@lenart.se\", \"password\": \"martin\"}'

$token = ($response | ConvertFrom-Json).token
Write-Host "Token: $token"


curl.exe -s -X 'GET' `
  'https://lia2026-webapi-public-57875114.azurewebsites.net/api/Admin/users' `
  -H 'accept: */*' `
  -H "Authorization: Bearer $token"