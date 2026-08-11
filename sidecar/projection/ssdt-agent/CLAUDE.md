# CLAUDE.md — ssdt-agent routing

This directory is the **classify-by-proving skill tree**: it helps an OutSystems-native
developer land a safe SSDT data-model change, and hands the reviewer a pull request they can
approve by reading. `README.md` is the entry surface (the model, the two findings, the read
order); `THE_RECORD.md` governs every word said out loud.

**Routing.** When the user asks for a schema or data-model change in OutSystems vocabulary —
"make Email required", "rename this attribute", "add a reference", "delete the entity" — enter
through **Persona 1's front door**: the `intake` agent (or the `confirm-intent` skill directly),
which names the op-slug, gathers the three state-variables, asks the one business question, and
hands `change-author` a change-spec to prove. A review request ("look at this schema PR",
"reproduce this change") is **Persona 2**: the `reviewer` agent over `skills/review/`.

The tree is packaged into `.claude/skills/` and `.claude/agents/` as **generated dispatch
pointers** (`sidecar/projection/scripts/ssdt-agent-package.py apply`; the `packaging` gate keeps
them in sync). The canonical bodies live here, where their relative citations resolve — a
packaged skill's body routes you to its in-tree file; follow it.

Never edit a packaged pointer by hand; edit the source skill and re-run `apply`. Everything else
about working in this tree — the substrate, the proving loop, the registers, the self-test —
is owned by the tree's own surfaces; this file only routes.
