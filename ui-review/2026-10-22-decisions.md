# UI/UX decision round — 2026-10-22 (room dlsite-client ui discussion)

Participants: lead (deepseek), cursor-grok subagent, local qwen3.8 (LM Studio, HTTP), codex blocked (thread-start failure).

Votes:
- A1 downloaded cell: qwen leave | cursor (a) locale keys | lead (a) → (a)
- A2 SID watermark: qwen keep | cursor (a) locale key | lead (a) → (a)
- A3 library sync: qwen add button now | cursor (c) docs first | lead (c) → (c)
- A4 workTypeLabel: qwen accept duplication | cursor (a) shared module | lead (a) → (a)
- A5 thumb failure: qwen glyph only | cursor (b) glyph + retry | lead (b) → (b)
- B1 uninstall flag: qwen do now | cursor do now | lead initially defer, reversed → do now
- B2 change-exe: qwen do now | cursor do now | lead initially defer, reversed → do now

Implementation verified: net10.0 and net10.0-windows build; headless UI suite passes.

## Third round — 2026-10-22 (owner approved, room retired)

Route: gpt-6.1-sol via `codex exec -m gpt-6.1-sol` (read-only sandbox). cursor: earlier round. qwen3.8: local HTTP, unreliable here (empty content on large prompts).

| Item | Votes | Decision |
|---|---|---|
| A3 sync entry in empty library | 6.1-sol: docs first then button; cursor: docs first; lead: docs first | docs line + button, same change |
| A6 uninstall failure visible | 6.1-sol: Task<bool> + localized msg | done |
| A8 sort in grid/hybrid | 6.1-sol: toolbar ComboBox + direction | done |
| A9 ClearSelectedExe dead code | 6.1-sol: delete | done |
| Change-exe keeps old exe | 6.1-sol review risk | always-use switched off when the picker opens |
| Filter reset scope | 6.1-sol review defect | reset clears every filter |
| Update column visibility | 6.1-sol review defect | checkbox honoured again |
| AutomationProperties on sort chips | 6.1-sol review risk | rejected, no DSL helper in this FuncUI build; recorded as open |

Verification: net10.0 and net10.0-windows build; headless UI suite passes; locale parity 188 keys; no missing t key.
