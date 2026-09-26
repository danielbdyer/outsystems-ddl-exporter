# The refusal ledger

The named file behind `skills/review/verdict/SKILL.md` §"The refusal ledger": every
**Approved with a named risk** and every **Escalated** disposition appends one block here, in
the skill's own format, verbatim — the disposition is auditable, not verbal. A risk without
its proof artifact is not a named risk; the CI gate holds that line (every block must carry
its `disposition:` and `proof artifact:` fields).

Blocks append newest-last. The ledger opens empty; the first named risk or escalation on this
estate writes the first block, shaped exactly as the verdict skill specifies:

```
LEDGER — <op> on <object>
  disposition:      Approved with a named risk | Escalated
  risk/escalation:  <one line — the named consumer / the design fork>
  proof artifact:   <the delta line · the Msg + count · the still-blocks probe · the dependency map path>
  routed to:        lead (accept/override) | lead (escalation + 1 question) | persona-1 (teaching fix)
  the one question (if escalated): <the single yes/no the lead must answer>
```

---
