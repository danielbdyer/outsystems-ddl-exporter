@AGENTS.md

# Claude Code in this repository

- The SessionStart hook installs the .NET SDK that `global.json` names when it is absent, builds
  `cli` into `.estate/bin/` when stale, starts Docker and the shared SQL Server container in remote
  sessions only, and runs `estate doctor`. The SessionEnd hook runs `estate twin down --if-idle`.
  Both live in `.claude/hooks/`, which the operator adds at M0 with `.claude/settings.json`,
  because the permission check refuses an agent's edit to its own settings.
- `archive/` is readable, because v2 is the specification a port reads. Editing it is denied, and
  so is editing a generated file (`LAWS.md` from M1, `cli/VERBS.md` from M8, and `.claude/skills/`
  and `.claude/agents/`, which return at M7 from `estate knowledge package`).
- Allowed without asking: `estate doctor`, `read`, `diff`, `classify`, `predict`, `check`,
  `twin up`, `twin down`; `dotnet build`, `dotnet test`, `dotnet clean`.
- Always asked: every verb that pushes (`profile --commit`, `check cdc --commit`,
  `check environments --page --commit`, `knowledge vendor`).
