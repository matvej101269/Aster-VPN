# EXE installer

Build with `scripts/build-installer.ps1`. Requires the official portable Inno Setup 7.1.0 compiler in `vendor/inno-7.1.0`. A clean self-contained win-x64 publish is created in a new staging directory. The installer includes .NET/WPF and does not need a separate runtime download on the target PC.

The compiler receives an explicit generated list of audited payload files. Only generated application/runtime files, specifically named VPN cores, and license notices enter the payload. No files are read from the user's AsterVPN AppData directory, `.private`, tests, generated VPN profiles, or subscription storage. `dist/installer/payload-manifest.json` lists all packaged files and SHA-256 hashes for audit; the installer is the separate `.exe` in that directory.

Setup installs under Program Files, creates Start menu and optional desktop shortcuts, and registers the standard Windows uninstaller. Installation requires administrator privileges, as does the TUN client. Existing Aster instances must be exited using the tray menu before installation or upgrades. No VPN is automatically launched by setup.

Saved settings in the current user's AppData directory are neither included in the installer nor deleted by uninstall. Thus an existing user retains their settings; a new user receives an empty default configuration. No code-signing certificate is configured.
