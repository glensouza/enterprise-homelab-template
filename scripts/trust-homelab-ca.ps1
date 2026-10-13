<#
.SYNOPSIS
  Fetch the homelab's step-ca root certificate, verify it against the CA host, and trust it for your Windows account.

.DESCRIPTION
  Every internal HTTPS name (authentik/critterwatch/grafana/... .brewhouse.internal) is signed by the step-ca root.
  A browser shows ERR_CERT_AUTHORITY_INVALID until that root is trusted. A `terraform destroy` mints a new root
  unless the CA was restored from the controller (docs/01 ADR 126), so run this after a rebuild if the browser complains.
  Defaults match this repo's addressing (devops 10.10.10.111, step-ca 10.10.130.121); override -Devops / -StepCa for your own lab.

  Steps: scp the public root from devops, compare its SHA-256 fingerprint with the one step-ca itself reports,
  and only if they match run `certutil -user -addstore Root` (Windows shows a confirmation prompt; click Yes).
  Restart the browser afterwards. The old root, if any, can stay; remove it in certmgr.msc if you want a clean store.

.EXAMPLE
  .\scripts\trust-homelab-ca.ps1

.EXAMPLE
  .\scripts\trust-homelab-ca.ps1 -Devops 192.168.1.10 -StepCa 192.168.1.20
#>
[CmdletBinding()]
param(
    [string]$Devops = '10.10.10.111',
    [string]$StepCa = '10.10.130.121',
    [string]$RemoteRoot = '/opt/ansible-fetched/step-ca/root_ca.crt',
    [string]$OutFile = (Join-Path $env:USERPROFILE 'Downloads\brewhouse-root_ca.crt')
)

$ErrorActionPreference = 'Stop'

Write-Host "Fetching the root certificate from root@$Devops ..."
scp "root@${Devops}:$RemoteRoot" $OutFile
if ($LASTEXITCODE -ne 0) { throw "scp failed (exit $LASTEXITCODE)" }

$cert = New-Object Security.Cryptography.X509Certificates.X509Certificate2 $OutFile
$sha = [Security.Cryptography.SHA256]::Create()
$local = [BitConverter]::ToString($sha.ComputeHash($cert.RawData)) -replace '-'

Write-Host "Asking the CA host ($StepCa) for its own fingerprint ..."
$line = ssh "root@$Devops" "ssh root@$StepCa 'openssl x509 -in /root/.step/certs/root_ca.crt -noout -fingerprint -sha256'"
if ($LASTEXITCODE -ne 0 -or -not $line) { throw 'Could not read the fingerprint from the step-ca host.' }
$remote = (($line -split '=')[1] -replace ':', '').Trim()

Write-Host "Downloaded : $local"
Write-Host "CA reports : $remote"

if ($local -ne $remote) {
    throw 'Fingerprints do NOT match - not installing. Check that the CA host and devops are the ones you expect.'
}

Write-Host 'Fingerprints match. Installing into your trusted root store (confirm the Windows prompt) ...'
certutil -user -addstore Root $OutFile
if ($LASTEXITCODE -ne 0) { throw "certutil failed (exit $LASTEXITCODE)" }

Write-Host 'Done. Fully restart your browser so it picks up the new root.'
