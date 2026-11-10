# Kotodama staged plan (P1–P4, plus release integration)

Each heading below is **one jj change**. Conventional Commit messages are used, matching FluffySim and plogons.
File paths are relative to the repository root. "Core" means `Kotodama/Core/`: Dalamud-free code that
the test project compiles directly (see F1). Every change must leave `dotnet build -c Debug` and
`dotnet test -c Debug` green. Nothing is pushed or tagged without your approval.

## Status (2026-11-10)

Built and committed locally (not pushed, not yet tested in game; see [TESTING.md](TESTING.md)):

- **F1–F3, P2.1–P2.5, P1.1–P1.5, P3.1–P3.5, P4.1–P4.6, R1, R2, R4**, plus a Kotodama README.
- **R3** is a separate local jj change in the plogons repository: `feat(tools): support Kotodama in the manifest updater`.
- **Not built:** P1.6 (translation preview; later, off by default) and P5 (design only).
- 439 xUnit tests and 9 Python tooling tests pass. The build has 0 warnings.

Deviations from the plan as written:

- **P2.4** adds an "engine not configured" block (an empty URL never falls back to localhost). Fresh configs start
  with a PLaMo instance (P3.4).
- **P3.2:** the classifier's lone-letter guard also covers Latin letters, so `o/`, `o7` and a waymark `B` count as Unknown.
- **P3.3:** PLaMo returns text that is already in the target language without a request. The live benchmark
  uses the plugin's own path (`TranslateDetailedAsync`).
- **P4.5:** names, emoticons and macro tags are sent as **identity glossary entries** rather than bracket
  placeholders. A dropped macro tag fails the translation.
- **P4.6:** in mixed incoming lines the auto-translate phrases are shown verbatim in 【】 **in front of** the
  translation. They can never be mistranslated, but their position in the line isn't kept.
- **P1.3:** sends skip the game's `SanitizeString` (its handling of Japanese punctuation is undocumented);
  `OutgoingGuards.Sanitize` strips control and payload bytes instead.

Live benchmark against Ollama 0.40.15 (55 cases: 48 bench plus 7 regression, `tests/Kotodama.LiveBench`):

| Profile | Success | Median | p95 | Notes |
|---|---:|---:|---:|---|
| plain | 55/55 | 256 ms | 363 ms | Reproduces the known failures. |
| Raid | 55/55 | 525 ms | 587 ms | Fixes food/ご飯, crash/落ちました, light/光, tower assignment, stack/spread, mitigation and cooldown names. |
| Raid + `--keep Kath` | 7/7 (regression set) | 578 ms | 634 ms | Kath stays as written. 蘇生飛ばしてます and MT無敵受けで still fail. |

Scores are not judged automatically. The CSVs are in the git-ignored `bench/runs/`.

## Recommended order

**F (foundation) → P2 → P1 → P3 → P4**, with R1–R3 slotted in any time after F, and R4 last.

The P2 → P1 → P3 → P4 order you proposed holds up against the code. There are two changes:

1. **Add F first.** Every P-change needs Dalamud-free tests, and that harness doesn't exist yet. F is small and makes the rest reviewable.
2. **The cache-key fix moves into P2 (P2.5)** instead of waiting for P3. A cache keyed only by `(text, sl, tl)` keeps serving Google-produced translations after cloud engines are gated off. That violates the spirit of "no cloud on local-only" and is a one-file fix.

Why P2 before P1:

- P2 is the smallest change with the biggest safety win, and it also covers **incoming** chat. P1 only covers outgoing.
- P2 also changes the router's failure semantics (it throws instead of returning the input), and P1's failure policy builds on that.
- P3 should not come before P1. A working local model in live chat is only safe once the outgoing path stops replaying hook state and stops sending the original text.

---

## F: Foundation

### F1 `test: add solution and xUnit project with linked Core sources`
- **Scope.**
  - Add `Kotodama.sln` with `Kotodama/Kotodama.csproj` and `tests/Kotodama.Tests/Kotodama.Tests.csproj` (net10.0, xUnit).
  - The test project **links** `Kotodama/Core/**/*.cs` (`<Compile Include=… Link=…>`) instead of referencing the plugin.
    - This avoids a second shipped assembly, which would mean packaging risk and an extra file in the ZIP.
    - It avoids loading Dalamud types in tests.
    - It enforces purity by construction: Core code that touches Dalamud won't compile in the test project.
  - Move the two already-pure files, `LanguageDetector.cs` and `NonLatinScriptKind.cs`, into `Kotodama/Core/`. This is a file move only.
- **Files.** `Kotodama.sln`, `tests/Kotodama.Tests/*`, `Kotodama/Core/` (moved files), `Kotodama/Kotodama.csproj` (only if globbing needs it).
- **Tests.** Characterisation tests for `LanguageDetector` that pin the audit table (Finding 9), including today's wrong answers, marked as such.
- **In-game.** None (no behaviour change). A quick load to confirm the plugin still starts.

### F2 `ci: build and test on push and pull requests`
- **Scope.** `.github/workflows/build.yml`, modelled on FluffySim's `pr-build.yml`:
  - triggers: push and PR, concurrency cancel
  - `windows-2022`, .NET 10, Dalamud `latest.zip` → `%AppData%\XIVLauncher\addon\Hooks\dev`
  - `dotnet build -c Debug`, `dotnet test -c Debug --no-build`
  - Decided: `windows-2022`, for parity with FluffySim.
- **Files.** `.github/workflows/build.yml`.
- **Tests.** Runs only once the GitHub repository exists. Locally: `dotnet test`.
- **In-game.** None.

### F3 `test: add benchmark and regression fixtures`
- **Scope.**
  - `bench/cases.tsv`: the 48 cases from `~/echo.sh`, using its `id, direction, source, reference, note` schema.
  - `bench/regression.tsv`: the 7 regression rows.
  - Your `bench/review.csv` and `bench/results.jsonl` go next to them.
  - A test asserts the fixtures parse, that IDs are unique, and that **no glossary or prompt default contains a full reference sentence**. That last check is a guard against hardcoding answers.
- **Files.** `bench/*`, `tests/Kotodama.Tests/FixtureTests.cs`.
- **In-game.** None.

---

## P2: Local-only provider behaviour

### P2.1 `feat: classify engine instances as local or cloud`
- **Scope.** `Core/EngineLocality.IsLocal(engineType)` classifies by **engine type only**. It never looks at the endpoint URL, so any hostname you enter (`fenchurch.lan`, an IP address, anything your DNS resolves) works the same way.
  - **Local (self-hosted):** Local AI and PLaMo. The endpoint is whatever you typed, and nothing is pre-filled (decision 3).
  - **Cloud:** Google, Papago, Yandex, ChatGPT, Gemini, Claude, DeepSeek, OpenRouter and LibreTranslate. LibreTranslate counts as cloud because its usual endpoint is the public service.
  - The guard's job is to stop Kotodama from **silently** switching to a service you didn't choose. Policing a URL you typed in yourself isn't its job.
- **Files.** `Kotodama/Core/EngineLocality.cs`, tests.
- **Tests.** Every `TranslationEngine` value is classified (a new enum value without a classification fails the test). Local AI and PLaMo → local. Every cloud type → cloud.
- **In-game.** None.

### P2.2 `feat: add fallback policy and filter engines through one choke point`
- **Scope.**
  - New `Configuration.FallbackPolicy { None, LocalOnly, Any }`, default **None**.
  - `Core/EngineSelection.Select(instances, primaryId, policy)` returns the ordered allowed list:
    - **None** = local primary only, no fallback
    - **LocalOnly** = local primary, then other enabled local instances
    - **Any** = today's behaviour
    - A cloud primary under None or LocalOnly gives an **empty** list plus a reason. Translation is then unavailable with a clear notice, rather than silently using cloud.
  - `UpdateTranslationService` builds the router **only** from this list, and the secondary becomes `list[1]` or `null`.
  - Remove the hardcoded Google/Papago secondary (`Plugin.cs:136-138`) and the `?? _googleTranslator` (`Plugin.cs:1149-1150`).
  - Engines tab: policy combo, a "cloud" badge on instances, and cloud instances greyed out under None/LocalOnly.
- **Files.** `Kotodama/Core/EngineSelection.cs`, `Configuration.cs`, `Plugin.cs`, `UI/ConfigWindow.cs`, resources (en and es only; other languages fall back to English).
- **Tests.**
  - Policy × engine-list matrix.
  - The default engine list under None produces zero cloud engines.
  - A cloud primary under None produces an empty list with a reason.
  - Order is preserved under LocalOnly and Any.
- **In-game.**
  - With policy None and the Ollama server stopped: send a `/tl` message and receive an incoming JP line.
  - Expect a local error and the original shown untranslated, and **no** `Google`/`Papago`/`Yandex` lines in `/xllog`.
  - Optionally watch `ss -tnp | grep -i wine` on the host for connections to anything other than your Ollama host.

### P2.3 `fix: make failover respect the fallback policy`
- **Scope.**
  - `SwitchToGoogleFailover` becomes `HandleEngineFailure`. It does nothing unless the policy is `Any`, and even then it **never re-enables a disabled instance**.
  - The `LocalAiUnavailable` auto-switch and recovery run only under `Any`.
  - The hook's and incoming handler's secondary comes from P2.2's list.
  - Every `UpdateTranslationService` call is marshalled to the framework thread, which fixes the thread-pool rebuild race (AUDIT §3.2).
  - Router recovery probes are linked to plugin lifetime and only target selected engines.
- **Files.** `Plugin.cs`, `IncomingMessageHandler.cs`, `GameFunctions/ChatBoxHook.cs`, `Services/TranslationRouter.cs`, `Kotodama/Core/FailoverDecision.cs`.
- **Tests.** `FailoverDecision` under each policy: no switch under None/LocalOnly, and never a re-enable.
- **In-game.** Kill Ollama mid-session under None. The engine list in `/kotodama` must stay unchanged and Google must stay disabled.

### P2.4 `feat: local-only defaults for fresh configs`
- **Scope.**
  - The first-run engine list becomes one **Local AI** instance with **no endpoint** (decided: no default, because the plugin ships publicly). Translation stays unavailable, with a notice that points to settings, until you enter a URL. P3.4 replaces this instance with a PLaMo instance.
  - Google, Papago and Yandex are not pre-created. Users can still add them.
  - `NormalizeDefaults` stops force-enabling `Engines[0]` when that engine is cloud under None/LocalOnly (`Configuration.cs:357-372`).
- **Files.** `Configuration.cs`, `UI/WelcomeWindow.cs` (one line explaining local-only), tests on a Core-extracted `DefaultEngines()`.
- **Tests.** A fresh config contains no cloud engines, and normalise never enables a cloud engine under None.
- **In-game.** Delete `~/.xlcore/pluginConfigs/Kotodama.json`, load, check the Engines tab.

### P2.5 `fix: router reports total failure, and cache keys include the engine`
- **Scope.**
  - `TranslationRouter.TranslateAsync` throws `AllEnginesFailedException` instead of returning the input (`TranslationRouter.cs:185`).
  - Incoming shows the original **marked failed** in the translated window and doesn't cache it, which fixes identity-cache poisoning.
  - Outgoing behaviour is unchanged until P1 (it still goes through `callback(null)`).
  - Cache key → `v3|variant|text|sl|tl`, where the variant is engine type, model and prompt version, with profile added in P4. `v2` entries are ignored.
  - The cache flush moves off the framework thread (async write, debounced).
- **Files.** `Services/TranslationRouter.cs`, `IncomingMessageHandler.cs`, `Services/TranslationCache.cs`, `Plugin.cs` (flush), `Kotodama/Core/CacheKey.cs`.
- **Tests.**
  - `CacheKey` differs per engine, model and prompt version.
  - A router with all engines failing throws.
  - A failure result is never added to the cache (via a fake-translator test on the Core part of the incoming pipeline, if extracted; otherwise in-game only).
- **In-game.** Translate an incoming line with the server up, switch engine, and confirm the line is re-translated rather than served from cache.

---

## P1: Safe, predictable outgoing messages

### P1.1 `feat: add outgoing mode setting`
- **Scope.**
  - `Configuration.OutgoingMode { Off, ExplicitOnly, AutoIntercept }`, default **ExplicitOnly**. The legacy `TranslationEnabled = false` maps to Off.
  - The detour passes plain text straight to `Original` unless the mode is AutoIntercept (or see P1.3's ordering rule).
  - Settings UI combo.
  - Decided: **every `/tl` keyword is removed**, including `on`, `off`, `config`, `chat`, `reset` and `help`. `/tl <text>` only translates, and `/tl` on its own prints usage. Mode is set in the settings UI, and `/kotodama mode off|explicit|auto` exists as a convenience. The window and help commands move to `/kotodama chat|reset|help`.
- **Files.** `Configuration.cs`, `GameFunctions/ChatBoxHook.cs`, `Plugin.cs`, `UI/ConfigWindow.cs`, resources.
- **Tests.** `Core/OutgoingRouting.Decide(mode, text, queueEmpty)` → PassThrough, Enqueue or Bypass, covering `/` lines, excluded lines and empty lines.
- **In-game.**
  - In ExplicitOnly, type a normal line: it arrives instantly and untranslated.
  - `/tl hello` still works (on the old path until P1.3).
  - In AutoIntercept the behaviour is unchanged for now.

### P1.2 `feat: parse and capture the target channel at entry time`
- **Scope.**
  - `Core/ChannelTarget` plus `ChannelPrefixParser`, which parses an explicit prefix:
    - `/p /party /fc /a /s /sh /y /n /e /l1-8 /linkshell1-8 /cwl1-8 /cwlinkshell1-8 /r /t`
    - tells: `/t Name@World`, `/t First Last@World`, `/t "First Last@World"`
  - `ChatTypeToPrefix(chatType, tellName, tellWorld)` turns `RaptureShellModule.ChatType` into an explicit prefix (`/t First Last@World`, `/l3`, …).
  - **Unknown `/commands` are rejected, not translated** (fixes `/tl /em …`).
  - On the Dalamud side, the active channel is read **on the framework thread inside the command handler/detour**. That fixes the worker-thread game-memory read (AUDIT §3.2) and the "channel at send time" bug (AUDIT §3.3).
- **Files.** `Kotodama/Core/ChannelTarget.cs`, `Kotodama/Core/ChannelPrefixParser.cs`, `Plugin.cs` (replaces `ParseChannelAndMessage`, `Plugin.cs:843-1025`), tests.
- **Tests.** Parser table, including `/tl /t Kath Auteberry@Twintania hi`, quoted names, `/r` with and without a tell target, an unknown command, an empty body, and full-width spaces.
- **In-game.** **[in-game]** Check the mapping table against the real `ChatType` values for every channel you use: Party, FC, LS1–8, CWLS1–8, Say, Tell, Alliance, Novice.

### P1.3 `feat: ordered outgoing queue and clean send path for /tl`
- **Scope.**
  - `Core/OutgoingQueue`: a bounded (default 8), per-session FIFO with sequence numbers and a single consumer.
    - It processes **one item at a time**. PLaMo answers in about 0.2–0.5 s warm, so sequential processing is simpler and gives strict order.
    - A full queue gives a local error and the message is not sent.
    - Each item carries the original, the captured `ChannelTarget`, its mode, and a cancellation token linked to the plugin lifetime.
  - Sending goes through an `ISendSink`. The Dalamud adapter posts to the framework thread and calls `UIModule.Instance()->ProcessChatBoxEntry(sanitized prefix+text, 0, false)` after registering the pending outgoing entry.
    - **No captured `UIModule*` or `a4` is ever replayed.**
    - `SanitizeString` is applied (the logic from the dead `ChatBox.SendMessage`).
  - Output guards fail the item:
    - an empty translation
    - a translation that starts with `/`
    - a translation over 450 bytes (no silent truncation)
    - a translation identical to the source
  - The failure policy is fixed to **DoNotSend** here: a local error that includes the original text. P1.5 makes it configurable.
  - `/tl` (`TranslateAndSend`, `Plugin.cs:648-841`) is rewritten on top of the queue, which removes two of the four original-send paths.
- **Files.** `Kotodama/Core/OutgoingQueue.cs`, `Kotodama/Core/OutgoingGuards.cs`, `Kotodama/GameFunctions/ChatSender.cs` (new, replaces `ChatBox.cs`), `Plugin.cs`, tests.
- **Tests.**
  - Order is preserved when the translator completes out of order (a fake translator with per-item delays).
  - The bound is enforced.
  - Dispose cancels pending items and never sends after dispose.
  - A guard failure produces DoNotSend.
  - Sequence numbers are monotonic.
- **In-game.** Send three quick `/tl` lines, then `/tl /p …`, then a tell. Confirm order and channels, and that a timeout (server stopped) prints the local error and sends nothing.

### P1.4 `feat: route auto-intercept through the queue`
- **Scope.**
  - In AutoIntercept the detour captures the channel, enqueues the line, and returns without calling `Original`. The send happens through `ChatSender`, so the `TranslationContext` replay (`ChatBoxHook.cs:161-206`) and the cache-hit fast path (`118-149`) are deleted.
  - Cache hits go through the queue too. They complete instantly but keep their order.
  - Lines that contain SeString payloads (auto-translate, item links) **pass through untranslated** until tested in game.
  - **Ordering rule in ExplicitOnly:** if the queue isn't empty, a plain line is enqueued as a pass-through item so it can't overtake an earlier `/tl`. Otherwise it goes straight to the game. Decided: strict order.
- **Files.** `GameFunctions/ChatBoxHook.cs` (most of `TranslationHelper` is deleted), `Plugin.cs`, tests extended in `OutgoingQueue` and `OutgoingRouting`.
- **Tests.** Pass-through items keep their order, a cached item doesn't overtake, and payload lines are never enqueued for translation.
- **In-game.** **[in-game]** AutoIntercept in party: quick consecutive lines, chat history (up-arrow) behaviour, auto-translate phrases, item links. Check whether the echo matches our pending entry, so our own lines aren't translated again.

### P1.5 `feat: configurable outgoing failure policy`
- **Scope.** `Configuration.OutgoingFailurePolicy { DoNotSend (default), SendOriginal }`. The UI warns that SendOriginal sends untranslated text. The last original-send paths are deleted from `ChatBoxHook`.
- **Files.** `Configuration.cs`, `Kotodama/Core/OutgoingQueue.cs`, `UI/ConfigWindow.cs`, resources, tests.
- **Tests.** Each failure kind (timeout, engine failure, guard) × each policy gives the expected sink call or local error.
- **In-game.** Server stopped: DoNotSend prints the error with the original text, and SendOriginal sends it to the captured channel.

### P1.6 `feat: optional translation preview` (later, off by default)
- **Scope.** An ImGui popup with the translation, Enter/Confirm and Esc/Cancel. The queue head waits for confirmation (head-of-line blocking is intended), with a timeout that becomes Cancel.
- **Files.** `UI/PreviewWindow.cs`, `Kotodama/Core/OutgoingQueue.cs` (confirmation hook), config, resources.
- **Tests.** The queue waits on confirmation, cancel drops the item, and later items wait.
- **In-game.** Keyboard focus behaviour while in combat.

---

## P3: First-class PLaMo profile

Transport decision (AUDIT §4): **raw `/api/generate` with a hand-built PFML3 prompt.** It was fastest in both runs, it's byte-identical to the benchmarked prompt, and the builder can be tested.

### P3.1 `feat: PFML3 prompt builder`
- **Scope.** `Core/Pfml3PromptBuilder.Build(direction, text, context?, glossary?, form?, history?)`.
  - It follows the template order: dataset, context, history pairs, glossary, input, output with `lang` (and `form` only for `ja`).
  - It neutralises `<|plamo:` in **all** user-supplied text (chat, context, glossary) by inserting U+200B after `<`. Incoming chat from strangers can't inject prompt structure, and the server can't 500 on the template check.
- **Files.** `Kotodama/Core/Pfml3PromptBuilder.cs`, tests.
- **Tests.** Golden strings copied from `~/echo.sh` output: plain jp-en, plain en-jp with `form=polite`, and raid with context plus glossary. Also history-pair ordering per the template, injection neutralisation, and an empty-glossary omission.
- **In-game.** None.

### P3.2 `feat: EN/JA script classifier, direction resolver and PLaMo output cleanup`
- **Scope.**
  - `Core/PlamoOutputCleaner`: trim, cut at any leftover `<|plamo:`, and keep only the first non-empty line when the source is single-line. Strip wrapping quotes only if the source didn't have them.
  - `Core/ScriptLanguageClassifier.Classify(text) → Ja | En | Unknown`: the EN/JA-only classifier designed in AUDIT Finding 9. It classifies an NFKC-normalised copy and never sends that copy. It counts JP letters (kana, CJK ideographs, `々`) against Latin letters (A–Z and accented), ignores punctuation, digits, symbols and emoji, applies the lone-kana kaomoji guard, and decides JA when `2 × JP ≥ Latin`. No letters means Unknown, and an Unknown line passes through untranslated. It doesn't use Lingua or ChatTranslated code (AGPL).
  - `Core/DirectionResolver`: uses the classifier for incoming lines (Ja → JA→EN, En → no translation for an English reader, Unknown → pass through). Outgoing uses the configured direction (default EN→JA), with an optional "detect" toggle that uses the classifier.
  - The existing `LanguageDetector` stays for the cloud engines' skip-same-language path. The PLaMo path doesn't use it.
- **Files.** `Kotodama/Core/PlamoOutputCleaner.cs`, `Kotodama/Core/ScriptLanguageClassifier.cs`, `Kotodama/Core/DirectionResolver.cs`, tests.
- **Tests.** Output cleaner fixtures with stray tags or extra lines. Classifier table tests:
  - **Regression and bench lines → Ja:** `MT無敵`, `MT無敵受けで`, `蘇生飛ばしてます`, `D1とD2逆です`, `LB3お願い`, `散開`, `無敵`, `了解`, `草`.
  - **English lines → En:** the EN bench sources (`Please make sure your food hasn't expired.`, `You can just call me Kath.`, `I'll MT; PLD can ST.`), `stack`.
  - **Mixed lines:** `Fulgent Bladeでスプリントお願いします` → Ja, `Can we do 頭割り at B?` → En.
  - **Kaomoji:** `lol ¯\_(ツ)_/¯` → En, `ok ヽ(°〇°)ﾉ` → En, while a real one-kana word next to Japanese (`はい`) still counts.
  - **Typographic punctuation:** `That’s “fine” — café…` → En, and `…`, `–—‚„‘“’”«»` are ignored in letter counts.
  - **Full-width and half-width forms:** `ＯＫ` → En, `ｵｹ` → Ja, `ｗｗｗ` → En (NFKC gives `www`).
  - **Unknown:** `o/`, `；；`, `…`, `123`, `!!`, emoji-only, empty string.
  - **Ignored Japanese punctuation:** `、。「」！？～・ー〇` don't change the counts.
  - The original string is unchanged after classification (no NFKC leakage).
- **In-game.** None.

### P3.3 `feat: PLaMo translator service`
- **Scope.**
  - `Core/PlamoClient` plus `Services/PlamoTranslatorService : ITranslationService`.
  - `TranslationEngine.Plamo` is **appended** to the enum, because enums are serialised as ints.
  - It has its own `HttpClient` (a `SocketsHttpHandler` with a pooled connection lifetime), disposed on plugin unload.
  - Requests go to `POST /api/generate` with `raw: true`, `stream: false`, `keep_alive` (default `30m`), and `options {temperature (default 0), num_predict (default 192), stop: [<|plamo:tag|>, <|plamo:bos|>]}`.
  - It uses a per-request timeout (default 8 s) linked to plugin lifetime and per-item cancellation. It doesn't retry after a timeout, and retries once on connection-refused.
  - It throws typed `PlamoUnavailableException` and `PlamoTimeoutException`.
  - It doesn't implement `ICombinedTranslator`. Romanisation is disabled while PLaMo is primary, which avoids the throw described in AUDIT §3.3.
  - PLaMo counts as a local engine for P2 locality and for `EngineSelection`.
- **Files.** `Kotodama/Core/PlamoClient.cs`, `Kotodama/Services/PlamoTranslatorService.cs`, `Configuration.cs` (enum and per-instance fields), `Plugin.cs` (`UpdateTranslationService` case, dispose), tests.
- **Tests.** A fake `HttpMessageHandler` covers:
  - a golden request body
  - response parsing
  - timeout → `PlamoTimeoutException`
  - lifetime cancellation → OCE and no exception noise
  - HTTP 500 → unavailable
  - `num_predict` and stop sequences always present
- **In-game.** Set PLaMo as primary under None and run `/tl` plus incoming JP. Latency in `/xllog` should be about 0.2–0.5 s warm.

### P3.4 `feat: PLaMo settings, test connection and warm-up`
- **Scope.**
  - Per-instance settings: base URL, model, timeout, temperature, `keep_alive`, `num_predict`.
  - **Test connection** runs asynchronously, with the button disabled while it runs. It checks `GET /api/version`, then `POST /api/show` (model present, family `plamo3`), then one sample translation in each direction, and shows the outputs and latencies.
  - Background warm-up runs on plugin load and on settings change: a tiny request with `keep_alive`, cancellable.
  - The first-run default (P2.4) becomes a PLaMo instance with an **empty base URL** and the model name pre-filled. Nothing is sent until you enter a URL (for example `http://fenchurch.lan:11434`). Test connection and warm-up are disabled while the URL is empty.
- **Files.** `UI/ConfigWindow.cs`, `Kotodama/Services/PlamoTranslatorService.cs`, `Plugin.cs`, resources.
- **Tests.** The Core part of the test-connection result (parsing `/api/show` and reporting the family check).
- **In-game.** The button works with the server up, the server down, and a wrong model name. The UI never freezes, and closing the window mid-test is fine.

### P3.5 `test: opt-in live benchmark harness`
- **Scope.**
  - `tests/Kotodama.LiveBench`, a console app (not part of `dotnet test`).
  - The xUnit project also has a smoke `[Fact]` that is skipped unless `KOTODAMA_OLLAMA_URL` is set.
  - It runs `bench/cases.tsv` plus `bench/regression.tsv` through the real `PlamoClient` and prompt builder, with plain and active-profile variants.
  - It writes `review.csv` with the exact columns `case_id,direction,profile,run,source,reference,model_output,latency_ms,completion_tokens,manual_score_0_to_2,review_note`, plus `results.jsonl`.
  - **No automatic judging.**
- **Files.** `tests/Kotodama.LiveBench/*`, `Kotodama.sln`, `docs/DEV-LINUX.md` (usage).
- **Tests.** The CSV writer is unit-tested (quoting, Japanese text, newlines).
- **In-game.** None. You score `review.csv` by hand.

---

## P4: Editable context and terminology glossary

### P4.1 `feat: profile file format and loader`
- **Scope.** Profiles are plain files in `~/.xlcore/pluginConfigs/Kotodama/profiles/<name>/`:
  - `context.txt`: shared free text, sent as PFML3 `context`
  - optional `context.en-ja.txt` and `context.ja-en.txt`: appended for that direction (for example the EN→JA register instructions)
  - `glossary.tsv`: `ja<TAB>en<TAB>note<TAB>direction`, where direction is `both|ja-en|en-ja` and defaults to both.
    - `#` lines are comments.
    - `en` may list aliases as `stack|stacks|stacking`; the first one is the canonical target.
    - **The note is never sent to the model.** AUDIT §4.3 shows explanatory targets leaking into the output.
  - The loader is tolerant: bad lines are reported in the UI and log, never fatal.
- **Files.** `Kotodama/Core/Profiles/*.cs`, tests.
- **Tests.** Parsing (comments, aliases, CRLF, BOM, bad column counts) and direction filtering.
- **In-game.** None.

### P4.2 `feat: ship default Raid and Casual profiles without overwriting edits`
- **Scope.**
  - Defaults are embedded resources.
  - On load, **missing files only** are written. Each version also refreshes `profiles/.defaults/<name>/…`, so you can diff new defaults against your edits. User files are never overwritten.
  - Default **Raid** content:
    - jobs and roles: `ナイト`=PLD, `暗黒`=DRK, MT/ST, H1/H2, D1–D4 as identity entries
    - mitigation and cooldowns: `軽減`, `無敵`, `牽制`=Feint, `アムレン`=Arm's Length, `リビデ`=Living Dead
    - mechanics: `頭割り`=stack, `ペア頭割り`=pair stack, `散開`=spread, `塔踏み`=tower soak, `タゲサ`=hitbox, `外周`=arena edge, `南基準`=south-relative, `時計回り`/`反時計回り`, `誘導`=bait
    - consumables: `ご飯`=food, `薬`=pot
    - PF etiquette terms
    - EN→JA context asking for natural PF register (`お願いします`, `〜さん` for roles, short sentences).
  - **Casual** is a lighter context and a small glossary.
  - Every target is a clean term, never an explanation or a full reference sentence (F3 enforces this).
- **Files.** `Kotodama/Profiles/Defaults/**` (embedded), `Kotodama/Core/Profiles/ProfileInstaller.cs`, `Kotodama/Kotodama.csproj` (EmbeddedResource), tests.
- **Tests.**
  - Install into a temp dir.
  - Edit a file, re-run, and the edit is kept.
  - `.defaults` is refreshed.
  - The default glossary contains no bench reference sentence.
- **In-game.** None (the files appear on first load).

### P4.3 `feat: glossary matcher`
- **Scope.** `Core/GlossaryMatcher.Match(text, direction, entries, cap)`:
  - **JA→EN** uses substring matching with longest-match-first and no overlaps, so `反時計回り` wins over `時計回り`.
  - **EN→JA** uses case-insensitive matching with ASCII word boundaries, plus aliases and a simple plural/-ing form (`food` → ご飯, `stack` → 頭割り).
  - The output is capped (default 24 entries) and ordered by position in the source.
- **Files.** `Kotodama/Core/GlossaryMatcher.cs`, tests.
- **Tests.** Regression rows (food, stack, tower), the overlap case, an alias case, and no false hits inside words (`stackable`). Role tags D1–D4 match without catching `D10`.
- **In-game.** None.

### P4.4 `feat: send active profile with PLaMo requests`
- **Scope.**
  - The PLaMo service receives the context plus the matched glossary.
  - The cache variant gets `profile name + content hash`, so edits invalidate the cache.
  - Profile switching from `/kotodama profile <name>` and a UI combo.
  - Profile files are reloaded by a debounced `FileSystemWatcher` (off the framework thread), plus a Reload button.
  - The stable context goes before the variable glossary so Ollama's prefix cache gets reused.
- **Files.** `Kotodama/Services/PlamoTranslatorService.cs`, `Plugin.cs`, `UI/ConfigWindow.cs`, `Kotodama/Core/CacheKey.cs`, resources.
- **Tests.** The cache key changes when profile content changes, and the request body contains only matched entries (fake handler).
- **In-game.** Switch Raid and Casual mid-session, edit `glossary.tsv` outside the game, and confirm the next translation uses it.

### P4.5 `feat: preserve names, macros and markers`
- **Scope.** `Core/TokenGuard`:
  - Macro and placeholder tags (`<se.N>`, `<t>`, `<me>`, `<pos>`, …) become numbered placeholders that are checked after translation. A missing placeholder counts as a failure, which means DoNotSend.
  - Role tags, waymarks (A–D, 1–4) and emoticons (`;w;`, `；；`) become **identity glossary entries**. That's PLaMo's native mechanism and safer than bracket placeholders.
  - Names: an identity entry for your character name, a configurable "names to keep" list (default empty, you add `Kath`), and the current party member names (read on the framework thread and passed in).
  - The existing `GlossaryService` protector is kept for cloud engines and skipped for PLaMo.
- **Files.** `Kotodama/Core/TokenGuard.cs`, `Kotodama/Services/PlamoTranslatorService.cs`, `Plugin.cs` (party names), config/UI, tests.
- **Tests.** Placeholder round trip, a missing placeholder gives a failure, and identity entries are generated (the Kath regression).
- **In-game.** **[in-game]** A macro line with `<se.1>`, party member names, and `；；`.

### P4.6 `feat: pass auto-translate phrases through untouched (incoming)`
- **Scope.**
  - Build the incoming text from SeString payloads.
  - A line that is only auto-translate phrases plus punctuation or emoticons is shown as-is and not translated.
  - Otherwise the auto-translate spans are kept verbatim as placeholders.
- **Files.** `IncomingMessageHandler.cs`, `Kotodama/Core/AutoTranslateSpans.cs`, tests (on a Core representation of the spans).
- **Tests.** Span splitting and the "only auto-translate" detection.
- **In-game.** **[in-game]** Receive auto-translate-only and mixed lines from a JP client.

---

## P5: Conversational context (design only, not built)

- **Mechanism.** Use the PFML3 **`context` field** for recent chat ("Recent party chat: A: …, B: …"), not input/output history pairs. Reasons:
  - History pairs need a *finished translation* for every earlier line. Incoming translations run concurrently (3 slots), so earlier lines may still be pending, which creates ordering dependencies.
  - Raw source lines in `context` have no such dependency, and both directions can mix freely.
  - It's an additive block, so the plain request shape stays the same.
- **Scope of the buffer.**
  - Per channel (Party, FC, Tell+partner, …), a ring buffer of the last N **source** lines with sender names, taken in arrival order on the framework thread.
  - Optional (default off). N ≤ 4 and about 400 characters total.
  - Our own outgoing originals are included.
- **Cache correctness.**
  - When context is non-empty, the request is context-dependent: **skip both cache read and cache write**.
  - Context-free requests (empty buffer, or the feature off) keep using the cache.
  - Hashing the context into the key would make hit rates almost zero for little gain, so the plan doesn't do that.
- **Latency.** Context adds about 50–150 prompt tokens. Its position in the prompt, after the stable profile context, keeps the profile prefix cacheable.
- **Open question for later.** Whether history pairs actually improve PLaMo quality enough to justify the ordering complexity. The live harness from P3.5 can A/B this.

---

## R: Release integration (same pattern as FluffySim and plogons)

The target behaviour:

- A push to `master` (and PRs) runs CI.
- A tag `vX.Y.Z.0` runs CI, then builds Release **on the runner only**, publishes a GitHub release with `Kotodama-X.Y.Z.0.zip`, and updates `KathyAuteberry/plogons` `mostly-harmless.json` from the **downloaded public ZIP**. Downgrades are refused, other entries are preserved, and push races are retried up to 3 times.

### R1 `build: add version props, changelog and release notes tooling`
- **Scope.**
  - `Kotodama.Version.props` (`KotodamaVersion` 0.1.0; assembly, file and tag versions append `.0`). The plogons updater requires `X.Y.Z.0`.
  - The csproj imports the props.
  - `CHANGELOG.md`, and `tools/release_notes.py` plus its test, copied from FluffySim's.
  - Delete upstream `repo.json` (it points at EchoXIV's releases).
  - Add `IconUrl` to `Kotodama.json`.
- **Files.** `Kotodama.Version.props`, `Kotodama/Kotodama.csproj`, `CHANGELOG.md`, `tools/release_notes.py`, `tools/tests/test_release_notes.py`, `repo.json` (deleted), `Kotodama/Kotodama.json`.
- **Tests.** `python -m unittest discover -s tools/tests`. The Debug build reports the new version.
- **In-game.** `/xlplugins` shows 0.1.0.0.

### R2 = F2 (CI on push and PR). Later: add the Python tool tests to it.

### R3 *(in the plogons repository)* `feat(tools): support Kotodama in the manifest updater`
- **Scope.**
  - Generalise `tools/update_manifest.py`, which is hardcoded to FluffySim today (identity check, URL and InternalName lookup).
  - Add a small **allow-list table** `{InternalName: (GitHub repo, asset pattern)}` with FluffySim and Kotodama, and a `--plugin` flag that **defaults to FluffySim**, so FluffySim's existing workflow keeps working unchanged.
  - All existing safety checks stay: CRC, duplicate entries, identity/API, no downgrade, equal version means equal metadata, atomic replace, concurrent-change detection.
- **Files.** `plogons/tools/update_manifest.py`, `plogons/tools/tests/test_update_manifest.py`, `plogons/README.md` and `AGENTS.md` (one line each).
- **Tests.** Existing FluffySim tests unchanged. New Kotodama cases: correct URL, identity mismatch rejected, coexistence with a FluffySim entry, and a FluffySim-only invocation that leaves a Kotodama entry untouched.
- **Push.** Needs your approval (plogons `master` is the publication bookmark).

### R4 `ci: tag-driven release that publishes to plogons`
- **Scope.** `.github/workflows/publish-kotodama.yml`, mirroring `publish-fluffysim.yml`:
  - verify the tag equals `v$(KotodamaVersion).0`, and the tag is an ancestor of `origin/master`
  - Dalamud download, Debug build and tests
  - **Release build on the runner**
  - `tools/package_release.py`: takes DalamudPackager's `latest.zip`, verifies `Kotodama.dll`, `Kotodama.json` (Name, InternalName and AssemblyVersion match), `Newtonsoft.Json.dll` and `images/icon.png`, and writes `dist/Kotodama-X.Y.Z.0.zip`
  - create the release, or reuse it on rerun (never replace the asset)
  - verify the repo is public and the release is not a draft or prerelease
  - checkout plogons with `secrets.PLOGONS_TOKEN`, run its tests, then `update_manifest.py --plugin Kotodama --zip existing/… --version X.Y.Z.0`
  - commit `chore(release): publish Kotodama X.Y.Z.0 metadata`, and push with fetch/reset retry (3×)
- **Files.** `.github/workflows/publish-kotodama.yml`, `tools/package_release.py`, `tools/tests/test_package_release.py`, `RELEASING.md`.
- **Tests.** Packager unit tests on a synthetic ZIP. The workflow itself can only be exercised on GitHub.
- **Prerequisites (yours):**
  - create `KathyAuteberry/Kotodama` on GitHub and add it as the `origin` remote
  - add a fine-grained `PLOGONS_TOKEN` secret to the Kotodama repo (contents read/write on plogons only)
  - branch rules on plogons must allow the bot push
- **Blocked by Finding 11 (no upstream license).** A public repository or release ZIP redistributes EchoXIV-derived code. Get the upstream author's permission or a license first. Until then, R1–R3 and a private repository are fine.

---

## Decisions (resolved 2026-11-10)

1. `/tl` keywords are freed. `/tl` only translates. Mode is set in the UI or with `/kotodama mode`, and window/help commands move to `/kotodama` (P1.1).
2. Strict ordering: plain lines wait behind pending `/tl` items in ExplicitOnly (P1.4).
3. No default inference URL in shipped configs (P2.4, P3.4). Locality is decided by engine type, not by hostname (P2.1).
4. CI runs on `windows-2022`, for parity with FluffySim (F2, R4).
5. License: still open. It doesn't block F, P1–P4 or R1–R3, but it blocks R4 (see Finding 11).
