param(
    [string]$Script = (Join-Path $PSScriptRoot 'DuckNote.ps1'),
    [string]$Icon   = (Join-Path $PSScriptRoot 'assets\duck.ico'),
    [string]$OutDir = (Join-Path $PSScriptRoot 'dist'),
    [string]$CertThumbprint = $env:DUCKNOTE_CERT_THUMBPRINT,
    [string]$TimestampUrl   = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "compilatore C# non trovato: $csc" }
foreach ($f in @($Script, $Icon, (Join-Path $PSScriptRoot 'src\Launcher.cs'))) {
    if (-not (Test-Path $f)) { throw "file mancante: $f" }
}
if (-not (Test-Path $OutDir)) { [void](New-Item -ItemType Directory -Path $OutDir -Force) }

$tmp = Join-Path $env:TEMP ('ducknote-build-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
[void](New-Item -ItemType Directory -Path $tmp -Force)

$risorsa = Join-Path $tmp 'DuckNote.ps1'
$bom   = [byte[]](0xEF, 0xBB, 0xBF)
$corpo = [IO.File]::ReadAllBytes($Script)
if ($corpo.Length -ge 3 -and $corpo[0] -eq 0xEF -and $corpo[1] -eq 0xBB -and $corpo[2] -eq 0xBF) {
    [IO.File]::WriteAllBytes($risorsa, $corpo)
} else {
    $con = [byte[]]::new($bom.Length + $corpo.Length)
    [Array]::Copy($bom, 0, $con, 0, $bom.Length)
    [Array]::Copy($corpo, 0, $con, $bom.Length, $corpo.Length)
    [IO.File]::WriteAllBytes($risorsa, $con)
}

$hashSorgente = (Get-FileHash -LiteralPath $Script   -Algorithm SHA256).Hash.ToLowerInvariant()
$hashEstratto = (Get-FileHash -LiteralPath $risorsa  -Algorithm SHA256).Hash.ToLowerInvariant()

$sorgenteCs = Join-Path $tmp 'Launcher.cs'
(Get-Content (Join-Path $PSScriptRoot 'src\Launcher.cs') -Raw).Replace('@@SCRIPT_SHA256@@', $hashEstratto) |
    Set-Content $sorgenteCs -Encoding UTF8

$exe = Join-Path $OutDir 'DuckNote.exe'
$argomenti = @(
    '/nologo'
    '/target:winexe'
    '/optimize+'
    "/out:$exe"
    "/win32icon:$Icon"
    "/resource:$risorsa,DuckNote.ps1"
    '/reference:System.dll'
    '/reference:System.Windows.Forms.dll'
    $sorgenteCs
)
& $csc @argomenti
$code = $LASTEXITCODE
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
if ($code -ne 0) { throw "compilazione fallita (codice $code)" }

$statoFirma = 'non firmato (nessun certificato indicato)'
if ($CertThumbprint) {
    $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $CertThumbprint.Replace(' ', '') } | Select-Object -First 1
    if (-not $cert) { throw "certificato $CertThumbprint non trovato tra quelli per la firma del codice" }
    $firma = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert `
                 -HashAlgorithm SHA256 -TimestampServer $TimestampUrl
    if ($firma.Status -ne 'Valid') { throw "firma non valida: $($firma.StatusMessage)" }
    $statoFirma = "firmato con $($cert.Subject) ($($cert.Thumbprint))"
}

$hashExe = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$somme = Join-Path $OutDir 'SHA256SUMS.txt'
@(
    "# DuckNote, somme SHA-256 - $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
    "# Confronta la riga che corrisponde a quel che stai eseguendo."
    "$hashSorgente  DuckNote.ps1 (sorgente, senza BOM)"
    "$hashEstratto  DuckNote.ps1 (come estratto dall'eseguibile, con BOM)"
    "$hashExe  DuckNote.exe"
) | Set-Content $somme -Encoding UTF8

$info = Get-Item $exe
'{0}  ({1:N0} byte)' -f $info.FullName, $info.Length
"  script  $hashSorgente"
"  estratto $hashEstratto"
"  exe      $hashExe"
"  firma    $statoFirma"
"  somme    $somme"
