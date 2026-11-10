# Kotodama Phase 0 audit

Baseline: EchoXIV v1.0.2.1, upstream commit `3916be1` (jj change `xruuos`), read on 2026-11-10.
The only code change applied so far is the identity rename (`chore: rename plugin identity to Kotodama`).
It moved `EchoXIV/` to `Kotodama/` **without changing any line numbers in `.cs` files**, so every
`file:line` reference below is valid both for upstream (with the `EchoXIV/` prefix) and for this fork.

How things were verified:

- **Static reading** of every file on the outgoing, incoming, router, config and engine paths.
- **Debug build** on Linux: 0 warnings, 0 errors (see `docs/DEV-LINUX.md`).
- **LanguageDetector**: run against real chat strings in a scratch console app that compiled the
  unmodified `LanguageDetector.cs` and `NonLatinScriptKind.cs`. The output is quoted in Finding 9.
- **PLaMo template and transports**: measured live against `http://10.1.0.103:11434` (Ollama 0.40.15).
  Results are in [§4](#4-plamo--ollama-measurements).
- **License**: checked in the repository, in all upstream branches, and through the GitHub API.
- **Not verified**: anything that depends on the running game (hook behaviour, what `a4` points to,
  auto-translate payload text). These items are marked **[in-game]**.

---

## 1. Architecture map

### 1.1 Startup and wiring: `Kotodama/Plugin.cs`

| What | Where |
|---|---|
| Commands `/translate`, `/tl` (same handler), `/kotodama` (was `/echoxiv`) | `Plugin.cs:31-33`, `187-200` |
| Config load and normalise | `Plugin.cs:87`, `1055-1071` → `Configuration.NormalizeDefaults()` `Configuration.cs:262-438` |
| Engine singletons (Google, Papago, LocalAI, Libre, Yandex) | `Plugin.cs:112-116` |
| Router build from `Configuration.Engines` (enabled only, list order) | `UpdateTranslationService()` `Plugin.cs:1092-1166` |
| **"Secondary" translator, hardcoded to Google or Papago at startup** | `Plugin.cs:136-138`, passed to the incoming handler (`143`) and the hook (`164`) |
| Secondary on later rebuilds: first non-primary instance, else **Google** | `Plugin.cs:1149-1150` |
| Incoming handler plus events → history | `Plugin.cs:140-156`, `532-546` |
| Outgoing hook | `Plugin.cs:159-179` → `GameFunctions/ChatBoxHook.cs` |
| `SwitchToGoogleFailover` (wired to hook and incoming) | `Plugin.cs:156`, `171`, `1168-1201` |
| Framework tick: chat visibility, LocalAI recovery poll, cache flush | `Plugin.cs:1208-1231` |
| Dispose | `Plugin.cs:381-452` |

### 1.2 Outgoing path A: plain chat-box text (auto-intercept)

1. `ChatBoxHook.Enable()` hooks `UIModule.ProcessChatBoxEntry(UIModule*, Utf8String*, nint a4, bool saveToHistory)` (`ChatBoxHook.cs:76-88`).
2. `ProcessChatBoxDetour` (`ChatBoxHook.cs:93-159`) runs on the framework thread:
   - It passes the message through untouched when `TranslationEnabled` is off (`98-102`), and when the
     text is empty, starts with `/`, is in `ExcludedMessages`, or is one of our own pending sends (`107-113`).
   - On a cache hit, keyed by `(text, SourceLanguage, TargetLanguage)`, it sends the translation synchronously through `SendTranslated` → `Original` (`118-149`).
   - Otherwise it **swallows the call** (never calls `Original`) and starts `ProcessAsync` (`152`).
3. `ProcessAsync` (`161-206`) captures `UIModule*`, the text, `a4` and `saveToHistory` in a `TranslationContext` and calls `TranslationHelper.TranslateAsync`.
4. `TranslationHelper.TranslateAsync` (`280-441`) runs `Task.Run` per message (`293`):
   - glossary protect (`298`), 30 s timeout (`299`, `TranslationDefaults.cs:9`)
   - router call with source `"auto"` (`313`, `325`)
   - fallback translator on `TranslationRateLimitException` (`331-351`)
   - glossary restore, optional romanisation, cache add, pending-outgoing registration, then `callback(ctx, sentText)`.
   - It calls `callback(ctx, null)` when the result equals the input (`425`), on timeout (`433`) and on any exception (`438`).
5. The callback (`183-204`) marshals to the framework thread and calls `Original(ctx.UiModule, …, ctx.A4, ctx.SaveToHistory)`
   with the translation, or with **the original text when the result is null** (`194-202`).

### 1.3 Outgoing path B: `/tl <text>` and `/translate <text>`

1. `OnCommand` (`Plugin.cs:558-619`) treats some arguments as keywords (`on`, `off`, `config`, `settings`, `chat`, `c`, `reset`, `help`, `?`, `input`, `i`). Everything else goes to `TranslateAndSend` (`616`).
2. `TranslateAndSend` (`Plugin.cs:648-822`) runs `Task.Run`. **Inside the worker** it calls `ParseChannelAndMessage` (`656`):
   - An explicit prefix (`/p /fc /sh /y /s /a /e /l1-8 /cwl1-8 /r /t`) is kept as a string prefix (`849-946`). `/r` reads `AgentChatLog.TellPlayerName` (`893-896`).
   - With no prefix, it reads `AgentChatLog.CurrentChannel` and `RaptureShellModule.ChatType` (`952-989`), but **only to label the history entry**. The prefix stays `null`.
   - An unknown `/command` returns `(null, wholeText)` (`949`), and the whole string, command included, is translated.
3. The router call uses `"auto"` (`699-716`). Then `RunOnFrameworkThread` → `RegisterPendingOutgoing` → `SendToChannel(textToSend, prefix)` (`738-766`).
4. `SendToChannel` (`1027-1053`) calls `UIModule.Instance()->ProcessChatBoxEntry(mes)` with the default arguments (`a4 = 0`, `saveToHistory = false`). It doesn't call `SanitizeString`. (The sanitising helper `GameFunctions/ChatBox.cs:11-41` is never used.)
   The call goes back through our own detour. Because it starts with `/`, or is a registered pending send, it passes through.
5. On a timeout it **sends the original** (`768-794`). On `TranslationRateLimitException` it switches to Google and retries once, then **sends the original** (`795-812`, `824-841`). Any other exception prints "Translation error" and sends nothing (`813-820`).

### 1.4 Incoming path: `Kotodama/IncomingMessageHandler.cs`

1. It subscribes to `IChatGui.ChatMessage` (`136`), which runs on the framework thread. `OnChatMessage` (`195-347`) takes `message.Message.TextValue` (`199`), so payloads are flattened to text.
2. If the text matches a pending outgoing send, it is logged as our own message and not translated (`201`, `275-293`). Otherwise the channel must be enabled (`212`). RMT spam (`296`), `/` lines (`303`) and excluded messages (`307-321`) are skipped. In-flight duplicates are dropped (`323-332`).
3. `OnTranslationStarted` adds a placeholder history row synchronously, so the window keeps arrival order (`344`). `TranslateAsync` is then fire-and-forget (`346`).
4. `TranslateAsync` (`358-589`):
   - it limits concurrency to 3 (`96`, `364`)
   - skip-same-language uses `LanguageDetector` (`367-380`)
   - cache lookup uses the key `(text, "auto", readingLang)` (`384-410`)
   - then glossary protect, router call with `"auto"` (`444-449`), restore, **unconditional cache add** (`459-464`)
   - on timeout or rate-limit it uses `_secondaryTranslator` and then raises `OnRequestEngineFailover` → `SwitchToGoogleFailover` (`477-570`)
   - final failure shows the original text as the "translation" (`513`, `564`, `574`).

### 1.5 Router: `Kotodama/Services/TranslationRouter.cs`

- It holds an ordered list of `(Id, ITranslationService, DisplayName)` built from enabled instances (`Plugin.cs:1105-1144`).
- `TranslateAsync` (`145-186`) tries the primary, then the rest in list order, skipping "sick" engines. **It catches every exception, marks the engine sick, and when all fail returns the input text unchanged** (`185`).
- `TranslateWithRomanizationAsync` (`57-98`) tries only `ICombinedTranslator` engines and throws when none succeed (`97`).
- Recovery probes send the literal text `"hi"` to every sick engine, cloud ones included, every `EngineRecoveryCheckMinutes` (`219-280`, `266`).
- Because the router swallows all engine exceptions, `TranslationRateLimitException` never reaches the hook or `/tl` handlers. Those `catch` blocks only fire if the router itself is bypassed.
  The **timeout** path is different: the router rethrows OCE when the outer token fires (`175-178`), so the incoming secondary/failover path *does* run.

### 1.6 Configuration: `Kotodama/Configuration.cs`

- Serialized by Dalamud's Newtonsoft (`TypeNameHandling` from Dalamud). Enums are stored as **integers**, so new `TranslationEngine` values must be appended at the end.
- `TranslationEnabled = true` (`44`). This is the outgoing auto-intercept switch, and it can only be changed with `/tl on|off`. There's no UI toggle.
- First-run engine list: Google, Papago and Yandex, **all enabled** (`346-352`, `531-539`). If no instance is enabled, `NormalizeDefaults` force-enables `Engines[0]` (`357-372`).
- Local AI settings are global fields (`178-216`). `LocalAiTranslatorService` reads these, not `EngineInstance.Endpoint` or `Model`, so several Local AI instances share one endpoint and model.
- `LocalAiUnavailable` (`216`) is set by `SwitchToGoogleFailover` and polled by `OnFrameworkUpdate` (`Plugin.cs:1213-1222`).

### 1.7 Services and UI

- Engines: `Services/*TranslatorService.cs`. Each one has a `static readonly HttpClient` that is never disposed.
- `LocalAiTranslatorService` (OpenAI-compatible `/v1/chat/completions`) has a static English system prompt (`FfxivTranslationPrompt`, `LocalAiTranslatorService.cs:596-656`) and a JSON-mode prompt for romanisation (`463-502`, `response_format` `360`).
- `GlossaryService`: placeholder protector, `[[PF]]` etc. (`GlossaryService.cs:41-65`), plus optional `custom_glossary.json`.
- `TranslationCache`: `translation_cache.json`, 5000 entries, whole file dropped when older than 30 days (`TranslationCache.cs:12-13`, `32`). Key: `v2|text|sl|tl` (`172-173`).
- `MessageHistoryManager`: `history.json`, up to 10 MB of chat, tells included, stored as plain text.
- UI: `UI/ConfigWindow.cs` has General, Engines, Chat, History, Local AI (`1152-1259`) and About tabs, with **no test-connection button**. `UI/TranslatedChatWindow.cs` is the translated chat log with a retry action (`541`). `UI/WelcomeWindow.cs` is the first-run language picker.

---

## 2. Verdicts on findings 1–11

### 1. Silent cloud fallback: **Confirmed, and wider than described**

A local-only user can reach Google, Papago or Yandex through any of these paths:

- **Default engine list.** Google, Papago and Yandex ship enabled (`Configuration.cs:531-539`), and the router tries every enabled instance (`TranslationRouter.cs:158-183`).
- **Hardcoded secondary.** At startup the secondary is `Google` or `Papago` no matter what the user configured (`Plugin.cs:136-138`).
  After any engine change it becomes the first non-primary instance or, if there is none, **`_googleTranslator`**, even when Google isn't in the list (`Plugin.cs:1149-1150`).
- **Incoming timeout path.** When the primary times out, the message goes to that secondary (`IncomingMessageHandler.cs:489-504`), and then `OnRequestEngineFailover` → `SwitchToGoogleFailover` runs.
- **`SwitchToGoogleFailover`.** If a Google instance exists in the list, it **re-enables it** (`google.Enabled = true`), makes it primary, and sets `LocalAiUnavailable` (`Plugin.cs:1181-1188`).
  Disabling Google in the UI therefore doesn't protect you. Only deleting the instance does.
- **`/tl` rate-limit path.** It calls `SwitchToGoogleFailover` and then retries (`Plugin.cs:804-811`). In practice this is unreachable through the router (§1.5), but it stays live code.
- **The hook's `fallbackTranslator`** (`ChatBoxHook.cs:331-351`). It's equally unreachable through the router, but it's the same hardcoded Google/Papago secondary.
- **Recovery probes** send `"hi"` to sick cloud engines (`TranslationRouter.cs:266`). That's low risk, but it's cloud traffic too.

### 2. Every outgoing message intercepted: **Confirmed**

`TranslationEnabled` defaults to `true` (`Configuration.cs:44`). The detour holds and translates every non-empty, non-`/`, non-excluded line (`ChatBoxHook.cs:98-152`).
There's no setting in the UI, only `/tl off`. With the default engines, a freshly installed Kotodama sends **everything you type to Google** until P1/P2 land. See §3.1.

### 3. Original text sent on failure: **Confirmed, in four places, not two**

1. **The router returns its input when every engine fails** (`TranslationRouter.cs:185`). That trips `textToSend != OriginalText` (`ChatBoxHook.cs:394`) → `callback(ctx, null)` (`425`) → `Original(original)` (`194-202`).
2. **A timeout or exception** in `TranslationHelper` → `callback(ctx, null)` (`433`, `438`) → original sent.
3. **`/tl` timeout** → `SendToChannel(message, prefix)` (`Plugin.cs:768-794`).
4. **`/tl` after a failed failover retry** → `SendOriginalAfterTranslationFailure` (`Plugin.cs:799-800`, `824-841`).

Side effect on the incoming side: the same "return input on failure" behaviour gets **cached as a valid translation for up to 30 days** (`IncomingMessageHandler.cs:459-464` has no identity check).

### 4. No ordering: **Confirmed**

- Every held message gets its own `Task.Run` (`ChatBoxHook.cs:293`), and the same is true for `/tl` (`Plugin.cs:651`). There's no sequence number and no queue.
- A **cache hit is sent synchronously inside the detour** (`ChatBoxHook.cs:118-149`), so a cached line typed after an uncached one is guaranteed to overtake it.

### 5. Fragile hook state: **Partly true. The risk is real, but its size is unknown [in-game]**

- ClientStructs declares `ProcessChatBoxEntry(Utf8String* message, nint a4 = 0, bool saveToHistory = false)` (`FFXIVClientStructs/FFXIV/Client/UI/UIModule.cs`, current `main`). `a4` has no name and no documentation, and ClientStructs' own convenience call uses `0`. Kotodama's `SendToChannel` also uses `0`.
- The game's own chat-input call passes some non-zero `a4`. It is most likely a pointer to input-side state, but that is unproven. It is stored and replayed seconds later (`ChatBoxHook.cs:168`, `198`). If it is a stack or temporary pointer, replaying it is a use-after-free.
- `UIModule*` is a process-lifetime singleton, so replaying it is fine in practice. A cleaner option is to re-fetch `UIModule.Instance()` at send time.
- **Recommendation:** never replay `Original` with captured arguments. Send every translated message through a fresh `UIModule.Instance()->ProcessChatBoxEntry(sanitized, 0, false)` with an **explicit channel prefix captured when the message was entered**. This is the same path `/tl` already uses for explicit prefixes, and that path is known to work. It also fixes the `/tl` "active channel at send time" bug (§3.3).

### 6. Hardcoded prompt: **Confirmed**

`FfxivTranslationPrompt` and `BuildSystemPromptWithRomanji` are static string builders (`LocalAiTranslatorService.cs:463-502`, `596-656`). Gemini, Claude and OpenAI-compatible each have their own copies (for example `GeminiTranslatorService.cs:~303`). Nothing can be edited from config.

### 7. No conversational context: **Confirmed**

Every request carries a single message. The v1.0.2.1 changelog in the upstream `EchoXIV.json` says context processing was removed. The XML doc comment at `LocalAiTranslatorService.cs:324` ("with optional conversation context") is stale: the method has no context parameter.

### 8. "Glossary" is a protector: **Confirmed, with extra defects**

`GlossaryService.Protect` replaces terms with `[[PF]]`-style placeholders and puts them back afterwards (`GlossaryService.cs:106-134`). There is no terminology mapping anywhere. Other problems:

- `\bTERM\b` with `IgnoreCase` (`146-147`). .NET's `\w` includes CJK letters, so **`PFで` or `MBに` never match**. Meanwhile English words like `tea` (TEA), `ls`, `fc` do.
- Placeholders like `[[FC]]` are a corruption risk for any LLM, and PLaMo has its own glossary channel, so it doesn't need them. Keep the protector for the cloud engines and for `<se.N>`/macro tags, but the PLaMo path should send real glossary entries instead.

### 9. Direction is `"auto"`: **Confirmed. LanguageDetector is Partly adequate, not reliable enough as-is**

`"auto"` is passed on every path: `ChatBoxHook.cs:313`, `325`, `340`; `Plugin.cs:701`, `713`; `IncomingMessageHandler.cs:386`, `433`, `446`, `496`, `529`. Router probes use it too.

Measured with the unmodified detector:

| Input | `LanguageDetector` | `NonLatinScriptDetector` |
|---|---|---|
| `MT無敵受けで`, `D1とD2逆です`, `蘇生飛ばしてます`, `LB3お願い` | ja 0.99 | Japanese |
| `MT無敵` | **zh-CN 0.88 (counts as confident)** | **ChineseSimplified** |
| `散開`, `無敵`, `了解` | unknown (fewer than 4 letters) | **ChineseSimplified** |
| `頭割り` | unknown (fewer than 4 letters) | Japanese |
| `ｵｹ` (half-width katakana) | unknown | **Latin** |
| `Please make sure your food hasn't expired.`, `stack`, `ty!` | unknown | Latin |
| `Does D1 take the far tower?`, `I'll MT; PLD can ST.` | en 0.76 (below the 0.85 threshold) | Latin |

Verdict: the detector is designed for many-language detection. It misreads kanji-only Japanese as Chinese, misses half-width kana, and is rarely confident on short English.

**Decision (approved by you through a side session): build a local EN/JA-only script classifier in Core.** Rejected: Lingua and ChatTranslated's detector (kelvin124124/ChatTranslated):

- Lingua (SearchPioneer.Lingua, Apache-2.0) runs offline, but it's overkill for two languages and puts tens to hundreds of MB of models into the game process.
- If Chinese is a candidate language, it risks labelling kanji-only lines (`散開`, `無敵`, `MT無敵`) as Chinese.
- ChatTranslated's wrapper adds online parts: Google detection through GTranslate in `DetectIsoAsync`, and models downloaded at runtime from GitHub.
- ChatTranslated is AGPL-3.0, so its code can't go into this unlicensed EchoXIV-derived codebase. Its ideas, such as a per-channel recency boost, can be reimplemented freely.

Classifier design:

- **Normalise a copy** of the text with NFKC for classification only, and never send the normalised copy. NFKC turns full-width `ＯＫ` into `OK` and half-width `ｵｹ` into `オケ`.
- **Japanese letters:** hiragana, katakana and CJK ideographs, plus `々`.
- **Latin letters:** A–Z plus accented letters (Latin-1 Supplement, Latin Extended-A/B, Latin Extended Additional).
- **Ignored:**
  - punctuation, including the English typographic characters `…–—‚„‘“’”«»`
  - digits, symbols and emoji
  - `、。「」！？～`, `；；`, `・`, `ー`, `〇`
- **Kaomoji guard:** a lone kana with non-letters on both sides doesn't count. Otherwise `¯\_(ツ)_/¯` and `ヽ(°〇°)ﾉ` would make English lines classify as Japanese.
- **Decision rule:**
  - **JA** if `2 × JP letters ≥ Latin letters`, so a single kanji (`草`) is JA.
  - **Unknown** if there are no letters at all (`o/`, `；；`, `…`, digits or emoji only). Unknown means the line passes through untranslated.
  - **EN** otherwise.
- **Expected:**
  - JA: `MT無敵`, `MT無敵受けで`, `D1とD2逆です`, `LB3お願い`, `Fulgent Bladeでスプリントお願いします`
  - EN: `Can we do 頭割り at B?`, `That’s “fine” — café…`
- **Possible later add-ons:** a per-channel memory of the last kana-confirmed language to break ties on kanji-only lines, and a few simplified-only characters (`们这么说没`) meaning "not Japanese, skip".

A Chinese-only line on a JP data centre would be misread as JA, which is acceptable. Outgoing direction stays **explicit** (configured EN→JA), not detected.

### 10. No tests: **Confirmed**

No test project, no `.sln`, no CI workflow in the repository.

### 11. No license: **Confirmed**

- No `LICENSE*`, `COPYING` or `NOTICE` file at `3916be1` or in either upstream branch (`feat/hybrid-wpf-bridge`, `feature/windows-native-refactor`).
- The GitHub API reports `"license": null` for ssalazar217/EchoXIV.
- I haven't changed anything about licensing. **Implication:** without a license, default copyright applies. Private modification is one thing, but a public repository or published release ZIPs (the plogons pipeline) are redistribution of a derivative, which needs the upstream author's permission. This blocks the public release work (change R4 in `docs/PLAN.md`), not development.

### Auto-translate payloads (【…】): **No handling exists**

- Incoming text is `SeString.TextValue` (`IncomingMessageHandler.cs:199`). For an `AutoTranslatePayload`, Dalamud's `Text` is "the actual text displayed in-game". That is the phrase **resolved in the reader's client language**, wrapped in `SeIconChar.AutoTranslateOpen/Close` (private-use glyphs), **not** literal `【】`.
- Nothing strips or protects these spans, so they go to the engine together with the rest of the message. The prompts tell the model to keep `【…】`, a form that never actually shows up in the text. A line that consists only of auto-translate phrases already reads correctly for the receiver, yet it is still translated (EN→EN on an English client) unless skip-same-language happens to be confident.
- Outgoing: the hook reads `Utf8String.ToString()` (`ChatBoxHook.cs:104`), and the translated text replaces the original entirely, so **auto-translate payloads typed in the chat box are lost**. What `ToString()` returns for payload bytes is **[in-game]**.
- **Recommendation:** split incoming SeStrings by payload. Pass the auto-translate payload text through verbatim (placeholder it, or skip the message entirely when it has no other text), and translate only the remaining text. Outgoing: refuse to auto-translate lines that contain payloads (send them unchanged) until it's tested in game.

---

## 3. Other risks found

### 3.1 Before you load the renamed build in game

Phase 0 changed no behaviour. On first load Kotodama has **auto-intercept on and Google, Papago and Yandex enabled**, so anything typed in chat goes to Google.
Before chatting: run `/tl off`, then in `/kotodama` → Engines disable or delete Google, Papago and Yandex and add a Local AI instance. Deleting them is the only reliable option (Finding 1). Alternatively, don't use Kotodama in live chat until P2 and P1 have landed.

### 3.2 Threading

- **Game memory read off the framework thread:** `ParseChannelAndMessage` (it reads `AgentChatLog` and `RaptureShellModule`) runs inside `Task.Run` (`Plugin.cs:651-656`).
- **Engine rebuilds off the framework thread:** `UpdateTranslationService()` (`Plugin.cs:1092`) is called from `CheckLocalAiRecoveryAsync` on the thread pool (`1255`), and from `SwitchToGoogleFailover`, which in turn runs from hook and incoming continuations on the thread pool.
  It disposes services that may be in use and swaps `_translatorService` and the secondary without synchronisation.
- **Blocking I/O on the framework thread:** the translation cache is flushed with a synchronous `File.WriteAllText` of up to 5000 indented JSON entries from `OnFrameworkUpdate` every 30 s (`Plugin.cs:1224-1230`, `TranslationCache.cs:126-153`). This can cause a frame hitch.
- History events (`OnMessageTranslated`) fire on worker threads and mutate `TranslatedChatMessage` objects that the UI is reading. Those are reference writes and benign in practice.

### 3.3 Outgoing correctness and safety

- **`/tl` with no prefix sends to the channel that is active when the translation completes**, not the channel active when you typed it. The prefix is `null` (`Plugin.cs:952-989`, `1032`).
- **Command execution from model output.** Anything sent through `ProcessChatBoxEntry` that starts with `/` is executed as a command. `/tl /em waves` translates the whole string, `/em` included (`Plugin.cs:949`), and sends it without a prefix.
  Any model output that starts with `/` is executed. Outgoing translations must be rejected or escaped when they start with `/`, unless the prefix is ours.
- No `SanitizeString` on the `/tl` path (`Plugin.cs:1044-1046`). The helper that sanitises (`ChatBox.SendMessage`) is dead code.
- Over-long output is **silently truncated to 450 bytes with `...` and sent** (`ChatBoxHook.cs:222-241`, `Plugin.cs:1035-1042`).
- The `/tl` keywords make the messages `on`, `off`, `c`, `i`, `chat`, `help`, `reset`, `config`, `settings` and `?` untranslatable (`Plugin.cs:568-617`).
- Pending-outgoing matching relies on exact text equality (`IncomingMessageHandler.cs:149-193`). If the game alters the echoed text (sanitising, payloads), our own message is treated as incoming and sent to the engine again **[in-game]**.
- Enabling romanisation with only non-combined engines (for example a future PLaMo-only setup) makes every outgoing message fail. `router.TranslateWithRomanizationAsync` throws (`TranslationRouter.cs:97`), and failure currently means the original is sent.

### 3.4 Timeouts, cancellation, lifetimes

- **Local AI timeouts.** The per-attempt timeout is `LocalAiTimeoutSeconds` (default 30) with 3 attempts plus backoff (`LocalAiTranslatorService.cs:53-155`), all inside a 30 s outer budget (`TranslationDefaults.cs:9`).
  After one real timeout there's no time left to retry. A dead LAN server costs 30 s per message before failover or the original is sent.
- **Recovery probes** use their own 5 s token, not linked to plugin lifetime (`TranslationRouter.cs:265`).
- **Dispose order.** Translators are disposed before the hook (`Plugin.cs:390-402`). `IncomingMessageHandler.Dispose` disposes the `SemaphoreSlim` while in-flight tasks may still `Release()` it in `finally` (`IncomingMessageHandler.cs:577-580`, `699-705`), which gives an `ObjectDisposedException` on unload.
- **Static `HttpClient`s** (one per engine class, never disposed, default 100 s timeout) survive plugin unload. They hold sockets and can keep the plugin's load context alive across reloads.

### 3.5 Cache correctness

- The key `v2|text|sl|tl` (`TranslationCache.cs:172`) has **no engine, model, prompt or profile**. After you switch engines you keep getting the old engine's translations for up to 30 days. Incoming uses `sl = "auto"`, and outgoing uses the configured `SourceLanguage`.
- Identity results from failed incoming translations are cached (Finding 3).
- Trimming removes `Keys.First()` (`155-159`), which is roughly insertion order, not LRU.

### 3.6 Privacy

- `history.json` stores up to 10 MB of chat in plain text, tells included, in the plugin config folder.
- Verbose logging writes message text to `dalamud.log`.

### 3.7 Generic Local AI with PLaMo is the wrong tool (confirms P3)

PLaMo's GGUF chat template treats any system message that doesn't start with `[plamo-translate]` as **reference context**. If `target_lang` isn't given, it renders the model's **native JA⇄EN auto-switch**.
So the existing Local AI engine pointed at PLaMo sends its long English instruction prompt as PFML3 `context`, the model picks the direction by itself, and the romanisation path additionally forces `response_format: json_object`.
The template also **raises an error (HTTP 500)** for any text that contains `<|plamo:`. A dedicated service has to sanitise for that.

---

## 4. PLaMo / Ollama measurements

Server: Ollama 0.40.15 on `10.1.0.103`. Model: `hf.co/pfnet/plamo-3-translate-8b-GGUF:Q4_K_M` (plamo3, 8.53B, Q4_K_M, context 262144).

### 4.1 Prompt format (PFML3)

The authoritative source is the GGUF-embedded chat template (`api/show` → `template`, `template_source: gguf_chat_template`). The benchmark script `~/echo.sh` builds the same structure by hand. It has to be byte-exact, so it's shown here verbatim:

```text
<|plamo:tag|>dataset<|plamo:msg|>translation
[<|plamo:tag|>context<|plamo:msg|>{context}]
[for each prior pair: <|plamo:tag|>input[<|plamo:key|>lang<|plamo:val|>{src}]<|plamo:msg|>{source}<|plamo:tag|>output<|plamo:key|>lang<|plamo:val|>{tgt}<|plamo:msg|>{translation}]
[<|plamo:tag|>glossary(<|plamo:index|>{term}<|plamo:item|>{translation})*]
<|plamo:tag|>input[<|plamo:key|>lang<|plamo:val|>{src}]<|plamo:msg|>{text}<|plamo:tag|>output<|plamo:key|>lang<|plamo:val|>{tgt}[<|plamo:key|>form<|plamo:val|>polite|plain]<|plamo:msg|>
```

- The order is fixed: context, then history pairs, then glossary, then input. Glossary comes after history, so it sits right before the input.
- `form` requires `tgt = ja`.
- Stop sequences `<|plamo:tag|>` and `<|plamo:bos|>` are required, because the model defines no defaults.
- Without `lang` on the output, the model detects JA or EN itself and translates into the other. Kotodama should always send an explicit `lang`.
- `echo.sh` omits the input `lang` key. Adding `source_lang` (what the chat-path script does) changes outputs slightly: `蘇生飛ばしてます` → "Skipping resuscitation." (raw) vs "Skipping the resurrection." (chat).

### 4.2 Transport comparison

These ran back to back at temperature 0, with 6 cases × 3 warm runs per mode, and a cold run after `keep_alive: 0` plus confirming via `/api/ps` that the model was unloaded:

| Transport | Cold (first request) | Warm avg | Warm median | Warm p95 |
|---|---:|---:|---:|---:|
| raw `/api/generate` (hand-built PFML3, `keep_alive: 30m`) | 3226 ms (load 3035 ms) | 213 ms | 215 ms | 307 ms |
| native `/api/chat` (template, `keep_alive: 30m`) | 2802 ms (load 2547 ms) | 230 ms | 232 ms | 319 ms |
| OpenAI `/v1/chat/completions` (template) | 2306 ms | 232 ms | 234 ms | 320 ms |

Raid profile (context plus about 20 glossary entries, `echo.sh --profile raid --limit 6`, n=6 per direction, medians):

| Transport | jp-en | en-jp |
|---|---:|---:|
| raw `/api/generate` | 435 ms | 459 ms |
| chat `/v1/chat/completions` | 574 ms | 516 ms |

The outputs were identical in 11 of 12 cases.

Conclusions:

- **The "chat API is about 2× slower" claim is refuted.** Warm, the plain profile is equal within noise (the chat path's prompt is 4 tokens longer because of `source_lang`). With the raid profile, chat is roughly 10–30 % slower on small samples.
- Cold cost is the model load, about 2.3–3.2 s, on every transport. A warm-up request plus `keep_alive` matters far more than which transport is used.
- Warm latency is dominated by fixed overhead, not prompt length: 17–31 prompt tokens take about 55–140 ms to evaluate. The raid profile's ~400-token prompt about doubles total latency. That supports sending **only matching glossary entries**, and putting stable context before variable parts so the prefix cache can be reused.
- **Pick:** raw `/api/generate` with a hand-built PFML3 prompt.
  - It's byte-identical to the prompt that produced the benchmark results.
  - It gives full control over ordering, `keep_alive` and `options`.
  - The prompt builder can be unit-tested.
  - It was the fastest in both runs.
  The template's pass-through rule (a user message that starts with `<|plamo:tag|>dataset<|plamo:msg|>translation` is rendered verbatim) means the same builder also works over `/v1/chat/completions` for non-Ollama servers, if that's ever needed.

### 4.3 Quality signals seen (not scored)

- The plain profile reproduces the known failures: 軽減お願いします → "Please reduce the amount."; MT無敵受けで → "With MT Invincible Defense"; 蘇生飛ばしてます → "Skipping resuscitation."; food → 食品の賞味期限…; Kath → キャス.
- The raid profile fixes 軽減, but **MT無敵受けで → "MT tank invulnerability take/soak a hit"**. That's the literal glossary target `受け → take/soak a hit` leaking into the output.
  Glossary targets must be clean target-language terms. Explanations belong in a note that isn't sent to the model. This drives the P4 glossary design.
