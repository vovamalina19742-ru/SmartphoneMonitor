# Start monitoring stack (Prometheus + Grafana)
# Run this from PowerShell as Administrator if needed.

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $scriptDir

# Prefer Docker Compose v2 (bundled with Docker Desktop): use 'docker compose'
if (Get-Command 'docker' -ErrorAction SilentlyContinue) {
    try {
        docker compose up -d
    } catch {
        try {
            docker-compose up -d
        } catch {
            Write-Error "Failed to start compose using 'docker compose' and 'docker-compose'. Ensure Docker Desktop is installed."
            exit 1
        }
    }
} else {
    Write-Error "Docker CLI not found. Install Docker Desktop: https://www.docker.com/get-started"
    exit 1
}

Start-Sleep -Seconds 8

docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"

Write-Host '--- Grafana health ---'
try { Invoke-RestMethod -Uri 'http://localhost:3000/api/health' -UseBasicParsing | ConvertTo-Json -Depth 5 } catch { Write-Host 'Grafana health check failed:' $_.Exception.Message }

Write-Host '--- Prometheus ready ---'
try { Invoke-RestMethod -Uri 'http://localhost:9090/-/ready' -UseBasicParsing | ConvertTo-Json -Depth 5 } catch { Write-Host 'Prometheus check failed:' $_.Exception.Message }

Write-Host '--- Prometheus targets ---'
try { Invoke-RestMethod -Uri 'http://localhost:9090/api/v1/targets' -UseBasicParsing | ConvertTo-Json -Depth 5 } catch { Write-Host 'Prometheus targets check failed:' $_.Exception.Message }

Write-Host 'Done.'
