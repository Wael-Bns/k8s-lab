# k8s-lab

A deliberately minimal .NET Web API plus everything around it (Docker, K8s
manifests, CI/CD, Prometheus/Grafana) so you can experience real Kubernetes
operational behavior, not just deploy a "hello world."

## What's in here

```
src/K8sLabApi/        ASP.NET Core 8 minimal API (health checks, /metrics, chaos endpoints)
k8s/base/              Raw K8s manifests (Deployment, Service, HPA, PDB, Ingress, ServiceMonitor)
helm/                  Values + script to install kube-prometheus-stack via Helm
.github/workflows/     CI/CD: build -> test -> image -> push to GHCR -> deploy
scripts/               kind cluster bootstrap (metrics-server + ingress-nginx)
```

## 1. Local cluster

You need `kind`, `kubectl`, `helm`, and `docker` installed locally (none of
that runs in this chat sandbox).

```bash
chmod +x scripts/setup-local-cluster.sh helm/install-monitoring.sh
./scripts/setup-local-cluster.sh
```

This gives you metrics-server (required for the HPA to work) and an
ingress-nginx controller on a kind cluster.

## 2. Build and load the image

For local iteration without a registry:

```bash
docker build -t k8s-lab-api:dev src/K8sLabApi
kind load docker-image k8s-lab-api:dev --name k8s-lab
sed -i.bak 's|ghcr.io/OWNER/k8s-lab-api:IMAGE_TAG_PLACEHOLDER|k8s-lab-api:dev|' k8s/base/deployment.yaml
```

## 3. Deploy the app

```bash
kubectl apply -k k8s/base
kubectl -n k8s-lab get pods -w
```

Add `127.0.0.1 k8s-lab.local` to `/etc/hosts`, then `curl http://k8s-lab.local:8080/`
(kind maps ingress to host port 8080 per the cluster config).

## 4. Install Grafana/Prometheus

```bash
./helm/install-monitoring.sh
kubectl -n monitoring port-forward svc/kube-prometheus-stack-grafana 3000:80
```

Open http://localhost:3000 (admin/admin). The `ServiceMonitor` in
`k8s/base/servicemonitor.yaml` gets your app's `/metrics` scraped
automatically - look for `k8s-lab-api` as a target in Prometheus, and import
community dashboard **ID 12900** (ASP.NET Core) or browse the built-in
Kubernetes dashboards for the cluster-level view (node/pod CPU & memory,
restarts, OOMKills).

## 5. CI/CD

`.github/workflows/ci-cd.yaml` builds, pushes to `ghcr.io/<you>/k8s-lab-api`,
then applies manifests to a cluster. It needs one repo secret:

- `KUBE_CONFIG` - your kubeconfig, base64-encoded (`cat ~/.kube/config | base64 -w0`)
  pointed at a cluster it can actually reach (a local kind cluster won't be
  reachable from GitHub's runners - use a real cluster, a self-hosted
  runner, or just skip the `deploy` job and apply locally).

## Production issues to go trigger on purpose

Every scenario below is something that actually happens in prod. Watch it
happen with `kubectl -n k8s-lab get pods -w`, `kubectl top pods`,
`kubectl describe pod <name>`, and the Grafana dashboards side by side.

| Scenario | How | What to look for |
|---|---|---|
| **OOMKilled** | `curl -X POST "http://k8s-lab.local:8080/api/chaos/memory?megabytes=40"` a few times (limit is 128Mi) | Pod restarts, `kubectl describe pod` shows `OOMKilled`, restart count climbs |
| **CrashLoopBackOff** | `curl -X POST http://k8s-lab.local:8080/api/chaos/crash` repeatedly | Backoff delay growing between restarts |
| **CPU throttling / HPA scale-out** | `curl -X POST "http://k8s-lab.local:8080/api/chaos/cpu?seconds=120"` against several pods, or hit it with `hey`/`k6` | `kubectl get hpa -w` shows replica count climbing; Grafana shows CPU throttling metrics |
| **Readiness failure without a restart** | `curl -X POST http://k8s-lab.local:8080/api/chaos/unready` | Pod stays `Running` but drops out of the Service's endpoints (`kubectl get endpoints k8s-lab-api -n k8s-lab`) |
| **Slow dependency / cascading timeouts** | `curl -X POST "http://k8s-lab.local:8080/api/chaos/slow?seconds=15"` while load-testing | Client-side timeouts, latency spike in Grafana |
| **Bad rollout** | Point `deployment.yaml`'s image at a tag that doesn't exist, `kubectl apply -k k8s/base` | `ImagePullBackOff`, rollout stuck (`kubectl rollout status` hangs), practice `kubectl rollout undo deployment/k8s-lab-api -n k8s-lab` |
| **Disruption budget in action** | Drain a node (`kubectl drain <node> --ignore-daemonsets`) with only 2 replicas up | PDB blocks eviction until a replacement pod is ready |
| **Config drift** | Edit `configmap.yaml`, `kubectl apply` it, then check `/` on a running pod | Env vars from a ConfigMap don't update in a running container until it restarts - a classic gotcha |

## Good next additions once this feels easy

- A `PodMonitor`/log shipper (e.g. Loki + Promtail) so logs and metrics sit
  next to each other in Grafana.
- `kube-bench` or `trivy` in CI for a security-scanning step.
- A second, "downstream" service so you can see how failures propagate
  across a call (this is what StocksApp already gives you at a bigger scale).
