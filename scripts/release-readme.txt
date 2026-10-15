GTA V Save Editor - Windows x64

1. Install the Microsoft .NET 10 Desktop Runtime for Windows x64:
   https://dotnet.microsoft.com/en-us/download/dotnet/10.0
   Choose "Desktop Runtime", not the SDK or ASP.NET Core Runtime.
2. Extract the whole ZIP to a folder you can write to.
3. Run GtaVSaveEditor.exe.

If the app asks for a key, use Import key with your 32-byte key file and one of
your own GTA V saves. Source builds and releases made without a bundled key need this
one-time setup. See the project README and tools/KeyRecovery/README.md for details.

Back up saves before editing. The editor also copies existing files before
overwriting them. Backups: %LocalAppData%\SaveEditor\Backups\gtav-pc
Imported key: %AppData%\SaveEditor\keys\gtav-pc.key

Close GTA V before editing and check cloud-save synchronization before reopening
the game. Story mode PC saves only; GTA Online and console saves are unsupported.
Compatibility with GTA V Enhanced has not been verified.

Source, help, and issues: https://github.com/nikankad/GtaVEditor
Read LICENSE, NOTICE, and THIRD_PARTY_NOTICES.md for distribution terms.
