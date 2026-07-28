# Configuration & data schema

All shared state lives in `%ProgramData%\SqlBackup` (override with the
`SQLBACKUP_DATA_DIR` environment variable — used by tests and non-Windows dev):

| Path | Purpose | Written by |
|---|---|---|
| `config.json` | Connections, jobs, notification + service settings | Desktop app |
| `history\history.jsonl` (+ `history.1.jsonl`) | One JSON line per database backup attempt, size-rotated | Service & app (standalone runs) |
| `logs\service-yyyyMMdd.log` | Service log, daily rolling | Service |
| `logs\app-yyyyMMdd.log` | Desktop app log | App |

The app writes `config.json` atomically (temp file + rename) and then asks the
service to reload over IPC; the service also watches the file's timestamp every
scheduler tick, so changes apply without a restart even when IPC is unavailable.

## config.json

JSON conventions: camelCase property names, enums as strings, timestamps as ISO-8601.

```jsonc
{
  "schemaVersion": 1,
  "modifiedUtc": "2026-07-18T21:14:05.1230000+00:00",

  "connections": [
    {
      "id": "6f1f5f2e-83b1-4f5e-9f60-1c9f29a2b1aa",
      "name": "Production",
      "server": "db01\\SQLEXPRESS",          // host | host\instance | host,port
      "authMode": "Sql",                      // "Windows" | "Sql"
      "username": "backup_user",              // SQL auth only
      "protectedPassword": "dpapi:AQAAANC...",// DPAPI blob; never plaintext
      "encrypt": false,                       // require TLS to SQL Server
      "trustServerCertificate": true,
      "connectTimeoutSeconds": 15
    }
  ],

  "jobs": [
    {
      "id": "0b9f2f97-3f43-4f6e-a67a-77a11cf6a8d2",
      "name": "Nightly full",
      "enabled": true,
      "connectionId": "6f1f5f2e-83b1-4f5e-9f60-1c9f29a2b1aa",
      "selectionMode": "Explicit",            // "Explicit" | "AllUserDatabases" | "AllDatabases"
      "databases": ["Sales", "Inventory"],    // Explicit mode only
      "excludedDatabases": [],                // discovery modes only; matched case-insensitively
      "type": "Full",                         // "Full" | "Differential" | "TransactionLog"
      "schedule": {
        "kind": "Daily",                      // "Interval" | "Daily" | "Weekly" | "Cron"
        "intervalMinutes": 60,                // Interval only
        "timeOfDay": "02:30:00",              // Daily/Weekly, local time
        "days": ["Monday", "Friday"],         // Weekly only
        "cronExpression": ""                  // Cron only (5 fields, or 6 with seconds)
      },
      "destinationFolder": "\\\\nas\\sql-backups",
      "subfolderPerDatabase": true,
      "retention": {
        "mode": "KeepLastN",                  // "KeepAll" | "KeepLastN" | "MaxAgeDays"
        "keepLast": 14,
        "maxAgeDays": 30
      },
      "options": {
        "compression": false,                 // WITH COMPRESSION (not on Express)
        "checksum": true,                     // WITH CHECKSUM
        "copyOnly": false,                    // WITH COPY_ONLY (Full/Log)
        "verifyAfterBackup": true,            // RESTORE VERIFYONLY after backup
        "fallbackToFullIfNoBase": true        // Diff/Log with no full backup -> take Full
      },
      "catchUpMissedRun": false,              // run once at startup if an occurrence was missed
      "rpoHours": 26,                         // alert when no successful backup for N hours; 0 = off
      "offsiteDestinationId": null,           // references offsiteDestinations[].id; null = no off-site copy
      "offsiteRetention": { "mode": "KeepAll", "keepLast": 14, "maxAgeDays": 30 },
      // "LocalAndOffsite" (default: keep both) | "LocalOnly" | "OffsiteOnly"
      // OffsiteOnly deletes the local file after a successful upload; a failed
      // upload always keeps it, and so does a missing/unusable destination.
      "storageMode": "LocalAndOffsite"
    }
  ],

  "offsiteDestinations": [
    {
      "id": "9a7e...",
      "name": "NAS",
      "kind": "Sftp",                         // "SmbShare" | "AzureBlob" | "S3" | "Sftp"
      "prefix": "server01",                   // optional key prefix for all uploads
      // SmbShare:  smbPath (\\server\share\folder), optional smbUsername + protectedSmbPassword
      //            (no credentials = accessed as the account the backup engine runs as)
      // AzureBlob: azureContainerUrl + protectedAzureSasToken (SAS needs create/write/list/delete)
      // S3:        s3Bucket, s3Region or s3ServiceUrl (+ s3ForcePathStyle), s3AccessKeyId, protectedS3SecretKey
      // Sftp:      sftpHost, sftpPort, sftpUsername, protectedSftpPassword, sftpRemotePath
      "sftpHost": "nas.local", "sftpPort": 22, "sftpUsername": "backup",
      "protectedSftpPassword": "dpapi:...", "sftpRemotePath": "/backups/sql"
    }
  ],

  "notifications": {
    "emailEnabled": false,
    "smtpHost": "smtp.example.com",
    "smtpPort": 587,
    "useTls": true,
    "smtpUsername": "alerts@example.com",
    "protectedSmtpPassword": "dpapi:AQAAANC...",
    "fromAddress": "alerts@example.com",
    "toAddresses": "dba@example.com; ops@example.com",
    "onlyOnFailure": true,                    // applies to email and webhook; RPO alerts always send
    "subjectPrefix": "[SqlBackup]",
    "webhookEnabled": false,
    "webhookUrl": "",                         // JSON POST; payload includes Slack "text" / Discord "content"
    "eventLogEnabled": false                  // Windows Event Log, source "SqlBackup" (ids 1000/1001/1002)
  },

  "service": {
    "schedulerPollSeconds": 5,
    "rpoCheckMinutes": 15,
    "sqlConnectRetries": 3,                   // extra attempts, backoff 5s/15s/45s
    "sqlRetryBaseDelaySeconds": 5,
    "commandTimeoutSeconds": 0,               // 0 = unlimited (backups can be long)
    "minimumLogLevel": "Information",
    "logRetentionDays": 31,
    "minFreeDiskSpaceWarnMb": 512
  }
}
```

## history.jsonl

One line per database backup attempt:

```json
{"id":"...","jobId":"...","jobName":"Nightly full","database":"Sales","type":"Full",
 "trigger":"Scheduled","startedUtc":"2026-07-18T02:30:00+00:00","durationSeconds":42.7,
 "success":true,"filePath":"\\\\nas\\sql-backups\\Sales\\Sales_Full_20260718_023000.bak",
 "fileSizeBytes":123456789,"message":"BACKUP DATABASE successfully processed ... RESTORE VERIFYONLY passed.",
 "error":null}
```

`trigger` is `Scheduled`, `CatchUp`, `Manual` (via service IPC) or
`ManualStandalone` (run inside the desktop app). `type` records the backup type
actually taken — after a full-backup fallback it can differ from the job type.

## Backup file naming

`{Database}_{Full|Diff|Log}_{yyyyMMdd_HHmmss}.bak` (`.trn` for log backups),
optionally inside one subfolder per database. Retention only ever deletes files
matching this exact pattern for the job's database + type, and always keeps the
newest matching file.

## Secret storage

`protectedPassword` / `protectedSmtpPassword` are produced with Windows DPAPI
(machine scope, extra entropy, `dpapi:` prefix + base64). Machine scope is
required so both the interactive user (app) and the service account can decrypt.
Consequence: any local process able to read `config.json` could decrypt them —
prefer Windows authentication, and tighten the data folder ACL where that matters.
On non-Windows dev machines, `SQLBACKUP_ALLOW_PLAINTEXT_SECRETS=1` enables an
explicitly-unsafe base64 fallback (`plain:` prefix); without it, protecting or
reading secrets off-Windows fails loudly.
