# Create a FAT32 animation partition only inside already-unallocated space.
# This helper never shrinks, deletes, formats, or repartitions an existing volume.
param([ValidateRange(1,29)][int]$RequiredGiB = 1)
$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script as Administrator.'
}

$disks = @(Get-Disk | Where-Object { $_.OperationalStatus -contains 'Online' -and -not $_.IsBoot -and -not $_.IsSystem })
if (-not $disks) { throw 'No online non-system disk is available. Connect the intended animation drive and retry.' }
$rows = foreach ($disk in $disks) {
    $parts = @(Get-Partition -DiskNumber $disk.Number -ErrorAction SilentlyContinue)
    $used = ($parts | Measure-Object -Property Size -Sum).Sum
    $free = [math]::Max(0, [int64]$disk.Size - [int64]$used)
    [pscustomobject]@{ Number = $disk.Number; Model = $disk.FriendlyName; Style = $disk.PartitionStyle; SizeGB = [math]::Round($disk.Size / 1GB, 1); ApproxUnallocatedGB = [math]::Round($free / 1GB, 2) }
}
$rows | Format-Table -AutoSize
$diskNumber = [int](Read-Host 'Enter the disk number for the dedicated BAS animation partition')
$selected = $rows | Where-Object Number -eq $diskNumber | Select-Object -First 1
if (-not $selected) { throw 'That disk is not in the eligible list.' }
$disk = Get-Disk -Number $diskNumber
if ($disk.IsBoot -or $disk.IsSystem -or $disk.IsReadOnly -or $disk.IsOffline) { throw 'Safety check failed: selected disk is boot/system, read-only, or offline.' }
if ($disk.PartitionStyle -ne 'GPT') { throw 'Only GPT disks are supported by this helper.' }
$sizeInput = Read-Host "Partition size in GiB (1 through 29; at least $RequiredGiB GiB required; Enter for $([math]::Min(29, $RequiredGiB)) GiB)"
$sizeGiB = if ([string]::IsNullOrWhiteSpace($sizeInput)) { [math]::Min(29, $RequiredGiB) } else { [int]$sizeInput }
if ($sizeGiB -lt 1 -or $sizeGiB -gt 29 -or $sizeGiB -lt $RequiredGiB) { throw 'Choose a size from 1 to 29 GiB, at least the animation estimate.' }
$sizeBytes = [int64]$sizeGiB * 1GB
$parts = @(Get-Partition -DiskNumber $diskNumber -ErrorAction SilentlyContinue)
$approxFree = [int64]$disk.Size - [int64](($parts | Measure-Object -Property Size -Sum).Sum)
if ($approxFree -lt ($sizeBytes + 64MB)) { throw 'The disk does not have enough unallocated space for this partition.' }
Write-Host "`nThis creates a new $sizeGiB GiB FAT32 partition in unallocated space on disk $diskNumber ($($disk.FriendlyName)). Existing partitions and files are not selected for changes." -ForegroundColor Yellow
if ((Read-Host 'Type CREATE BAS VOLUME to continue') -cne 'CREATE BAS VOLUME') { Write-Host 'Cancelled. No disk changes were made.'; exit 0 }
$partition = New-Partition -DiskNumber $diskNumber -Size $sizeBytes -AssignDriveLetter
$volume = $partition | Format-Volume -FileSystem FAT32 -NewFileSystemLabel 'BASBOOT' -Confirm:$false -Force
Write-Host "Ready: $($volume.DriveLetter):\ is the BAS animation volume." -ForegroundColor Green
