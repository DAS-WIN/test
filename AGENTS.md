# Repository workflow

- Work directly on `main`. Do not create feature, release, maintenance, or version branches unless the user explicitly changes this policy.
- The implementation follows the 0.1.8 → 0.2.1 → 0.2.2 lineage. Do not reintroduce the abandoned 0.3.0 miner-adapter prototype.
- Keep published releases and assets immutable. Increment the version and add release notes for new application releases.
- Maintain README.md in Traditional Chinese with download links, setup, monitoring, logging, and build instructions.
- Application changes must pass core tests and Windows CI verification before publication.
