#!/usr/bin/env bash
set -euo pipefail

# Installs Prometheus + Grafana + Alertmanager + kube-state-metrics +
# node-exporter via the community kube-prometheus-stack chart.

helm repo add prometheus-community https://prometheus-community.github.io/helm-charts
helm repo update

kubectl create namespace monitoring --dry-run=client -o yaml | kubectl apply -f -

helm upgrade --install kube-prometheus-stack prometheus-community/kube-prometheus-stack \
  --namespace monitoring \
  --values "$(dirname "$0")/monitoring-values.yaml" \
  --wait

echo
echo "Grafana:    kubectl -n monitoring port-forward svc/kube-prometheus-stack-grafana 3000:80"
echo "            then open http://localhost:3000  (admin / admin)"
echo "Prometheus: kubectl -n monitoring port-forward svc/kube-prometheus-stack-prometheus 9090:9090"
echo "Alertmgr:   kubectl -n monitoring port-forward svc/kube-prometheus-stack-alertmanager 9093:9093"
