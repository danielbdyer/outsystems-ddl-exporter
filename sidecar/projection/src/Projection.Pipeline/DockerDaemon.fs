namespace Projection.Pipeline

// LINT-ALLOW-FILE-MUTATION: Docker JIT bring-up poll loop mutable (`memoResult` +
//   the `up` poll accumulator). Per audit Lens-2 Tier-1+2; the daemon-readiness
//   poll is the sanctioned mutation boundary for the bring-up budget loop.

open System
open System.IO

/// Docker availability + lazy JIT bring-up. Per session-36 — the
/// session-start hook starts dockerd at session boot, but the
/// daemon can drop mid-session (OOM, signal, environment shift);
/// `ensureRunning` probes responsiveness and re-starts the daemon
/// best-effort so canary tests don't fail spuriously. `isAvailable`
/// stays as the static yes/no for callers that just want a probe;
/// tests that actually need Docker should call `ensureRunning`.
///
/// Re-exposed under `Deploy.Docker` via a nested module abbreviation
/// so every existing `Deploy.Docker.*` call site keeps working;
/// this is the concept-named home (container-daemon lifecycle).
[<RequireQualifiedAccess>]
module DockerDaemon =
    // Canonical Docker Unix socket locations. Two candidates:
    //   1. system-wide socket at /var/run/docker.sock (Linux default)
    //   2. user-scoped rootless socket under ~/.docker/run/docker.sock
    // Path composition flows through `Path.Combine` (audit Top-10
    // #6: filesystem boundary uses BCL primitive, not `sprintf`).
    [<Literal>]
    let private SystemSocketPath : string = "/var/run/docker.sock"

    let private socketCandidates : string list =
        // Defensive against empty home directory (distroless
        // containers; `USER nobody`; k8s pods without `$HOME`):
        // `GetFolderPath SpecialFolder.UserProfile` returns "" on
        // those hosts; `Path.Combine("", ...)` yields a relative
        // path that resolves against `CurrentDirectory`, which
        // is misleading at probe time. Guard the rootless
        // candidate behind a non-empty home.
        let home = Environment.GetFolderPath Environment.SpecialFolder.UserProfile
        if String.IsNullOrEmpty home then
            [ SystemSocketPath ]
        else
            [ SystemSocketPath
              Path.Combine(home, ".docker", "run", "docker.sock") ]

    // Why these specific values (not arbitrary):
    // - `ProbeTimeoutMs = 2000`: `docker version` against a healthy
    //   daemon returns in tens of ms; 2 s is the slack for a
    //   loaded host. Larger timeouts hide a wedged daemon
    //   (downstream canary work fails anyway). 2 s is the smallest
    //   value that doesn't false-negative under host load.
    // - `BringupBudgetMs = 5000`: dockerd cold-start measured at
    //   1-3 s on hosts where bring-up is permitted; 5 s is the
    //   smallest budget that survives a sandbox-loaded p99 cold
    //   boot. Per `DECISIONS 2026-05-10 — Docker probe efficiency`,
    //   higher budgets ARE NOT a help on a host where the daemon
    //   genuinely cannot start (web sandbox without dockerd
    //   privileges) — the budget is paid once per call (×N tests
    //   absent memoization), and then the daemon still won't be
    //   up. Memoization (below) collapses the test-suite-level
    //   cost; the lower per-call ceiling makes even un-memoized
    //   first-call paths reasonable.
    // - `BringupPollMs = 200`: below the perceptual instant-response
    //   threshold (~250 ms) so a successful bring-up returns
    //   without feeling laggy; small enough to detect a fast
    //   bring-up promptly without hammering the probe.
    // The shape is a poll-until-ready loop, not a fixed wait —
    // we exit as soon as the daemon is responsive; the budget
    // is only consumed in the failure case.
    [<Literal>]
    let private ProbeTimeoutMs : int = 2_000

    [<Literal>]
    let private BringupBudgetMs : int = 5_000

    [<Literal>]
    let private BringupPollMs : int = 200

    /// Static probe: env var or socket file present. Cheap; doesn't
    /// contact the daemon. Used as the gate for "this environment
    /// could plausibly run Docker."
    let isAvailable () : bool =
        let hasEnvHost =
            let v = Environment.GetEnvironmentVariable "DOCKER_HOST"
            not (String.IsNullOrWhiteSpace v)
        let socketExists =
            socketCandidates |> List.exists File.Exists
        hasEnvHost || socketExists

    /// Active probe: shell out to `docker version` with a 2 s
    /// ceiling. Captures the daemon-not-listening case that
    /// `isAvailable` (socket-file probe only) misses.
    let private isResponding () : bool =
        try
            let psi =
                System.Diagnostics.ProcessStartInfo(
                    FileName = "docker",
                    Arguments = "version --format \"{{.Server.Version}}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false)
            match System.Diagnostics.Process.Start psi with
            | null -> false
            | p ->
                use _ = p
                if p.WaitForExit ProbeTimeoutMs then p.ExitCode = 0
                else
                    try p.Kill() with _ -> ()
                    false
        with _ -> false

    /// Best-effort daemon spawn followed by poll-until-ready. The
    /// budget is a *ceiling* — we exit as soon as the daemon
    /// answers `isResponding`, so a healthy bring-up returns
    /// within a few hundred ms. The full budget consumes only
    /// when dockerd genuinely failed to start.
    let private startDaemon () : bool =
        try
            let psi =
                System.Diagnostics.ProcessStartInfo(
                    FileName = "sudo",
                    Arguments = "dockerd",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false)
            System.Diagnostics.Process.Start psi |> ignore
        with _ -> ()
        let sw = System.Diagnostics.Stopwatch.StartNew()
        let mutable up = isResponding ()
        while not up && sw.ElapsedMilliseconds < int64 BringupBudgetMs do
            System.Threading.Thread.Sleep BringupPollMs
            up <- isResponding ()
        up

    // Memoization for `ensureRunning`. Per `DECISIONS 2026-05-10 —
    // Docker probe efficiency`: a test suite invokes `ensureRunning`
    // per-test through `skipIfNoDocker`; absent memoization, every
    // canary test pays the full bring-up budget when Docker is
    // unavailable (~14 minutes for a 15-test suite at the prior
    // 30 s budget). Memoization collapses the suite-level cost to
    // a single probe-and-bring-up cycle; the result is cached for
    // the life of the process.
    //
    // Thread-safety: the lock guards both read and write. Re-entry
    // is impossible (the inner work is `isAvailable` /
    // `isResponding` / `startDaemon`, none of which call
    // `ensureRunning`).
    //
    // Reset semantics: `resetMemo ()` clears the cache for callers
    // that need to re-probe (e.g., a long-running CLI that wants
    // to retry after a daemon recovery). Tests do not use it; the
    // canary-test pattern is one probe per process.
    let private memoLock : obj = obj ()
    let mutable private memoResult : bool option = None

    // A warm external SQL Server (`PROJECTION_MSSQL_CONN_STR` — the
    // dev-loop warm container or any reachable instance) needs no
    // *local* Docker daemon: the tests connect over TCP and create
    // per-test databases on it. Short-circuit the daemon probe in
    // that case so the warm-reuse loop runs even where `dockerd`
    // can't be brought up. The literal is inlined (the canonical
    // `Deploy.WarmConnStringEnvVar` lives on the `Deploy` module in
    // the compile order); kept in sync by the single call site.
    let private warmConfigured () : bool =
        not (String.IsNullOrWhiteSpace
                (Environment.GetEnvironmentVariable "PROJECTION_MSSQL_CONN_STR"))

    let private probeOnce () : bool =
        // Per the chapter-3.1 sandbox finding (codified
        // 2026-05-10 — Docker probe efficiency): the dominant
        // worst case in the web sandbox is `isAvailable` true
        // (socket file present from a prior bring-up attempt)
        // but the daemon not responding AND `sudo dockerd`
        // unavailable to us. `BringupBudgetMs` is the ceiling
        // for that loop; memoization (above) keeps the cost
        // suite-wide constant.
        if warmConfigured () then true
        elif not (isAvailable ()) then false
        elif isResponding () then true
        else startDaemon ()

    /// JIT bring-up. Returns true iff Docker is usable for canary
    /// work. Tests gate on this for clean skip-if-unavailable
    /// semantics that survive a mid-session daemon drop.
    ///
    /// **Memoized** per `DECISIONS 2026-05-10 — Docker probe
    /// efficiency`. The bring-up budget is paid at most once per
    /// process. Use `resetMemo ()` if you need to retry after a
    /// known recovery event (rare; tests don't).
    let ensureRunning () : bool =
        lock memoLock (fun () ->
            match memoResult with
            | Some cached -> cached
            | None ->
                let result = probeOnce ()
                memoResult <- Some result
                result)

    /// Clear the memoized `ensureRunning` result. The next call
    /// re-probes from scratch. Per the discipline above: only
    /// callers that have *evidence* of daemon recovery should
    /// invoke this (e.g., a test runner with a between-suite
    /// hook that re-armed Docker).
    let resetMemo () : unit =
        lock memoLock (fun () -> memoResult <- None)
