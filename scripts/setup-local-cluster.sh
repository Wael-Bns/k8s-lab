#!/usr/bin/env bash
set -euo pipefail

# Spins up a local cluster with the pieces the manifests assume:
# metrics-server (for HPA) and an ingress controller.
# Requires: kind, kubectl, helm.

CLUSTER_NAME="k8s-lab"

cat <<'EOF' > /tmp/kind-config.yaml
kind: Cluster
apiVersion: kind.x-k8s.io/v1alpha4
nodes:
  - role: control-plane
    extraPortMappings:
      - containerPort: 80
        hostPort: 8080
        protocol: TCP
      - containerPort: 443
        hostPort: 8443
        protocol: TCP
EOF

kind create cluster --name "$CLUSTER_NAME" --config /tmp/kind-config.yaml

# metrics-server: kind's CNI needs --kubelet-insecure-tls to work locally.
kubectl apply -f https://github.com/kubernetes-sigs/metrics-server/releases/latest/download/components.yaml
kubectl patch deployment metrics-server -n kube-system --type=json \
  -p='[{"op":"add","path":"/spec/template/spec/containers/0/args/-","value":"--kubelet-insecure-tls"}]'

# ingress-nginx, kind-flavoured manifest.
kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/main/deploy/static/provider/kind/deploy.yaml
kubectl wait --namespace ingress-nginx \
  --for=condition=ready pod \
  --selector=app.kubernetes.io/component=controller \
  --timeout=120s

echo "Cluster '$CLUSTER_NAME' is ready."
echo "Next: build/push the API image, then: kubectl apply -k k8s/base"
echo "Then: helm/install-monitoring.sh"
