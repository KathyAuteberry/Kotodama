# Kotodama 0.1.0.0 — release prepared

First Kotodama release: a fork of EchoXIV 1.0.2.1 by ssalazar217, rebuilt around fast Japanese ⇄ English chat translation with a local PLaMo 3 Translate model.

## Local-only by default

- New fallback policy (None, LocalOnly, Any; default None). Chat never reaches a cloud translation service unless you choose Any. The hardcoded Google/Papago fallback and the automatic switch to Google are gone.
- Fresh installs start with a PLaMo engine and no server URL. Nothing is sent anywhere until you enter your Ollama server.

## Outgoing messages

- Outgoing mode: Off, Only /tl messages (default) or Translate everything I type.
- `/tl <message>` sends to the channel that was active when you typed it, or to an explicit one (`/tl /p …`, `/tl /fc …`, `/tl /t First Last@World …`, `/tl /r …`).
- Messages are sent strictly in the order you typed them. Untranslated lines also wait behind pending translations.
- When a translation fails, nothing is sent by default and your original text is shown locally. You can choose to send the original instead.
- Translations that would run as a command, exceed the chat limit or come back unchanged are never sent.
- Optional translation preview (off by default): each translation waits for Enter to send or Esc to cancel, and an unanswered preview is cancelled after a timeout.
- `/tl` only translates. Settings moved to `/kotodama` (`mode`, `profile`, `reload`, `chat`, `reset`, `help`).

## PLaMo 3 Translate

- Dedicated PLaMo engine using its PFML3 prompt format over Ollama, with an explicit JA→EN or EN→JA direction, a timeout, keep-alive and background warm-up.
- A Test connection button checks the server and model, and runs a sample translation in each direction.
- Japanese is detected by script, so kanji-only lines such as 散開 or MT無敵 count as Japanese.

## Profiles and glossary

- Editable Raid and Casual profiles in the plugin config folder (`profiles/`). Each has a free-text context and a terminology glossary. Your edits are never overwritten; current defaults are kept in `profiles/.defaults/` for comparison.
- Only the glossary terms that occur in a message are sent, in both directions.
- Your character name, party members, a keep-as-written list, emoticons and macro tags such as `<se.1>` are preserved. A translation that drops a macro tag is not sent.
- Auto-translate phrases in incoming chat are shown as they are instead of being sent to the model.

## Reliability

- The translation cache is keyed by engine, model and profile, never stores failures, and is written off the game's main thread.
- Incoming translations are also printed in chat as a local-only `[KT] …` line below the original (stock chat and ChatTwo). It can be turned off.
- Incoming lines that couldn't be translated are marked "(not translated)" and can be retried.
