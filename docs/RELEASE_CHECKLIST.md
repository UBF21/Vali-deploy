# Checklist de release — Vali-Deploy

Al publicar una versión nueva en GitHub Releases (`https://github.com/UBF21/Vali-deploy/releases`):

1. Generar los binarios self-contained para cada RID:
   ```bash
   dotnet publish vali-deploy/vali-deploy.csproj -r win-x64 -c Release --self-contained true
   dotnet publish vali-deploy/vali-deploy.csproj -r osx-x64 -c Release --self-contained true
   dotnet publish vali-deploy/vali-deploy.csproj -r osx-arm64 -c Release --self-contained true
   dotnet publish vali-deploy/vali-deploy.csproj -r linux-x64 -c Release --self-contained true
   ```

2. Comprimir cada carpeta de publish a `.zip`, nombrados `Vali-Deploy_<version>-<rid>.zip` (ej. `Vali-Deploy_1.2.0-win-x64.zip`) — el nombre debe **contener el RID** (`win-x64`, `osx-x64`, `osx-arm64`, `linux-x64`) para que `UpdaterManager.MapToUpdateInfo` lo pueda mapear.

3. Generar `SHA256SUMS.txt` en la misma carpeta que los `.zip`:

   PowerShell (Windows):
   ```powershell
   Get-ChildItem *.zip | ForEach-Object { "$((Get-FileHash $_.Name -Algorithm SHA256).Hash.ToLower())  $($_.Name)" } | Out-File -Encoding utf8 SHA256SUMS.txt
   ```

   Bash (Linux/macOS):
   ```bash
   sha256sum *.zip > SHA256SUMS.txt
   ```

4. Crear el release en GitHub, subiendo TODOS los `.zip` más `SHA256SUMS.txt` como assets:
   ```bash
   gh release create v<version> *.zip SHA256SUMS.txt --title "v<version>" --notes "<release notes>"
   ```

5. Verificar: correr una versión vieja del CLI y confirmar que detecta la actualización nueva, descarga, valida el checksum sin error ("Checksum verified.") e instala correctamente.

Si se olvida subir `SHA256SUMS.txt`, el updater sigue funcionando pero muestra "No checksum available for this release — skipping integrity verification." y no bloquea el update.
