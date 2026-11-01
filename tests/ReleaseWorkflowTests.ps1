param([string]$WorkflowPath = (Join-Path $PSScriptRoot '..\.github\workflows\release.yml'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Test the actual YAML function, not a duplicate implementation. No network calls or files are written.
$yaml = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $WorkflowPath).Path, [Text.Encoding]::UTF8)
$blocks = [regex]::Matches($yaml, '(?m)^        run: \|\r?\n((?:(?:          .*|)\r?\n?)*)')
$definitions = @()
foreach ($block in $blocks) {
    $code = [regex]::Replace($block.Groups[1].Value, '(?m)^          ', '')
    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseInput($code, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw ('Workflow PowerShell parse error: ' + $errors[0].Message) }
    $definitions += @($ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-GitHubJson'
    }, $true))
}
if ($definitions.Count -ne 1) { throw 'Expected exactly one Invoke-GitHubJson definition in the workflow.' }
. ([scriptblock]::Create($definitions[0].Extent.Text))

$headers = @{}
$script:MockResponse = $null
$script:CapturedBody = $null
function Invoke-RestMethod {
    [CmdletBinding()]
    param([string]$Method, [string]$Uri, [hashtable]$Headers, [object]$Body, [string]$ContentType)
    $script:CapturedBody = $Body
    # Match the real cmdlet's non-enumerating JSON-array output, including an empty array.
    $PSCmdlet.WriteObject($script:MockResponse, $false)
}
function Assert-ReleaseCheck {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$script:MockResponse = [object[]]@()
$actual = @(Invoke-GitHubJson -Method Get -Uri 'https://example.invalid/releases')
Assert-ReleaseCheck ($actual.Count -eq 0) 'An empty release list must produce no pipeline objects.'
Write-Output 'PASS empty JSON array'

$script:MockResponse = [object[]]@([pscustomobject]@{ tag_name='v1.0.0'; draft=$false })
$actual = @(Invoke-GitHubJson -Method Get -Uri 'https://example.invalid/releases')
Assert-ReleaseCheck ($actual.Count -eq 1 -and $actual[0].tag_name -eq 'v1.0.0') 'A singleton list must expose the release object.'
Write-Output 'PASS singleton JSON array'

$script:MockResponse = [object[]]@([pscustomobject]@{ id=1; name='one' }, [pscustomobject]@{ id=2; name='two' })
$actual = @(Invoke-GitHubJson -Method Get -Uri 'https://example.invalid/assets')
Assert-ReleaseCheck ($actual.Count -eq 2 -and $actual[0].id -eq 1 -and $actual[1].id -eq 2) 'Multiple assets must remain separate objects.'
Write-Output 'PASS multiple JSON array entries'

$script:MockResponse = [pscustomobject]@{ id=123; draft=$true; upload_url='https://example.invalid/assets' }
$unicodeBody = [string][char]0x4E2D + [char]0x6587
$actual = Invoke-GitHubJson -Method Post -Uri 'https://example.invalid/releases' -Body @{ body=$unicodeBody; draft=$true }
Assert-ReleaseCheck ($actual.id -eq 123 -and $actual.draft) 'A release response object must be preserved.'
Assert-ReleaseCheck ($script:CapturedBody -is [byte[]]) 'JSON requests must use explicit UTF-8 bytes.'
$decoded = [Text.Encoding]::UTF8.GetString($script:CapturedBody) | ConvertFrom-Json
Assert-ReleaseCheck ($decoded.body -ceq $unicodeBody) 'Unicode release notes did not survive UTF-8 JSON encoding.'
Write-Output 'PASS JSON object and UTF-8 request body'

Write-Output 'Release workflow regression checks: 4 passed; no network used.'
