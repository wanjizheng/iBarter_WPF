param(
    [Parameter(Mandatory = $true)]
    [string] $Path
)

$resolvedPath = [System.IO.Path]::GetFullPath($Path)
if (-not [System.IO.File]::Exists($resolvedPath)) {
    throw "LAA target does not exist: $resolvedPath"
}

$stream = [System.IO.File]::Open(
    $resolvedPath,
    [System.IO.FileMode]::Open,
    [System.IO.FileAccess]::ReadWrite,
    [System.IO.FileShare]::Read)
$reader = [System.IO.BinaryReader]::new(
    $stream,
    [System.Text.Encoding]::UTF8,
    $true)
$writer = [System.IO.BinaryWriter]::new(
    $stream,
    [System.Text.Encoding]::UTF8,
    $true)

try {
    if ($stream.Length -lt 256) {
        throw "LAA target is too small to be a valid PE file: $resolvedPath"
    }

    $stream.Position = 0
    if ($reader.ReadUInt16() -ne 0x5A4D) {
        throw "LAA target has no MZ header: $resolvedPath"
    }

    $stream.Position = 0x3C
    $peOffset = $reader.ReadInt32()
    if ($peOffset -lt 0 -or $peOffset + 120 -gt $stream.Length) {
        throw "LAA target has an invalid PE offset: $peOffset"
    }

    $stream.Position = $peOffset
    if ($reader.ReadUInt32() -ne 0x00004550) {
        throw "LAA target has no PE signature: $resolvedPath"
    }

    $machine = $reader.ReadUInt16()
    if ($machine -ne 0x014C) {
        throw ("LAA target is not x86 PE32 (Machine=0x{0:X4}): {1}" -f $machine, $resolvedPath)
    }

    $optionalHeaderOffset = $peOffset + 24
    $stream.Position = $optionalHeaderOffset
    if ($reader.ReadUInt16() -ne 0x010B) {
        throw "LAA target does not have a PE32 optional header: $resolvedPath"
    }

    # NumberOfRvaAndSizes is at OptionalHeader+92 for PE32. A previous
    # implementation accidentally wrote the LAA bit here and corrupted the
    # apphost, so explicitly verify this field before and after the edit.
    $numberOfRvaAndSizesOffset = $optionalHeaderOffset + 92
    $stream.Position = $numberOfRvaAndSizesOffset
    $numberOfRvaAndSizes = $reader.ReadUInt32()
    if ($numberOfRvaAndSizes -ne 16) {
        throw "LAA target has a damaged PE data-directory count: $numberOfRvaAndSizes"
    }

    # IMAGE_FILE_LARGE_ADDRESS_AWARE is bit 0x0020 in the COFF File Header
    # Characteristics field: PE signature (4 bytes) + FileHeader offset 18.
    $characteristicsOffset = $peOffset + 4 + 18
    $stream.Position = $characteristicsOffset
    $characteristics = $reader.ReadUInt16()
    $updatedCharacteristics = $characteristics -bor 0x0020
    if ($updatedCharacteristics -ne $characteristics) {
        $stream.Position = $characteristicsOffset
        $writer.Write([UInt16]$updatedCharacteristics)
        $writer.Flush()
        $stream.Flush($true)
    }

    $stream.Position = $numberOfRvaAndSizesOffset
    if ($reader.ReadUInt32() -ne 16) {
        throw "LAA edit changed the PE data-directory count unexpectedly."
    }

    Write-Host (
        "[LAA] {0}: COFF Characteristics 0x{1:X4} -> 0x{2:X4}" -f
        $resolvedPath,
        $characteristics,
        $updatedCharacteristics)
}
finally {
    $writer.Dispose()
    $reader.Dispose()
    $stream.Dispose()
}
