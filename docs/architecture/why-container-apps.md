# Why Container Apps and not Kubernetes

**Decided when the deployment work started. Container Apps was chosen and AKS
was set aside.** Writing it down now because the reasoning lived only in a
conversation, and the first question anybody asks about a container platform is
why it is not Kubernetes.

## What is actually being run

Three stateless services and a queue worker, against one Postgres database:

| | |
|---|---|
| harbor | reverse proxy, the only public entry point |
| atlas | the API |
| lark | polls a mail queue and sends |
| migrations | a job that runs once per deploy and exits |

No service mesh, no multi-tenancy, no stateful workloads, no sidecars. The
whole system is idle for most of the year and busy for about two weeks.

## The cost, which is the smaller argument

Same workload, Azure retail pricing for `centralus`:

| | per month |
|---|---|
| Container Apps, idle most of the year | **~$38** |
| Container Apps, event month | ~$137 |
| AKS, every month | **~$352** |

The gap is not really about rates. It is that an AKS node pool is billed
whether or not anything is running, plus $73/month for the control-plane SLA
and a load balancer on top. A cluster cannot take advantage of the one property
that makes this workload cheap — that nobody is using it at 4am in March.
Container Apps scales the web-facing services to zero and a request wakes them.

## The operational argument, which is the larger one

**This team turns over every year.** That single fact decides it.

AKS means somebody owns node pools, an ingress controller, cert-manager,
cluster version upgrades, and the specific way all of those fail. That person
graduates. What they leave behind is a cluster the next tech lead cannot debug
and is afraid to upgrade, which is worse than expensive — it is a platform
nobody can safely touch during the week it matters.

Container Apps has no control plane to inherit. It upgrades itself, scales to
zero on its own, and the whole deployment is a Bicep file somebody can read in
an afternoon.

## What would change this

This is a decision that should be revisited, not defended:

- **The workload stops being stateless.** Anything needing persistent volumes,
  StatefulSets or pod-level scheduling control is a genuine reason.
- **It has to run somewhere other than Azure.** Kubernetes is portable in a way
  Container Apps is not, and if that ever becomes a real requirement rather than
  a hypothetical one, this is the trade being paid for.
- **Something cannot be expressed.** Container Apps deliberately exposes less
  than Kubernetes. Hitting that ceiling is a reason; expecting to hit it is not.

None of these are true today, and all of them would be visible well before they
bit.

## The honest caveat

Container Apps scaling to zero means the first request after an idle spell
waits a few seconds while a replica starts. That is fine for an organizer
opening an admin console and not fine for an applicant during registration,
which is why `warmReplicas` exists — see
[deployments](deployments.md).
