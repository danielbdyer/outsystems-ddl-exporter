@AGENTS.md

# Claude Code in this repository

- At M0 the operator adds `.claude/settings.json` and the SessionStart and SessionEnd hooks in
  `.claude/hooks/`, because the permission check refuses an agent's edit to its own settings.
  SessionStart installs the .NET SDK that `global.json` names when it is absent, builds `cli` into
  `.dbchange/bin/` when stale, starts Docker and the shared SQL Server container in remote sessions
  only, and runs `dbchange doctor`; SessionEnd runs `dbchange synthetic-copy down --if-idle`.
- `archive/` is readable, because v2 is the specification a port reads. No agent edits it or a
  generated file (`LAWS.md` from M1, `cli/VERBS.md` from M8, and `.claude/skills/` and
  `.claude/agents/`, which return at M7 from `dbchange knowledge package`); the operator's settings
  deny both from M0.
- Allowed without asking: `dbchange doctor`, `read`, `diff`, `classify`, `predict`, `check`,
  `synthetic-copy up`, `synthetic-copy down`; `dotnet build`, `dotnet test`, `dotnet clean`.
- Always asked: every verb that pushes (`profile --commit`, `check cdc --commit`,
  `check environments --page --commit`, `knowledge vendor`).

## Operator reviews and intake

- A question only the operator can answer (a milestone's close, an alignment review, an intake with
  open questions) goes to one private Artifact page built by `ci/review/build.js` from a JSON spec, in
  the register of `AGENTS.md`: a summary first, then what waits on the operator; each decision a card
  that gives the situation and the background, then the question, the recommended answer with its
  reason, and the options; each finding with its evidence, its fix, and fix, won't fix or discuss;
  terms defined in place (`{{Term}}`).
- Publish it with the `db` capability, and republish in place as it changes. Before acting, read the
  answers back with ArtifactData and write what is adopted into `DECISIONS.md`, `VALUES.md`,
  `NEXT.md` or the code; the page only stores the answers.
