# Recover the GTA V PC save key locally

The inspected GTA V Save Editor v5.9.0.0 constructs its working PC key from two
separate slices of an embedded byte-array resource. Its `GTAV_Class.PC_Key`
initializer does not decrypt the supplied PC save.

This tool reads metadata and resource bytes without loading or executing the
reference editor. It supports the exact reference binary recorded in
`docs/FINDINGS.md`; other builds are rejected because the metadata tokens and
constant offsets can change. No key bytes or reference resources are included
in the source.

From the project directory, recover a key to an ignored local file:

```powershell
dotnet run --project tools/KeyRecovery -- recover 'GtaStuff/GTA V Save Editor.exe' 'GtaStuff/gtasaves/SGTA50000' 'artifacts/keys/gtav-pc.key'
```

The key is written only after the save passes its CHKS checksum and decrypting
then encrypting produces a byte-for-byte identical file. Input saves are never
written. A different existing output key is never overwritten.

To verify an existing key:

```powershell
dotnet run --project tools/KeyRecovery -- verify 'artifacts/keys/gtav-pc.key' 'GtaStuff/gtasaves/SGTA50000'
```

To recover, verify, and install it in this app's current user's key store:

```powershell
dotnet run --project tools/KeyRecovery -- install 'GtaStuff/GTA V Save Editor.exe' 'GtaStuff/gtasaves/SGTA50000'
```

Installation uses `%AppData%/SaveEditor/keys/gtav-pc.key`. Alternatively, use the
app's **Import key…** button with the recovered key file and the sample save.
