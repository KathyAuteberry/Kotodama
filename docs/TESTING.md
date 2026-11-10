# In-game test checklist

Everything below needs the running game and can't be covered by the 439 automated tests. Work through it
top to bottom. Items marked **⚠** are the riskiest. Report anything unexpected together with the
`/xllog` lines around it. Turn on verbose logging in Settings → General if you can.

**Safety first.**
- Use the echo channel (`/tl /e …`) for your first translations: nobody else sees it.
- Unload EchoXIV while testing: both plugins register `/tl`.

## 0. Build and load

- [ ] `dotnet build Kotodama.sln -c Debug`
- [ ] The dev plugin location points at
      `Z:\home\kath\Projekty\KathyAuteberry\Kotodama\Kotodama\bin\x64\Debug\Kotodama.dll`. Note the `x64`.
- [ ] Fresh start: if Kotodama was loaded before, delete `~/.xlcore/pluginConfigs/Kotodama.json` and the
      `~/.xlcore/pluginConfigs/Kotodama/` folder.
- [ ] Load the plugin in `/xlplugins`. No errors in `/xllog`.

## 1. First run and local-only defaults

- [ ] The welcome window shows the blue "local AI server … never uses cloud services" note, and the button
      is visible without scrolling.
- [ ] `/kotodama` → Engines shows a single **PLaMo** instance with no URL. The policy is **None (local primary only)**.
      There's a red "no server URL yet" notice, and chat shows the same notice once.
- [ ] `~/.xlcore/pluginConfigs/Kotodama/profiles/` has `Raid/`, `Casual/` and `.defaults/` (with a README).
- [ ] Before any URL is set, `/tl /e hello` prints a local error and nothing is sent.

## 2. PLaMo setup

- [ ] Enter `http://fenchurch.lan:11434` and press **Test connection**. Expect four `[ok]` lines: Server, Model
      (family plamo3), JA→EN and EN→JA, each with a latency. The window stays responsive while it runs.
- [ ] Try a wrong URL and a wrong model name. Each gives a clear `[x]` line and nothing hangs.
- [ ] General: writing language **English**, target language **Japanese**.

## 3. `/tl` basics and channels ⚠

- [ ] `/tl /e Please make sure your food hasn't expired.` gives a Japanese line in echo with ご飯 (Raid profile).
- [ ] With the **active channel set to Party**, type `/tl hello`, then switch the active channel straight away.
      The message must arrive in **Party**.
- [ ] Explicit prefixes work: `/tl /p …`, `/tl /fc …`, `/tl /l1 …`, `/tl /cwl1 …`, `/tl /t First Last@World …`,
      and `/tl /r …` after receiving a tell.
- [ ] ⚠ **Channel mapping:** with each channel you use set as active (Say, Party, FC, LS1–8, CWLS1–8, Tell,
      Alliance, Novice), `/tl hi` goes to that channel. If any goes to the wrong place, report which one.
      The mapping from the game's active-channel value is unverified.
- [ ] `/tl /em waves` is refused ("not a chat channel …") and nothing is sent.
- [ ] `/tl` with no text prints usage. `/tl on` now translates the word "on" (keywords are gone).

## 4. Ordering and failures

- [ ] Send three `/tl` lines quickly, then type a normal (untranslated) line. All four arrive in the order typed.
- [ ] Stop Ollama (or enter a wrong URL), then `/tl /e hi`. Expect "[Kotodama] Not sent: … Your message: hi",
      with nothing sent. It takes at most the timeout (default 10 s on the General tab).
- [ ] Set **If a translation fails → Send my original message** and repeat. The original goes to the channel,
      with a notice. Set it back to Don't send.
- [ ] With the server down, a normal line typed after a failing `/tl` still arrives once the `/tl` gives up.

## 5. Outgoing modes ⚠

- [ ] **Only /tl** (default): normal lines arrive instantly and untranslated. Up-arrow history still works.
- [ ] **Translate everything I type** (`/kotodama mode auto`): plain lines are translated and go to the active
      channel; commands (`/dance`) and `/p …` lines are not translated.
- [ ] ⚠ With auto mode, type a line containing an **auto-translate phrase** (Tab completion) or an **item link**.
      It must be sent **untranslated and intact**.
- [ ] ⚠ Your own translated line appears **once** in the translated chat window (hover shows the original) and is
      not translated again as an incoming message.
- [ ] Back to **Only /tl**.

## 6. Incoming chat

- [ ] A Japanese line in Party or PF chat is translated to English in the translated chat window (Raid profile terms).
- [ ] ⚠ An incoming line made only of **auto-translate phrases** is shown as is, with no "(not translated)" marker.
- [ ] ⚠ A mixed line (phrase plus Japanese text) shows the phrases in 【】 followed by the English translation.
- [ ] English lines are left alone (with verbose logging on, no PLaMo request is logged for them).
- [ ] With Ollama stopped, an incoming Japanese line shows the original with **(not translated)**. Retry (the
      window's retry action) works once the server is back.

## 7. Local-only guarantee ⚠

- [ ] Add a Google engine and make it primary while the policy is None. Expect the red "primary engine is a cloud
      service" block, and nothing is translated.
- [ ] Make PLaMo primary again and stop Ollama mid-session. The engine list stays unchanged (Google stays disabled),
      and `/xllog` has **no** Google, Papago or Yandex request lines.
- [ ] Optional: on the host, `ss -tnp | grep -i wine` shows no connections other than to the Ollama host.

## 8. Profiles

- [ ] `/kotodama profile` lists Raid and Casual. `/kotodama profile casual` switches; the General tab shows it.
- [ ] Edit `profiles/Raid/glossary.tsv` outside the game (add a term). The next translation uses it without
      reloading. `/kotodama reload` prints the profile list.
- [ ] Add a broken line (one column). The General tab shows the ignored-lines warning; translation keeps working.

## 9. Names, macros, emoticons

- [ ] Add `Kath` to "Names and words to keep as written". `/tl /e You can just call me Kath.` keeps "Kath".
- [ ] Party member names in `/tl` lines stay as written.
- [ ] `/tl /e Stack on <t> <se.1>` keeps both tags. If the model ever drops one, the line is refused, not sent.
- [ ] `/tl /e That was close! ;w;` keeps `;w;`.

## 10. Lifecycle and performance

- [ ] While a `/tl` is pending (slow or stopped server), unload the plugin. Nothing is sent after unload, and
      there are no exceptions in `/xllog`.
- [ ] Reload the plugin. The config, profiles and cache survive.
- [ ] No frame hitches while typing, or when the cache flushes (every 30 s).

## Known limitations (not bugs)

- 蘇生飛ばしてます and MT無敵受けで still translate wrongly with the Raid profile (see the live benchmark notes in `docs/PLAN.md`).
- The translation preview (P1.6) isn't built yet.
- Conversational context (P5) is design only.
