# Benchmark fixtures

| File | What it is |
|---|---|
| `cases.tsv` | The 48-case JP⇄EN corpus (24 `jp-en`, 24 `en-jp`), copied verbatim from the PLaMo benchmark script `~/echo.sh`. Columns: `id, direction, source, reference, note`. |
| `regression.tsv` | Known failures, each linked to its bench case by `bench_id`. Columns: `id, bench_id, direction, source, wrong_output, expected, note`. `expected` describes the right meaning and isn't always a literal translation. |
| `review.csv`, `results.jsonl` | Your earlier PLaMo benchmark results, for comparison. |

Rules:

- These are **evaluation data only**. Never copy a reference translation into a prompt, a context
  file or a glossary. `FixtureTests.No_plugin_source_or_default_contains_a_reference_translation`
  fails the build if that happens. Glossary entries should teach terms (`頭割り` → stack), not answers.
- Files are UTF-8 TSV: one header row, tab-separated, no quoting, no empty fields. `#` lines are comments.
- The opt-in live harness (`docs/PLAN.md` P3.5) runs both files against a real Ollama server and writes
  a `review.csv` with the same columns as yours, for manual scoring (0 wrong, 1 usable but flawed, 2 correct and natural).
