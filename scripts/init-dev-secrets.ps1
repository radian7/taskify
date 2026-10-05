<#
.SYNOPSIS
    Creates the development secrets that the Taskify AppHost needs and stores them in .NET user secrets.

.DESCRIPTION
    The AppHost declares these Aspire secret parameters (src/Taskify.AppHost/AppHost.cs):
      postgres-password, projects-db-password, tasks-db-password, notifications-db-password,
      web-api-key, projects-api-key, tasks-api-key, notifications-api-key,
      dataprotection-cert, dataprotection-cert-password.
    Nothing secret is committed to the repository (constitution Principle I). Run this script once per
    machine; it keeps existing values unless -Force is given, because replacing the PostgreSQL password
    would lock you out of the existing data volume.

    The Data Protection certificate encrypts the key ring that protects the selected-user cookie
    (research R8). This script creates a self-signed development certificate; real deployments supply
    their own through the platform secret store.

.PARAMETER Force
    Replace values that already exist. Delete the "taskify-postgres-data" Docker volume first if you do.

.EXAMPLE
    ./scripts/init-dev-secrets.ps1
#>
[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\Taskify.AppHost'

function New-RandomHex([int]$Bytes = 32) {
    $buffer = New-Object byte[] $Bytes
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($buffer) } finally { $generator.Dispose() }
    return ([System.BitConverter]::ToString($buffer) -replace '-', '').ToLowerInvariant()
}

function New-DevelopmentCertificate([string]$Password) {
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    $request = New-Object System.Security.Cryptography.X509Certificates.CertificateRequest(
        'CN=taskify-dataprotection',
        $rsa,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $certificate = $request.CreateSelfSigned([System.DateTimeOffset]::UtcNow.AddMinutes(-5), [System.DateTimeOffset]::UtcNow.AddYears(2))
    try {
        $pfx = $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $Password)
        return [System.Convert]::ToBase64String($pfx)
    }
    finally {
        $certificate.Dispose()
        $rsa.Dispose()
    }
}

$existing = @{}
foreach ($line in (dotnet user-secrets list --project $project)) {
    if ($line -match '^(?<key>[^=]+?)\s*=\s*(?<value>.*)$') { $existing[$Matches['key']] = $Matches['value'] }
}

function Set-SecretIfMissing([string]$Name, [scriptblock]$Create) {
    $key = "Parameters:$Name"
    if ($existing.ContainsKey($key) -and -not $Force) {
        Write-Host "kept     $Name"
        return
    }
    dotnet user-secrets set $key (& $Create) --project $project | Out-Null
    Write-Host "created  $Name"
}

Set-SecretIfMissing 'postgres-password' { New-RandomHex 24 }
Set-SecretIfMissing 'projects-db-password' { New-RandomHex 24 }
Set-SecretIfMissing 'tasks-db-password' { New-RandomHex 24 }
Set-SecretIfMissing 'notifications-db-password' { New-RandomHex 24 }
Set-SecretIfMissing 'web-api-key' { New-RandomHex 32 }
Set-SecretIfMissing 'projects-api-key' { New-RandomHex 32 }
Set-SecretIfMissing 'tasks-api-key' { New-RandomHex 32 }
Set-SecretIfMissing 'notifications-api-key' { New-RandomHex 32 }

# The certificate and its password belong together, so they are created (or kept) as a pair.
if (($existing.ContainsKey('Parameters:dataprotection-cert') -and $existing.ContainsKey('Parameters:dataprotection-cert-password')) -and -not $Force) {
    Write-Host 'kept     dataprotection-cert (and password)'
}
else {
    $certificatePassword = New-RandomHex 24
    dotnet user-secrets set 'Parameters:dataprotection-cert-password' $certificatePassword --project $project | Out-Null
    dotnet user-secrets set 'Parameters:dataprotection-cert' (New-DevelopmentCertificate $certificatePassword) --project $project | Out-Null
    Write-Host 'created  dataprotection-cert (and password)'
}

Write-Host ''
Write-Host 'Development secrets are ready. Start Taskify with: dotnet run --project src/Taskify.AppHost'
