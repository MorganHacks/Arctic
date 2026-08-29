# deploy

Helm charts and per-environment config. One Argo CD application per service, each watching its own path under `envs/`.

**CI writes to git and the deployer pulls.** CI never holds cluster credentials.
