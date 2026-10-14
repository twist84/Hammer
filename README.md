# Hammer — standalone

`Hammer` recreates the Halo 3 **gamefile level & campaign** import/export
commands.

- **Forward**: XML level/campaign descriptors → BLF `.mapinfo` / `.campaign`
- **Reverse**: BLF `.mapinfo` / `.campaign` → XML descriptors + localized strings

## Requirements

- [.NET SDK](https://dotnet.microsoft.com/) 10 or newer (`dotnet --version`).

## Layout

```
hammer/
  build.bat        build the tool (dotnet build Hammer.csproj)
  run.bat          build (if needed) and run Hammer, forwarding all args
  Hammer.csproj    the project file
  source/          C# sources (11 *.cs) compiled by Hammer.csproj
  bin/, obj/       build output at the project root (git-ignored)
```

`bin/` and `obj/` are written at the project root; `.gitignore` ignores them.

`data\` (level descriptors + `*_names.txt` / `campaigns.txt`) and `maps\info\` (generated
`.mapinfo`/`.campaign`) are **runtime** directories — not part of this repo. Put your game
data under `.\data\` and run against `.\maps\info\`, or point `Hammer` elsewhere with
`--data-root` / `--info-dir`.

## Build & run

```
build.bat                         # dotnet build Hammer.csproj -c Debug
run.bat --help                    # print the command list
run.bat <command> [options]       # run Hammer (builds on first use)
```

`run.bat` forwards every argument to `Hammer` and uses the **current directory**
by default:

- `--data-root` defaults to `.\data`   (level descriptors + `*_names.txt` / `campaigns.txt`)
- the **maps folder** (where `.mapinfo`/`.campaign` are written/read) defaults to `.\maps\info`

So running from a directory that contains `data\` and `maps\` needs no flags. Every
location is overridable (see below).

## Commands

All eight are top-level subcommands (`run.bat <command> ...`).

### Forward — XML → BLF

| Command | Does |
|---|---|
| `gamefile-scenario-file-make-from-xml <xml>` | Build one `.mapinfo` from a scenario XML |
| `gamefile-campaign-file-make-from-xml <xml>` | Build one `.campaign` from a campaign XML |
| `gamefile-scenario-make-all` | Build `.mapinfo` for every scenario XML under `data\` |
| `gamefile-campaign-make-all` | Build `.campaign` for every campaign XML under `data\` |

Forward options: `-o/--output`, `--data-root`, a positional `signature_provider`
(`secrets` default, or `none`), and `--zero-signature` \| `--copy-signature-from <blf>`.
The `*-make-all` variants also accept `--include-missing-strings`.

### Reverse — BLF → XML

| Command | Does |
|---|---|
| `gamefile-scenario-file-make-from-mapinfo <mapinfo>` | Make one scenario XML (+ strings) from a `.mapinfo` |
| `gamefile-campaign-file-make-from-mapinfo <campaign>` | Make one campaign XML (+ strings) from a `.campaign` |
| `gamefile-scenario-make-from-mapinfo-all` | Make scenario XML/strings for every `.mapinfo` in the maps folder |
| `gamefile-campaign-make-from-mapinfo-all` | Make campaign XML/strings for every `.campaign` in the maps folder |

Reverse options: `-o/--output`, `--name`, `--data-root`, `--strings-file`,
`--no-strings`, `--info-dir`.

### Path overrides cheat-sheet

| Need | Option | Default |
|---|---|---|
| Level descriptors + strings | `--data-root <dir>` | `.\data` |
| Where `.mapinfo`/`.campaign` are read (reverse-all) | `--info-dir <dir>` | `.\maps\info` |
| Single output XML (reverse) / output BLF (forward) | `-o, --output <file>` | derived (maps folder / `data\levels\...`) |
| Single strings file (reverse) | `--strings-file <file>` | `data\levels\...` |

## Signatures

`.mapinfo`/`.campaign` files end with a 256-byte RSA signature. The key that signs
them is not part of this repository, so by default (`secrets`) `Hammer` keeps the
signature of the file it overwrites (output stays byte-identical) and writes zeros
for brand-new files. Use:

- `--zero-signature` — write an all-zero signature (and treat the provider as `none`)
- `--copy-signature-from <file.blf>` — reuse the signature from an existing file

(These two are mutually exclusive.)

## Examples

```
# Build one scenario descriptor into .\maps\info\010_jungle.mapinfo, reusing its signature
run.bat gamefile-scenario-file-make-from-xml data\levels\solo\010_jungle\010_jungle.xml --copy-signature-from maps\info\010_jungle.mapinfo

# Reverse a shipped mapinfo back to XML + strings, in place
run.bat gamefile-scenario-file-make-from-mapinfo maps\info\010_jungle.mapinfo

# Round-trip everything forward (deterministic, zero signatures)
run.bat gamefile-scenario-make-all --zero-signature
run.bat gamefile-campaign-make-all --zero-signature
```

## Exit codes

| Code | Meaning |
|---|---|
| 0 | success |
| 1 | a per-file conversion failed, or an input file was not found |
| 2 | bad arguments / usage (unknown option, missing argument, conflicting flags) |

`*-make-all` / `*-make-from-*-all` print `<n> converted, <m> skipped, <k> failed` and
exit `0` only when nothing failed.
