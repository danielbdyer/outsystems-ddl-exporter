# dbchange-lite: how an agent takes a schema change from the developer's words to a pull request

Read this once, then follow the `open` list the tool writes. The tool reads; you decide what to do
with what it read. You never state a confidence level the tool did not reach.

## The five levels

| Level | Name | Reached when |
| --- | --- | --- |
| 0 | Intent | The developer confirmed the operation, and `predict --intent` lists no unexplained operation |
| 1 | In sync | `drift` answers `in-sync` for the environment at the release it was deployed from |
| 2 | Planned | `predict` read the deploy plan for the environment |
| 3 | Measured | Every check and precondition of the plan was answered read-only on the environment |
| 4 | Proven | `prove` deployed the change to a local copy planted with rows that answer as the environment's do |

Dev, and every environment a lead reads, must reach level 3; level 4 wherever `predict` lists an item
no read-only query settles. Prod is never read: its row is assumed as the worst case, and the pull
request carries the `beforePromoting` queries a person runs under Prod rights.

## The steps

1. `dbchange-lite doctor`. Exit 0 or stop and do what each finding's remedy says.
2. Name the operation from the catalog in the developer's words (`confirm-intent`), and have the developer confirm it.
3. `dbchange-lite drift --env dev --at ref:<the tag Dev was deployed from>`. Exit 5 means Dev differs; each finding names the object and its known cause.
4. `dbchange-lite predict --env dev,qa,uat,prod --intent <operation> --at dacpac:<the release Prod runs>`. Read `confidence`, `environments` and `open`.
5. For each item of `open`: a `command` you can run, run it, then run `predict` again; a `command` for a lead, ask the lead to run it and paste the answer; a `person` and a `question`, ask that person the question, in their words, one question at a time.
6. When an item is at level 4, write the rows the copy needs from the environment's `checks` and `preconditions` (a table with rows, a violating row per violated precondition), never copied from an environment, and run `dbchange-lite prove --env dev --rows <file>`. Exit 8 means the copy's rows answer otherwise than Dev's: add or remove rows and run it again.
7. Stop when `open` is empty for every environment the change reaches, or every remaining item names a person. Write the pull request (`author-pr`): quote `confidence` and `provenance` as the tool wrote them, list each planted row, and put each remaining question under its person.

## What the exits mean to you

- 0 `done`: nothing blocks and nothing is open.
- 3 `blocked`: the change blocks on an environment; `blockedBy` says whether BlockOnPossibleDataLoss or a constraint violation stops it. Read the operation's skill for the shape that ships: two releases for a tightening on a populated table, a reconcile step for violating rows.
- 8 `needs-input`: the `open` list has your next step.
- 4, 6, 7, 9: the machine, the configuration, the build or the server refused; each finding carries a remedy. Do the remedy; never work around it.

## What you never do

- You never run a deploy to Dev, QA, UAT or Prod. The deployment step deploys.
- You never plant a row copied from an environment, and never paste a row value into the pull request.
- You never run `prove` against anything but the local server; the tool refuses by name, and so do you.
- You never claim a level from the SQL text. `Measured on dev` means the tool measured Dev.
