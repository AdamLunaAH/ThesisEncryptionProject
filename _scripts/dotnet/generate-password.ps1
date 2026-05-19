# generate-password.ps1
# Generates a strong random string passwords in the format: Rx0aKu-OkDdDf-qXDvSL
# using openssl for cryptographically strong random number generation

# Run this script with: .\generate-password.ps1

# Generate password using openssl (equivalent to bash version)
# Use OpenSSL from Git installation if available, otherwise assume it's in PATH
$opensslPath = "C:\Program Files\Git\usr\bin\openssl.exe"
if (-not (Test-Path $opensslPath)) {
    $opensslPath = "openssl"
}

# Generate base64 output and clean it up
$base64Output = (& $opensslPath rand -base64 64) -join ""
$cleanedOutput = $base64Output -replace '[/+=]', ''

# Ensure we have at least 18 characters
if ($cleanedOutput.Length -lt 18) {
    # Generate more if needed
    $additionalOutput = (& $opensslPath rand -base64 64) -join ""
    $cleanedOutput += ($additionalOutput -replace '[/+=]', '')
}

$password = $cleanedOutput.Substring(0, 18)

# Break password into three groups of 6 characters with hyphens
$part1 = $password.Substring(0, 6)
$part2 = $password.Substring(6, 6) 
$part3 = $password.Substring(12, 6)

$formattedPassword = "$part1-$part2-$part3"
Write-Output $formattedPassword