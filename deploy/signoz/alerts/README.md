# SigNoz alert rules (repo-owned)

These artifacts live in **FeatureManagement**, not in `BuildingBlocks.Telemetry` or `BuildingBlocks.Aspire.Hosting.SigNoz`.

Import or recreate them in the SigNoz UI (`http://localhost:<ui-port>`) after `aspire run` with `AddSigNoz()`.

## Sample rules

See [high-error-rate.json](high-error-rate.json) and [high-latency.json](high-latency.json).

## Webhooks (optional)

Point SigNoz alert channels at your own webhook (SMS/email bridge). Keep secrets out of git — use user-secrets or environment variables.
