# Korean localization integration

This work ports the Korean fork's runtime improvements through `3.2.1-ko.3` to upstream `main` at `84b0527` (after v3.2.4). It preserves upstream version numbers, the installer and loader, game mutation guards, API settings, fast/deep AI modes and window layout.

Included:

- Stable Korean names for 1,069 flag IDs; contextual event labels keyed by both ID and original name (406 entries). Both tables are embedded in the DLL. An optional external `event-names-ko.tsv` overrides built-in event labels.
- Korean labels for conditions and effects, translation before truncation, rich-text/template handling, quest names and official terminology. #29's Korean UI cell corrections are included.
- Separate canonical analysis and Korean presentation for current context, choices/dice, flowcharts, routes, collection progress, jump limitation notices and flag-change popups. Conditions, IDs, event pools, random selection and mutation commands retain their original meaning.
- Cache serialization of raw actions and restoration on the main thread; display-only fields do not serialize closures or invoke game localization from background JSON loading.
- Korean tools, queries, lookup output, summaries, mood/hint metadata and official-dialogue-based voice/address rules for all ten supported personas. Korean prompt selection runs through `AiPrompts.cs`. Fast mode keeps its five-tool filter, shorter prompts, ten dialogue samples and at most twenty experience entries.
- Offline regression tooling in `tools/ko-check/` and additional upstream tests for embedded tables, Korean central prompts/fast mode, voice boundaries and metadata compatibility.

Validation against the official Korean game `release_1.0.6001.0`:

- Full mod build passed (four existing unused-field warnings).
- Combined regression checks: 37,202 passed, including actual request-body construction, canonical parser/jump comparisons, background cache round-trips, display call sites and upstream layout comparisons.
- Differential language checks execute upstream source alongside the integration: 27,960 rendering cases and 90 AI prompt cases are identical for Simplified Chinese, Traditional Chinese, Japanese, English and unknown-language fallback, interleaving Korean lookups to check cache isolation. Original Japanese translations in five UI cells are preserved.
- Upstream non-game tests: 11 passed, 0 failed, 2 skipped (installer binary and packaged loader configuration unavailable). These include actual runtime language switching, tool-schema isolation and Korean/legacy mood-tag parsing.
- Exported flag display combinations: 5,304 checked, 0 failures. Cached condition strings: 5,060 checked, 0 containing Chinese.

Game assets and game installation files are not distributed or modified by these checks. No external AI API calls were made. Interactive gameplay, actual jump/return, reload behavior and live model output still need user testing. The previously known 47 mixed-language F9 full descriptions remain; this port does not claim to fix every remaining translation gap.

Korean rendering/template expansion, name matching, pre-truncation translation, popup payloads, AI query normalization and mood parsing are gated by the resolved display language. Non-Korean UI tables and rendering order retain upstream behavior. Canonical place names stay unlocalized in the atlas cache to avoid language-switch contamination.

The mod's interface language and the game's content language are separate. Story-record summaries, dialogue and option text continue to use the original game's current locale resolver. Selecting Chinese only in the mod while the game stays Korean therefore still shows Korean story excerpts, as upstream does. This integration does not force those game excerpts into Korean or override the game's language setting.

Historical fork release files, fork README, package publisher and fork-specific version numbers are intentionally excluded from the upstream change.
