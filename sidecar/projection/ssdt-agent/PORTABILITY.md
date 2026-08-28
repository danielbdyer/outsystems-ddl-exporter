# Portability — one waist, generated edges

**The design that makes this workflow agnostic to the AI surface (GitHub Copilot in Visual
Studio 2022 or 2026, Claude Code, or any editor that can read a file and run a command) and to
the local development surface (LocalDB, a local SQL Server, or a container — Docker is one
substrate, never a requirement).**

## 1. The shape: an hourglass

Everything variable lives at the edges; everything the workflow depends on passes through one
narrow, surface-neutral waist. The waist is deliberately primitive, because primitive is what
every surface supports:

1. **Files on disk.** The skills, the agents, the record forms, the estate ledgers — plain
   markdown any tool can read. One canonical tree; every packaged surface is a generated
   pointer into it (`scripts/ssdt-agent-package.mjs`).
2. **One command with a structured result.** `scripts/prove.mjs` packages the proving loop —
   build → real delta → Strict publish → classified verdict — as a single invocation that
   prints one JSON object on stdout, human progress on stderr, and carries the verdict class
   in its exit code (0 published · 3 blocked · 4 unreachable · 6 config · 7 build failed ·
   9 indeterminate). A blocked publish is a finding, never a tool error. The scaffolded
   command sequence in `skills/prove-on-dacpac/SKILL.md` remains as the explanation of what
   the tool does, and as the fallback when it cannot run.
3. **One committed substrate declaration.** `prove.config.json` names the engine endpoint,
   the project, the build mode, and the profiles for a machine or a repository. When it is
   absent, the tool falls back to the same detection ladder `skills/talk-to-local-sql/SKILL.md`
   teaches (the Twin, then the warm container, then stop-and-say-so).

Above the waist, the **AI-surface adapters** are all generated from the canonical tree:
`.claude/` pointers for Claude Code; `.github/skills/` + `.github/agents/` for Copilot
editors with native discovery; `.github/copilot-instructions.md`, the path-scoped
`.github/instructions/*`, `.github/prompts/*`, and the generated `skills/INDEX.md` for
editors without it. Below the waist, the **dev-surface adapters** are configuration, not
code: a LocalDB instance, a Developer-edition install, and a container differ only in the
`target` block of the config.

## 2. What equalizes, and what does not

**Equalizable — and equalized here:**

| Variance | The equalizer |
|---|---|
| Skill discovery (2026 18.5+ auto-discovers; 18.4 and 2022 do not) | every entry surface routes to the same canonical body; `skills/INDEX.md` is the one-hop manual route |
| Custom agents (2026) vs none (2022) | personas exist as agent files where supported and as `#prompt:` files where not; both read the same bodies |
| SQL engine (LocalDB / local install / container) | the engine is a connection target in `prove.config.json`; nothing in the loop asks *how* the engine runs |
| Docker present or absent | Docker is incidental: a container is just an endpoint, and the synthetic substrate can travel as data (a `.bacpac`/`.bak` restored into LocalDB) instead of as an image |
| Publish engine | `sqlpackage` everywhere — the one cross-platform constant, pinned via `estate/toolchain.md` |
| Build (classic `.sqlproj` needs MSBuild; SDK-style builds anywhere) | detected from the project file by the tool; each mode carries its own remediation message |
| Shell quirks (Git Bash path mangling, exit-code folklore, the .NET roll-forward shim) | the tool spawns without a shell, sets the shim itself, and classifies from the output text |

**Not equalizable — design around these, do not chase them:**

- **Agentic depth and model quality.** Copilot in Visual Studio 2022 follows long multi-step
  instructions less reliably than 2026's agent mode, and both differ from Claude Code. No
  packaging fixes this.
- **Discovery ergonomics.** On the newest surface a skill activates itself; on the oldest a
  developer clicks a prompt file. The *steps* are the same; the typing is not.
- **Approval UX and context budgets.** How a terminal command is approved, and how much of
  the tree a session can hold, are properties of the editor.

## 3. The floor doctrine

Because the residual differences are all differences in how much intelligence the runtime
can supply, the countermeasure is one rule:

> **Design every workflow to complete on the weakest supported surface — Visual Studio 2022,
> 17.14, agent mode, no skill discovery, no custom agents. Newer surfaces reduce typing;
> they never unlock steps.**

The test for any new capability: could a developer on the 2022 rung drive it with a
`#prompt:` entry, the index, and one terminal command they approve? If not, the capability
is holding too much of its intelligence in the runtime — move it into the waist (a file, a
command, a structured result) until the answer is yes. The corollary is where intelligence
belongs: **judgment in the skills, mechanics in the tool, state in files.** A weak agent
running a strong tool over honest files beats a strong agent running folklore.

## 4. What this costs

The honest trade: the packaged loop hides the individual `sqlpackage` invocations that the
scaffolded form teaches, so a developer learning the mechanism should read the scaffold in
`skills/prove-on-dacpac/SKILL.md` at least once — the tool's own header points there. And
full parity of *experience* across editors is not on offer, only parity of *capability*:
the same change, proven the same way, producing the same record, from any of the surfaces.
