# PowerShell Script to Generate a Development Certificate with IP Subject Alternative Name (SAN)
# This allows HTTPS to work without browser "Not Secure" warnings when accessing the API
# via a local IP address instead of localhost.
# The local IP is auto-detected; pass -IpAddress to override.
#
# REQUIREMENTS: Must be run as Administrator (needed to install cert into Trusted Root store)
#
# EXECUTION POLICY - Run this first if you get execution policy errors:
#   Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
#
# USAGE (auto-detects local IP):
#   .\generate-ip-dev-certificate.ps1
#
# USAGE with a specific IP (skips auto-detect):
#   .\generate-ip-dev-certificate.ps1 -IpAddress "10.0.0.5"

param(
    [string]$IpAddress    = "",
    [string]$CertPassword = "DevCertPassword123",
    [int]   $ValidityYears = 2
)

# ---- Auto-detect local IP if not supplied ----
if ([string]::IsNullOrWhiteSpace($IpAddress)) {
    # Pick a real LAN/WiFi DHCP address - excludes WSL/Hyper-V virtual adapters
    $detected = Get-NetIPAddress -AddressFamily IPv4 |
        Where-Object {
            $_.InterfaceAlias -in @("Ethernet", "Wi-Fi") -and
            $_.PrefixOrigin    -eq "Dhcp"                -and
            $_.SuffixOrigin    -eq "Dhcp"                -and
            $_.ValidLifetime   -ne [TimeSpan]::MaxValue  -and   # not Infinite
            $_.PreferredLifetime -ne [TimeSpan]::MaxValue       # not Infinite
        } |
        Sort-Object -Property InterfaceMetric |
        Select-Object -First 1 -ExpandProperty IPAddress

    if (-not $detected) {
        Write-Host "ERROR: Could not auto-detect a local IP address." -ForegroundColor Red
        Write-Host "Pass it manually:  .\generate-ip-dev-certificate.ps1 -IpAddress '192.168.0.10'" -ForegroundColor Yellow
        exit 1
    }
    $IpAddress = $detected
    Write-Host "Auto-detected IP : $IpAddress" -ForegroundColor Cyan
}

# ---- Guard: must be Administrator ----
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "ERROR: This script must be run as Administrator." -ForegroundColor Red
    Write-Host "Right-click PowerShell and choose 'Run as Administrator', then re-run." -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "=== SeidoSwStack IP Dev Certificate Generator ===" -ForegroundColor Cyan
Write-Host "IP address : $IpAddress" -ForegroundColor White
Write-Host "Valid for  : $ValidityYears year(s)" -ForegroundColor White
Write-Host ""

# ---- Remove old SeidoSwStack dev certs from both stores ----
Write-Host "Removing old SeidoSwStack dev certificates..." -ForegroundColor Yellow
@("Cert:\LocalMachine\My", "Cert:\LocalMachine\Root") | ForEach-Object {
    Get-ChildItem $_ -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -like "*SeidoSwStack*" } |
        Remove-Item -ErrorAction SilentlyContinue
}

# ---- Create self-signed certificate with localhost + IP as SANs ----
Write-Host "Creating certificate (DNS: localhost, IP: $IpAddress)..." -ForegroundColor Yellow

$cert = New-SelfSignedCertificate `
    -Subject           "CN=SeidoSwStack Dev" `
    -CertStoreLocation "Cert:\LocalMachine\My" `
    -NotAfter          (Get-Date).AddYears($ValidityYears) `
    -KeyAlgorithm      RSA `
    -KeyLength         2048 `
    -KeyExportPolicy   Exportable `
    -FriendlyName      "SeidoSwStack Dev Certificate" `
    -TextExtension     @(
        "2.5.29.17={text}IPAddress=$IpAddress&DNS=localhost",  # SAN: IP + DNS
        "2.5.29.37={text}1.3.6.1.5.5.7.3.1"                   # EKU: Server Authentication
    )

Write-Host "  Thumbprint : $($cert.Thumbprint)" -ForegroundColor Cyan

# ---- Export to PFX at solution root ----
#   Script lives at  <solution>/_scripts/dotnet/
#   Two levels up    -> solution root
$solutionRoot  = Resolve-Path (Join-Path $PSScriptRoot "..\..") | Select-Object -ExpandProperty Path
$pfxPath       = Join-Path $solutionRoot "dev-cert.pfx"
$securePassword = ConvertTo-SecureString $CertPassword -AsPlainText -Force

Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
Write-Host "  Exported to: $pfxPath" -ForegroundColor Cyan

# ---- Install into Trusted Root so browsers accept it ----
Write-Host "Installing into Trusted Root Certification Authorities..." -ForegroundColor Yellow
$store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
    [System.Security.Cryptography.X509Certificates.StoreName]::Root,
    [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
$store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
$store.Add($cert)
$store.Close()
Write-Host "  Certificate trusted." -ForegroundColor Green

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
Write-Host "  Certificate covers: localhost, $IpAddress" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Close ALL browser windows and reopen (browsers cache TLS state)" -ForegroundColor White
Write-Host "  2. Run the WebApiSimple project - Kestrel will automatically load dev-cert.pfx" -ForegroundColor White
Write-Host "  3. Re-run this script whenever the certificate expires or the IP changes" -ForegroundColor White
Write-Host ""
