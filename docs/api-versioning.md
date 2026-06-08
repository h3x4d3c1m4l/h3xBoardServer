# API versioning

Two fully independent axes:

| Axis | Where | What it versions |
| --- | --- | --- |
| **Transport** | URL — `/ws/v1`, `/ws/v2` | WebSocket handshake, message framing, RPC protocol — e.g. switching away from JSON-RPC entirely |
| **Service** | Method name — `auth.v1.*`, `auth.v2.*` | The API contract of a specific service group |

These axes are orthogonal. Examples of valid combinations:

| Setup | Meaning |
| --- | --- |
| `/ws/v1` → `AuthRpcV1` + `BoardsRpcV1` | Baseline |
| `/ws/v1` → `AuthRpcV1` + `AuthRpcV2` + `BoardsRpcV1` | Auth got a breaking change; old clients on `/ws/v1` still work via `auth.v1.*` |
| `/ws/v2` → `AuthRpcV1` + `BoardsRpcV1` | Transport changed, but both service APIs are unchanged |
| `/ws/v2` → `AuthRpcV1` + `AuthRpcV2` + `BoardsRpcV2` | Both axes bumped, v1 auth still available for compatibility |

Method names follow the pattern **`service.vN.action`** — e.g. `auth.v1.register`, `boards.v2.list`. The version in the name is required so that multiple class versions can be registered on the same endpoint without StreamJsonRpc seeing duplicate method names.
