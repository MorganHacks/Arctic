# atlas

Main API. Registration, identity, profiles. Owns `identity.*`, `applications.*`, `profiles.*`.

One service, multiple projects — not microservices. Only `MorganHacks.Api` references the modules; modules never reference each other directly. Cross-module calls go through DI-wired interfaces, and each module owns its own tables.

See `doc-starter/morganhacks-stack.md`.
