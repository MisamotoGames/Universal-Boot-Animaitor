# Universal Boot Animation System (BAS)

Universal BAS is a Windows editor and x64 UEFI application for playing a short animation before Windows starts. The editor imports still-image sequences, animated GIFs, and common video formats; lets you preview, scrub, reorder, and remove frames; and exports a compact frame package. The UEFI player reads that package from FAT32, displays it through UEFI Graphics Output Protocol (GOP), then starts Windows Boot Manager.

This is an experimental community project. UEFI graphics, storage discovery, and frame timing differ by firmware. Stable 40 FPS is a target, not a guarantee. The included EFI application is unsigned; Secure Boot must be disabled or the EFI binary must be signed by a key trusted by the target firmware. Preboot audio is not supported.

## Repository contents

- `UniversalBAS.csproj`, `MainForm.cs`, `AnimationPackage.cs`, `MediaFoundationVideo.cs`, `Program.cs`: Windows editor source.
- `uefi-rust/`: x64 UEFI player source and lockfile.
- `BASX64.EFI`: built UEFI player used by the setup package.
- `Create-BAS-Volume.ps1`: optional helper that creates a FAT32 volume only in existing unallocated space on a non-system disk.
- `UniversalBAS.iss`: Inno Setup installation payload definition.
- `setup-bootstrapper/`: full-screen BAS setup UI, animated progress bar, and administrator manifest.
- `Build-Setup.ps1`: builds the Inno payload and wraps it in the full-screen setup application.
- `BOOT-SETUP.md`: firmware installation and boot-choice steps.
- `LICENSE`: MIT License.

## Build the Windows editor

Requirements:

- Windows 10 or 11, x64
- .NET 8 SDK (Windows Desktop workload / Windows Forms targeting pack)
- Internet access on the first build to restore NuGet packages

From PowerShell in this folder:

```powershell
dotnet restore .\UniversalBAS.csproj
dotnet publish .\UniversalBAS.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\publish
```

The self-contained editor does not require a separate .NET runtime on the target PC. Media Foundation is provided by Windows and is used for video import.

## Build the UEFI player

Requirements:

- Rust stable toolchain (`rustup` and `cargo`)
- Rust target `x86_64-unknown-uefi`
- Windows GNU/LLVM-compatible linker toolchain supported by the Rust target; the standard Rust MSVC toolchain is sufficient for this crate

Install the target once, then build:

```powershell
rustup target add x86_64-unknown-uefi
cd .\uefi-rust
cargo build --release --target x86_64-unknown-uefi
```

The EFI output is `uefi-rust\target\x86_64-unknown-uefi\release\universal-bas-uefi.efi`. Copy it to the repository root as `BASX64.EFI` before building the Windows installer. Dependencies are pinned by `Cargo.lock`.

## Build the full-screen installer

Requirements:

- Inno Setup 6.7 or later
- .NET Framework 4.8 C# compiler (the .NET Framework developer pack)
- A successful Windows editor publish
- `BASX64.EFI` at the repository root

First build the editor and UEFI player as described above. Then run:

```powershell
PowerShell -ExecutionPolicy Bypass -File .\Build-Setup.ps1
```

The result is `Universal-BAS-Setup-x64.exe`, a borderless full-screen setup with a black BAS splash and smoothly moving outlined bar. The logo and bar fade away for prompts and return during installation. The setup installs the editor under Program Files and adds Start menu and optional desktop shortcuts. It reuses a recognizable Windows EFI System Partition mounted as Z:, otherwise asks Windows to mount its active EFI System Partition temporarily at Z: or another free drive letter. It copies the player to `EFI\BAS\BASX64.EFI` and does not replace Intel Unlock Manager or Windows Boot Manager. Animation setup defaults to skipped; choose a detected FAT32 volume to install the sample or add an animation later from the editor. Firmware boot order is unchanged; add BAS as a separate firmware entry.

## Install and select BAS

Run the installer as Administrator. It installs files, then gives you the manual firmware step. On a Dell system, press F2 during startup, open Boot Configuration, choose Add Boot Option, name it `Universal BAS`, and browse the EFI System Partition to `\EFI\BAS\BASX64.EFI`. Use F12 to manually choose Universal BAS for a test. Firmware menus vary by vendor and model; see `BOOT-SETUP.md`.

The editor writes exported animation data to `BAS\animation.bas` on a FAT32 volume. Keep that volume connected when starting BAS. The player searches UEFI-visible FAT filesystems for this package. If the package is missing or invalid, the player skips animation and continues to Windows Boot Manager.

## Animation format and limits

The package starts with `BASANIM1`, a little-endian version, playback FPS, and frame count, followed by width, height, byte length, and JPEG data for each frame. The current player supports 1–600 frames, 1–60 FPS, up to 8192×8192 individual dimensions, and a 4.2-megapixel output limit per frame. For consistent timing, set FPS in the editor; GIF import does not preserve each GIF frame's individual delay.

## Safety and compatibility

- The setup writes a new file under `EFI\BAS` and leaves firmware boot order unchanged.
- Do not delete or rename other EFI files. Keep an existing unlock manager first in the firmware sequence.
- FAT32 is required because UEFI firmware commonly reads it directly. A FAT32 file cannot exceed 4 GiB.
- The optional volume helper only uses already-unallocated space on an eligible non-system GPT disk; it does not shrink, delete, or format existing partitions.
- Test the BAS choice manually before changing any default boot selection.

## License

The project source is licensed under the MIT License. Third-party dependencies keep their own licenses; see their package and crate metadata.
