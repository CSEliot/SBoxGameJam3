# AI car driver iterations

Per-version log of the live AI car tuning loop (see `../AI-Car-Feedback-Loop.md` for how the loop works).
Each file: context, changes, telemetry results, LLM observations, human observations, outcome.

| Version | Main change | Score (best epoch) | File |
|---|---|---|---|
| v1-baseline | original driver | -69.5 | [v1-baseline.md](v1-baseline.md) |
| v2-corners | braking-distance corner speed, widened corners, three-point turns | -45.6 | [v2-corners.md](v2-corners.md) |
| v3-ledge-uturn | ledge probe, standstill-gated three-point turn | -54.2 | [v3-ledge-uturn.md](v3-ledge-uturn.md) |
| v4-reverse-cap | abs-speed standstill check, reverse speed cap | -9.4 | [v4-reverse-cap.md](v4-reverse-cap.md) |
| v5-pivot-escape | pivot livelock escape, bounded corner snap | -26.8 | [v5-pivot-escape.md](v5-pivot-escape.md) |
| v6-thick-whiskers | full-width thick whiskers (+ human scene rework) | -29.8 | [v6-thick-whiskers.md](v6-thick-whiskers.md) |
| v7-block-hysteresis | Blocked resumes after 0.3 s clear | -8.2 | [v7-block-hysteresis.md](v7-block-hysteresis.md) |
| v8-wake-body | keep the Rigidbody awake; CornerSpeed 350 tuning | +16.9 | [v8-wake-body.md](v8-wake-body.md) |
| v9-quay-guard | rear ledge braking, full-width ledge probes | +12.0 (in progress) | [v9-quay-guard.md](v9-quay-guard.md) |
| v10-corner-curve | corner speed independent of cruise speed | +26.7 | [v10-corner-curve.md](v10-corner-curve.md) |
| v11-reverse-counting | progress-aware reverse count, repick-loop teleport (v11 reverted, v11b/v11c kept) | +26.1 (v11c) | [v11-reverse-counting.md](v11-reverse-counting.md) |
| v12-corner-aim | aim at the next real corner behind gates; hotload radius-clamp fix (v12b-g) | +25.7 (v12; v12g unmeasured) | [v12-corner-aim.md](v12-corner-aim.md) |
| v13-steer-damping | steering deadband + yaw-rate damping | +25.4 (physcal tuning) | [v13-steer-damping.md](v13-steer-damping.md) |
| v14-reverse-safety | brake backward roll before driving; speed-scaled rear stop (after human physics retune + ThrottleResponse calibration) | +20.6 | [v14-reverse-safety.md](v14-reverse-safety.md) |
| v15-tilt-guard | cut throttle and back off when the body tilts (climbing obstacles at torque 90000) | +24.0 | [v15-tilt-guard.md](v15-tilt-guard.md) |

Scores come from `Tools/aicar/analyze.py --live` (higher is better). Destinations are random and several
versions ran in different play sessions, so compare neighbouring rows loosely.
