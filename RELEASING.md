# Releasing

Official binaries are built by GitHub Actions. Do not upload a workstation-built installer as an official release.

1. Create a branch on any development computer, commit the change, and open a pull request.
2. Confirm the `CI / build-test` check passes and merge to `main`.
3. Update `CHANGELOG.md` and the project `Version`, `AssemblyVersion`, and `FileVersion` properties.
4. From an up-to-date `main`, create and push a matching stable SemVer tag:

   ```powershell
   git tag -a v0.3.0 -m "Release 0.3.0"
   git push origin v0.3.0
   ```

5. The `Release` workflow validates, publishes a self-contained `win-x64` build, packages it with the pinned Velopack CLI, and creates the GitHub Release.
6. Verify the GitHub Release contains `Setup.exe`, full package, portable ZIP, and `releases.win.json` assets before installing it anywhere.

The application uses the public GitHub Releases API without an embedded token. Stable releases only are considered.

## Signing

The initial pipeline is intentionally unsigned because no signing identity was authorized. Configure a protected signing identity before representing builds as signed. Never commit a certificate private key or signing password. Velopack must perform signing during packaging so the installer, updater, and application payload receive consistent signatures.
