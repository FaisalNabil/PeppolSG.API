# Day 14 – Project Retrospective & Lessons Learned

## What Went Well
1. **Incremental Plan Execution** – Daily objectives with clear scopes kept the project on track.
2. **High Test Coverage** – >90% coverage caught regressions early.
3. **Defence-in-Depth** – Layered security (certificate, SSL, attachments, XXE) reduced risk surface.
4. **Observability** – Correlation IDs, structured logs and health endpoints simplified debugging.

## What Could Be Improved
1. **Earlier CI Setup** – Automated builds were added late; integrating from Day-1 would have surfaced issues sooner.
2. **Performance Profiling** – Preliminary benchmarks are positive, but real-world load testing with encrypted attachments is still pending.
3. **Documentation Cadence** – Documentation was batched near the end; living docs updated alongside code would reduce crunch.

## Lessons Learned
- Encrypting secrets in config removes plaintext exposure with minimal effort.
- Hot-reload of Web.config is simple yet powerful for operations; watch for thread-safety.
- Circuit-breakers combined with stale-cache fallback maintain service availability during outages.
- Combining MSTest with Moq provides fast feedback cycles.

## Next Steps
1. Perform live Peppol network certification tests.  
2. Integrate log shipping to centralized ELK.  
3. Containerize the application for easier deployment across environments. 