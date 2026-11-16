# Boot setup guide

## What the installer does

The full-screen installer puts the Windows editor in Program Files, adds shortcuts, copies `BASX64.EFI` to `EFI\BAS\BASX64.EFI` on the active EFI System Partition, and can place a sample animation package on a selected FAT32 volume. It reuses a recognizable EFI partition already mounted as Z:, or asks Windows to mount the active EFI partition temporarily at Z: or another free drive letter. Animation setup defaults to skipped, so you can finish without selecting a volume. You can export/copy an animation later. It does not reorder firmware entries or alter Intel Unlock Manager / Windows Boot Manager.

## Dell Precision 5520: add BAS as a separate boot option

1. Finish setup in Windows and leave Secure Boot disabled unless BAS has been signed with a key trusted by the firmware.
2. Restart and press **F2** at the Dell logo to open System Setup.
3. Open **Boot Configuration** and select **Add Boot Option**.
4. Set the name to **Universal BAS**.
5. Browse the EFI System Partition and select `\EFI\BAS\BASX64.EFI`.
6. Save the option. Do not move Intel Unlock Manager or Windows Boot Manager in the existing order.
7. Restart and press **F12** to open the one-time boot menu. Choose **Universal BAS** for a manual test. The animation should play, then hand off to Windows Boot Manager.

Dell firmware versions can label screens differently. Other vendors have their own firmware setup steps. If the firmware does not offer a way to add a file-based UEFI option, follow the vendor's manual boot-option process; do not replace the existing unlock manager to work around it.

## Animation storage

The editor exports `BAS\animation.bas` to a FAT32 volume. Keep the chosen volume connected during boot. The UEFI player scans visible FAT filesystems for this path. If the storage volume is absent, it should continue to Windows without animation.

## Recovery

If the BAS option fails, use F12 and select the existing Intel Unlock Manager or Windows Boot Manager. The installer keeps these existing EFI files untouched. To remove BAS, delete only the separate `Universal BAS` firmware entry using the firmware setup UI, then remove `EFI\BAS` and the `BAS` animation folder if desired. Do not delete any other EFI directory.
