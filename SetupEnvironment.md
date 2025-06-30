# Local Environment Setup – macOS host

PeppolSG.API targets ASP.NET 4.8 (System.Web) which **only runs on Windows**.  
These instructions describe how to prepare a macOS machine (Apple Silicon or Intel) to build, run and debug the application.

---
## 1. Prerequisites on macOS
1. **Virtualisation software** (choose one)
   - Parallels Desktop 18+ (recommended for Apple Silicon)  
   - VMware Fusion 13+  
   - VirtualBox 7 (Intel-only; slower)
2. **Windows 11 ISO**  
   - Download official ISO (or VHDX for Parallels) from Microsoft.
3. **~60 GB free disk space** (40 GB Win VM + 20 GB build artefacts).
4. **Git** (Homebrew: `brew install git`).
5. **PowerShell 7** (for optional scripts: `brew install --cask powershell`).

---
## 2. Create a Windows 11 VM
1. Install your chosen virtualisation app.
2. Create a new VM from the Windows ISO / Parallels VHDX.
3. Allocate resources:  
   - 4 CPU cores  
   - 8 GB RAM (16 GB preferred for test runs)  
   - 60 GB disk (dynamic).
4. Enable **nested virtualization** (VMware) if you plan to use Docker Desktop inside the VM.

---
## 3. Install Development Tools inside Windows
1. **Visual Studio 2022 Community**  
   Workloads:  
   - "ASP.NET and web development"  
   - ".NET Desktop Development" (for MSTest SDK)
2. **Git for Windows** (if not included in VS).
3. **NuGet CLI** (VS Installer adds it; verify `nuget.exe` in `%ProgramFiles%\NuGet`).
4. **7-Zip** (for inspecting packages) – optional.

> After installation reboot the VM once to register IIS Express & SSL certs.

---
## 4. Clone and Restore the Project
```powershell
cd %USERPROFILE%\source\repos
git clone https://github.com/<your-fork>/PeppolSG.API.git
cd PeppolSG.API
nuget restore PeppolSG.API.sln
```

---
## 5. Configure Certificates & Secrets
1. Create `C:\certs` and place your **Peppol PKI** `.p12` file (e.g., `ap_accesspoint.p12`).
2. Generate DPAPI-encrypted password:
   ```powershell
   $plain = Read-Host "Certificate Password" -AsSecureString
   $bytes = [System.Text.Encoding]::UTF8.GetBytes( (
       [Runtime.InteropServices.Marshal]::PtrToStringAuto(
           [Runtime.InteropServices.Marshal]::SecureStringToBSTR($plain))) )
   $enc = [Convert]::ToBase64String(
           [System.Security.Cryptography.ProtectedData]::Protect($bytes,$null,'LocalMachine'))
   $enc | clip  # copies to clipboard
   ```
3. Edit `PeppolSG.API/Web.config` `<appSettings>`:
   ```xml
   <add key="PeppolP12FilePath" value="C:\certs\ap_accesspoint.p12"/>
   <add key="PeppolP12PasswordEncrypted" value="<paste-value>"/>
   <add key="IsTestEnvironment" value="true"/>
   ```

> DPAPI encryption is **machine-specific** – regenerate if you move the VM.

---
## 6. Build & Run
### Visual Studio (interactive)
1. Open `PeppolSG.API.sln`.
2. Set build configuration `Debug` | `Any CPU`.
3. Press **F5**.  
   VS compiles, launches IIS Express on `https://localhost:44322/` and opens the Help page.

### Command-line (CI parity)
```powershell
nuget restore PeppolSG.API.sln
msbuild PeppolSG.API.sln /p:Configuration=Release
vstest.console.exe PeppolSG.API.Tests\bin\Release\PeppolSG.API.Tests.dll
```

---
## 7. Optional: Docker for Windows
ASP.NET System.Web requires **Windows containers**.
1. Install **Docker Desktop for Windows** inside the VM.
2. Enable **Windows containers** mode. *(Not available on ARM VMs)*
3. Author a `Dockerfile` based on `mcr.microsoft.com/dotnet/framework/aspnet:4.8-windowsservercore-ltsc2022`.

---
## 8. Helpful VS Extensions
- *Web Essentials* – XML/JSON helpers.  
- *SlowCheetah* – config transforms preview.

---
## 9. Running End-to-End Tests
The MSTest project compiles into `PeppolSG.API.Tests.dll`.  Execute via Test Explorer or:
```powershell
vstest.console.exe .\PeppolSG.API.Tests\bin\Debug\PeppolSG.API.Tests.dll
```

---
## 10. Maintenance
- Use `BuildRelease.ps1` to create zip packages (`artifacts/`) for deployment.  
- `ConfigurationService` hot-reloads `Web.config`; update settings without restarting IIS Express.  
- Log files are under `logs/` (rolling JSON per day).

---
## Summary Checklist
☑ Virtualisation software installed  
☑ Windows 11 VM with VS 2022 + .NET 4.8  
☑ Project cloned & NuGet restore  
☑ Peppol cert & encrypted password configured  
☑ Build succeeds & `/api/health/liveness` returns **Alive** 