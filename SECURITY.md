# Security

Breathing Room keeps your data on your PC:

- It has no accounts and no telemetry.
- It keeps everything in one JSON file in `%APPDATA%\BreathingRoom`.
- Its only network request is the update check (see below), which you can switch off in Settings.

The only system-level things it does are these:

- **The log hotkey:** it registers a global hotkey (`RegisterHotKey`).
- **Context, if switched on:** it reads the name of the app in front when you log a cigarette. You can turn this off in Settings.
- **Startup, if you ask:** it adds itself to your Windows startup list (`HKCU\...\Run`).
- **Updates:** about once a day it asks the GitHub API for this repository's latest release (`/repos/<owner>/<repo>/releases/latest`). Nothing about you is sent. When you click **Update now**, it:
  1. downloads the release's `BreathingRoom-<version>-windows.zip`, only from this repository's `github.com/.../releases/download/` address;
  2. checks the file against the `.sha256` file published with the release, and stops if they differ;
  3. checks that the exe inside carries the expected version;
  4. renames the running exe to `BreathingRoom.exe.old`, puts the new one in its place, and restarts. If anything fails, the old exe is put back.

The release zips are built from this repository by the [release workflow](.github/workflows/release.yml), which also writes the checksum files.

What the checksum does and doesn't do: it proves the download arrived whole and is the file published with that release. It doesn't prove who published it, because the checksum comes from the same release. So the updater trusts whoever can publish releases on this repository, the same people who can change its code. The app isn't code-signed, and an updated exe doesn't pass through SmartScreen again.

## Reporting a vulnerability

If you find a security problem, please **don't open a public issue**. Use GitHub's private vulnerability reporting instead: open the **Security** tab of the repository and choose **Report a vulnerability**. You'll get a reply as soon as possible, and credit if you'd like it.

## Supported versions

Only the latest release gets fixes.
