Set-StrictMode -Version Latest

function Get-NovaGameProcessEnvironmentSnapshot {
    param([Parameter(Mandatory)][string[]]$Names)

    $currentEnvironment = [Environment]::GetEnvironmentVariables("Process")
    $snapshot = [ordered]@{}
    foreach ($name in $Names) {
        if ($currentEnvironment.Contains($name)) {
            $snapshot.Add($name, [string]$currentEnvironment[$name])
        }
        else {
            $snapshot.Add($name, $null)
        }
    }

    return ,$snapshot
}

function Restore-NovaGameProcessEnvironmentSnapshot {
    param([Parameter(Mandatory)][System.Collections.IDictionary]$Snapshot)

    foreach ($entry in $Snapshot.GetEnumerator()) {
        $name = [string]$entry.Key
        if ($null -eq $entry.Value) {
            [Environment]::SetEnvironmentVariable(
                $name,
                [System.Management.Automation.Language.NullString]::Value,
                "Process")
        }
        else {
            [Environment]::SetEnvironmentVariable(
                $name,
                [string]$entry.Value,
                "Process")
        }
    }
}
