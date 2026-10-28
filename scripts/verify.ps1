$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    New-Item -ItemType Directory -Force -Path artifacts/verification | Out-Null
    function Invoke-Checked([string]$name, [string]$program, [string[]]$commandArgs) {
        $previousPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & $program @commandArgs 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath "artifacts/verification/$name.log"
            $exitCode = $LASTEXITCODE
        }
        finally { $ErrorActionPreference = $previousPreference }
        if ($exitCode -ne 0) { throw "$name failed with exit code $exitCode" }
    }
    $npmCommand = (Get-Command npm -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    Invoke-Checked 'npm-ci' $npmCommand @('ci', '--prefix', 'src/PicLoom.Web')
    Invoke-Checked 'web-build' $npmCommand @('run', 'build', '--prefix', 'src/PicLoom.Web')
    Invoke-Checked 'dotnet-build' 'dotnet' @('build', 'PicLoom.sln')
    Invoke-Checked 'dotnet-test' 'dotnet' @('test', 'PicLoom.sln', '--logger', 'trx', '--results-directory', 'artifacts/verification/test-results')
    Invoke-Checked 'web-test' $npmCommand @('test', '--prefix', 'src/PicLoom.Web', '--', '--run')
}
finally { Pop-Location }
