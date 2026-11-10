# Kotodama (言霊)

Japanese ⇄ English chat translation for Final Fantasy XIV, built for Japanese Party Finder raiding and
everyday social chat. It translates with **PLaMo 3 Translate** running on your own Ollama server.

Kotodama is a fork of [EchoXIV](https://github.com/ssalazar217/EchoXIV) by ssalazar217 and keeps its chat
hooking, channel detection, translated-chat window and history. See [CHANGELOG.md](CHANGELOG.md) for what
changed.

## Features

- **Local-only by default.** Chat never goes to a cloud translation service unless you explicitly allow it.
- **Explicit outgoing translation.** Your normal chat is sent as written. `/tl <message>` translates one
  line and sends it to the channel you were in when you typed it.
- **Safe failures.** Messages are sent in order. If a translation fails, nothing is sent and your original
  text is shown to you.
- **Optional preview:** check each translation and press Enter to send or Esc to cancel.
- **PLaMo 3 Translate** with its native prompt format, an explicit translation direction, and model warm-up.
- **Editable profiles** (Raid, Casual) with free-text context and a terminology glossary, as plain files you
  can edit outside the game.
- **Party Finder descriptions:** right-click a listing's details → "Translate (Kotodama)" (Japanese → English).
- **Keeps what shouldn't change:** player names, role markers, macro tags such as `<se.1>`, emoticons and
  auto-translate phrases.

## Setup

1. Run Ollama somewhere on your network and pull the model:

   ```bash
   ollama pull hf.co/pfnet/plamo-3-translate-8b-GGUF:Q4_K_M
   ```

2. In game, open `/kotodama` → **Engines**, select **PLaMo**, enter your server URL (for example
   `http://my-server.lan:11434`) and press **Test connection**.
3. In **General**, set the language you write in (English) and the target language (Japanese).

## Commands

| Command | Description |
|---|---|
| `/tl <message>` | Translate and send to the channel you were in when you typed it. |
| `/tl /p <message>` | Translate and send to a specific channel (`/p`, `/fc`, `/a`, `/s`, `/sh`, `/y`, `/l1`–`/l8`, `/cwl1`–`/cwl8`, `/t First Last@World`, `/r`, `/e`). |
| `/kotodama` | Open settings. |
| `/kotodama mode off\|explicit\|auto` | Outgoing translation mode. |
| `/kotodama profile [name]` | Show or switch the translation profile. |
| `/kotodama reload` | Reload profile files after editing them. |
| `/kotodama chat` / `reset` / `help` | Translated chat window, reset its position, list commands. |

## Profiles

Profiles live in the plugin config folder under `profiles/<name>/` (the settings window shows the path):

- `context.txt`, plus optional `context.ja-en.txt` / `context.en-ja.txt`: free-text background for the model.
- `glossary.tsv`: `ja<TAB>en<TAB>note<TAB>direction`. Extra English spellings go after `|`, and the note is
  never sent to the model.

Kotodama never overwrites your edits. The current defaults are kept in `profiles/.defaults/` so you can
compare them with yours.

## Development

See [docs/DEV-LINUX.md](docs/DEV-LINUX.md) (build, tests, live benchmark) and [RELEASING.md](RELEASING.md).

## Credits

- [EchoXIV](https://github.com/ssalazar217/EchoXIV) by ssalazar217, the base of this plugin.
- [PLaMo 3 Translate](https://huggingface.co/pfnet) by Preferred Networks.
- The Dalamud and XIVLauncher projects.

This is a third-party tool. Use it responsibly and respect Square Enix's terms of service.
