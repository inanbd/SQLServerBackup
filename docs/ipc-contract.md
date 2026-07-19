# IPC contract (desktop app ⇄ service)

Transport: **local named pipe** `SqlBackup.Control.v1`
(override with the `SQLBACKUP_PIPE_NAME` environment variable; tests do this).

Framing: **one request per connection** — the client connects, writes a single
UTF-8 JSON line (`\n`-terminated), reads a single JSON response line, and
disconnects. JSON uses camelCase names and enums as strings.

Pipe security (Windows): `SYSTEM` and `Administrators` get full control,
`Authenticated Users` get read/write — i.e. any local signed-in user can query
status and trigger a configured job, but the pipe is never remotely reachable.
Tighten in `IpcServer.CreateServerStream` if your environment needs it.

## Envelopes

```json
// request
{ "type": "<message-type>", "payload": { ... } }   // payload optional

// response
{ "ok": true,  "error": null, "payload": { ... } } // payload optional
{ "ok": false, "error": "human-readable reason" }
```

## Messages

| type | payload | response payload |
|---|---|---|
| `ping` | — | `{ "version": "1.0.0", "processId": 1234 }` |
| `get-status` | — | `ServiceStatusInfo` (below) |
| `get-history` | `{ "maxEntries": 200, "jobId": "<guid or null>" }` | `{ "entries": [JobHistoryEntry, ...] }` newest first, max 2000 |
| `run-job` | `{ "jobId": "<guid>" }` | `{ "accepted": true/false, "reason": "..." }` |
| `reload-config` | — | `{ "configModifiedUtc": "..." }`, or `ok:false` when config.json cannot be parsed |

Unknown `type` values return `ok:false` with an explanatory error — the
connection is never just dropped.

### ServiceStatusInfo

```json
{
  "serviceVersion": "1.0.0",
  "processId": 1234,
  "startedUtc": "2026-07-18T20:00:00+00:00",
  "configModifiedUtc": "2026-07-18T19:55:12+00:00",
  "configError": null,
  "jobs": [
    {
      "jobId": "0b9f2f97-3f43-4f6e-a67a-77a11cf6a8d2",
      "name": "Nightly full",
      "enabled": true,
      "isRunning": false,
      "nextRunUtc": "2026-07-19T00:30:00+00:00",
      "lastRunUtc": "2026-07-18T00:30:00+00:00",
      "lastRunSuccess": true,
      "lastRunSummary": "OK (2 database(s))",
      "scheduleError": null
    }
  ]
}
```

Semantics worth knowing:

* `run-job` is **fire-and-accept**: the run happens asynchronously in the
  service; watch `get-status` / `get-history` for the outcome. A job that is
  already running is rejected with a reason (no overlapping runs of one job).
* `reload-config` forces an immediate re-read of `config.json`. The service
  also detects file changes on its own every scheduler tick (default 5 s), so
  this is an optimization, not a requirement.
* `configError` non-null means the service is still operating on the **last
  good** configuration and tells you why the newest file could not be loaded.
* Client code lives in `SqlBackup.Core.Ipc.IpcClient`; both sides share the
  DTOs in `SqlBackup.Core.Ipc.IpcProtocol`, so the contract can only drift if
  the two binaries are from different versions — keep the `v1` pipe name suffix
  in step with breaking changes.
