# Developing Kotodama on Linux

Verified on 2026-11-10 on CachyOS (kernel 7.3.3), with FFXIV running through XIVLauncher-RB on
Proton/UMU and Dalamud running inside the Proton prefix.

> **Debug builds only.** Never build `-c Release` locally and never point Dalamud at a Release
> output folder. Release ZIPs are built only by CI from a tag (see `docs/PLAN.md`, change R).

## Toolchain

| Component | Version used | Notes |
|---|---|---|
| .NET SDK | 10.0.114 (`/usr/share/dotnet`) | `dotnet --list-sdks`. Dalamud.NET.Sdk 15 targets `net10.0-windows`. It builds on Linux without the Windows desktop SDK. |
| Dalamud.NET.Sdk | 15.0.0 (NuGet) | Pinned in `Kotodama/Kotodama.csproj`. Restored to `~/.nuget/packages/dalamud.net.sdk/15.0.0`. |
| Dalamud dev assemblies | `~/.xlcore/dalamud/Hooks/dev/` | Written by XIVLauncher-RB. `dev/` is byte-identical to `15.0.3.9/` (`Dalamud.dll`, commit `30509c3b`). |
| jj | 0.47.1 | Colocated with git. Remote `upstream` = ssalazar217/EchoXIV. |

## Where the SDK finds Dalamud

`Dalamud.NET.Sdk/15.0.0/Sdk/Sdk.props` resolves `DalamudLibPath` in this order:

1. `$(DALAMUD_HOME)/`, if the env var is set
2. On Linux: `$(HOME)/.xlcore/dalamud/Hooks/dev/`

On this machine the default from step 2 already points at the XIVLauncher-RB install, so **no
environment variable is needed**. Setting it explicitly also works and gives the same result:

```bash
export DALAMUD_HOME="$HOME/.xlcore/dalamud/Hooks/dev"
```

The build log confirms which path it used: `Dalamud.NET.Sdk: root at /home/kath/.xlcore/dalamud/Hooks/dev/`.
If XIVLauncher updates Dalamud to a new API level, the `dev/` folder updates too. Run a clean
rebuild after a Dalamud update.

## Build (Debug)

From the repository root, build the solution (the plugin and the tests):

```bash
dotnet build Kotodama.sln -c Debug
```

Run the tests:

```bash
dotnet test Kotodama.sln -c Debug
```

Clean rebuild:

```bash
rm -rf Kotodama/bin Kotodama/obj tests/*/bin tests/*/obj && dotnet build Kotodama.sln -c Debug
```

The solution maps the plugin to the `x64` platform, the same as FluffySim. The plugin output therefore lands in
**`Kotodama/bin/x64/Debug/`**: `Kotodama.dll`, the generated `Kotodama.json` manifest, `Newtonsoft.Json.dll`,
`images/` and the satellite resource folders. Expected result: 0 warnings, 0 errors.

Building the project on its own (`dotnet build Kotodama/Kotodama.csproj -c Debug`) writes to
`Kotodama/bin/Debug/` instead. Use the solution build so that Dalamud always loads the same path.

### Tests

`tests/Kotodama.Tests` (xUnit, `net10.0`) has **no Dalamud references**. It compiles the files under
`Kotodama/Core/` directly instead of referencing the plugin. Code in `Core/` must therefore stay
Dalamud-free; if it doesn't, the test project fails to compile.

## Loading the Debug build in game

Dalamud runs under Proton, so it sees Linux paths through the `Z:` drive.

1. In game: `/xlsettings` → *Experimental* → *Dev Plugin Locations*, add
   `Z:\home\kath\Projekty\KathyAuteberry\Kotodama\Kotodama\bin\x64\Debug\Kotodama.dll`
   (this follows the same pattern as the existing FluffySim/IINACT entries in `~/.xlcore/dalamudConfig.json`).
2. `/xlplugins` → *Dev Tools* → enable **Kotodama**.
3. Commands: `/kotodama` (settings), `/tl …` and `/translate …` (translate and send).
4. Config is stored in `~/.xlcore/pluginConfigs/Kotodama.json`, and the cache and history in
   `~/.xlcore/pluginConfigs/Kotodama/`. EchoXIV uses `EchoXIV.json` and `EchoXIV/`, so the two never share state.

Rebuilding while the plugin is loaded: unload it in `/xlplugins` first. Don't turn on Dalamud's automatic
reload for this location unless you want every build to reload the plugin in the running client.

### Running next to EchoXIV

The rename gives Kotodama its own InternalName, assembly, config directory, ImGui window IDs
(`###Kotodama*`) and config command (`/kotodama`). **`/tl` and `/translate` are still registered by
both plugins.** Dalamud refuses a second registration of the same command, so whichever plugin loads
second loses `/tl`. Unload EchoXIV while testing Kotodama's `/tl`. EchoXIV is not currently installed here.

## Ollama (inference server)

- Server: `http://10.1.0.103:11434` (Ollama 0.40.15 on the MacBook). Check it with
  `curl -s http://10.1.0.103:11434/api/version`.
- Model: `hf.co/pfnet/plamo-3-translate-8b-GGUF:Q4_K_M` (family `plamo3`, 8.53B, Q4_K_M).
- The GGUF embeds a Jinja chat template, and Ollama uses it (`template_source: gguf_chat_template`).
  Dump it with:

  ```bash
  curl -s http://10.1.0.103:11434/api/show -d '{"model":"hf.co/pfnet/plamo-3-translate-8b-GGUF:Q4_K_M"}' | jq -r .template
  ```

- The model has no default `stop` parameters. Every request must pass
  `stop: ["<|plamo:tag|>", "<|plamo:bos|>"]`.

## Troubleshooting

- `Dalamud installation not found at …`: `~/.xlcore/dalamud/Hooks/dev/` is missing. Start the game
  once through XIVLauncher-RB with Dalamud enabled, or set `DALAMUD_HOME` to a folder that contains
  `Dalamud.dll` and `FFXIVClientStructs.dll`.
- Restore failures while offline: `packages.lock.json` pins DalamudPackager 15.0.0,
  DotNet.ReproducibleBuilds 1.2.39 and Newtonsoft.Json 13.0.3. They must already be in `~/.nuget/packages`.
