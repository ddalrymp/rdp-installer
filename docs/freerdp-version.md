# FreeRDP Version Management

## Current Pinned Version

**Version:** 3.x.x (update this after downloading)  
**Source:** https://github.com/FreeRDP/FreeRDP/releases  
**License:** Apache 2.0

## Required Files

Place the following in `installer/freerdp/`:

- `wfreerdp.exe` — The Windows FreeRDP client with RAIL support
- All required DLLs (shipped alongside the .exe in the release zip)

## How to Update

1. Go to https://github.com/FreeRDP/FreeRDP/releases
2. Download the latest stable Windows x64 release (`.zip`)
3. Extract `wfreerdp.exe` and all `.dll` files
4. Replace contents of `installer/freerdp/` with the new files
5. Test a RemoteApp connection:
   ```
   wfreerdp.exe /v:your-server /u:testuser /from-stdin /app:"||YourApp" /cert:tofu
   ```
6. Update the version number at the top of this file
7. Rebuild the installer

## Testing Checklist

- [ ] RemoteApp launches as independent floating window (RAIL mode)
- [ ] Clipboard sharing works (`+clipboard`)
- [ ] Drive redirection works (`/drive:Home,$USERPROFILE`)
- [ ] Printer redirection works (`/printer`)
- [ ] Certificate TOFU prompt appears on first connect, not on subsequent
- [ ] Credentials passed via stdin (not visible in `tasklist /v`)
- [ ] Clean exit when user closes the RemoteApp window

## Architecture Notes

- The launcher expects FreeRDP at `{app}\freerdp\wfreerdp.exe`
- Credentials are passed via `/from-stdin` — password is written to stdin
- RAIL (Remote Applications Integrated Locally) provides true multi-window RemoteApp
- `/cert:tofu` = Trust On First Use — user sees prompt once, then cert is cached

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| "FreeRDP not found" | Missing from install dir | Re-run installer or manually copy files |
| App doesn't float | Missing RAIL support | Ensure you're using `wfreerdp.exe` not `xfreerdp` |
| Auth failure | Wrong credentials | Use Settings gear to re-enter password |
| Exit code 131 | Network/TLS error | Check server address, try `/cert:ignore` for testing |
