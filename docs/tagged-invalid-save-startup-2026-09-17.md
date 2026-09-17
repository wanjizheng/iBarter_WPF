# Invalid TAG save startup recovery

The installed crash log showed `TaggedTransportSession.Current` throwing `System.IO.InvalidDataException` while `TaggedRouteControl` was constructed from MainWindow XAML. The subsequent Window_Loaded null reference was a consequence of the incomplete window construction.

The restore filter caught IOException but omitted InvalidDataException, which derives from SystemException, not IOException. Add the explicit exception type so an old route rejected by the stricter capacity rules leaves the session empty, preserves settings, and shows replan guidance instead of interrupting startup.

Verification:

- New `--invalid-save <path>` WPF regression uses the actual deployed invalid route, constructs and loads the control twice, asserts no accepted session/map, retains TAG settings and validation guidance, and verifies the original file was unchanged.
- Debug smoke and Release app builds passed. Existing project warnings remain.
- Dependency package versions matched the installed build. Replaced only `D:\Games\iBarter\iBarter.dll`; backup at `D:\Games\iBarter\backups\startup-fix-20260917-020156`.
- Installed DLL SHA256: `41C08F7032EFEAE4585C05203D87EECC6BF05A515B081E75615297E043D2E4A7`.
- Started the installed executable. Its main window reached title `iBarter`, process responding; crash.log remained 11,103 bytes with no new entries. All 19 saved JSON/XML resource files retained their pre-deployment hashes.
- The installed application was left running. This checks application startup, not live barter/game automation.
