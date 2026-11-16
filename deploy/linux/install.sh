#!/usr/bin/env bash
# Instala ou atualiza o servidor do Coconut (sinalização + STUN) numa VM Ubuntu 22.04/24.04.
# Uso (na pasta extraída do pacote):  sudo ./install.sh <dominio>
#   <dominio> exemplo sem conta: 203-0-113-7.sslip.io  (o IP público da VM com hífens)
# Idempotente: rodar de novo atualiza os binários e reinicia o serviço.
set -euo pipefail

DOMAIN="${1:-}"
if [[ -z "$DOMAIN" || ! "$DOMAIN" =~ ^[A-Za-z0-9.-]+$ ]]; then
  echo "Uso: sudo $0 <dominio>   (ex.: 203-0-113-7.sslip.io)" >&2
  exit 2
fi
if [[ $EUID -ne 0 ]]; then
  echo "Execute com sudo." >&2
  exit 2
fi

HERE="$(cd "$(dirname "$0")" && pwd)"
APP_DIR=/opt/coconut-signaling

echo "==> Pacotes básicos"
apt-get update -y
apt-get install -y curl debian-keyring debian-archive-keyring apt-transport-https gpg

echo "==> Caddy (repositório oficial)"
if ! command -v caddy >/dev/null 2>&1; then
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | gpg --dearmor --yes -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' > /etc/apt/sources.list.d/caddy-stable.list
  apt-get update -y
  apt-get install -y caddy
fi

echo "==> Usuário de serviço"
id coconut >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin coconut

echo "==> Binários"
systemctl stop coconut-signaling 2>/dev/null || true
mkdir -p "$APP_DIR"
cp -a "$HERE/server/." "$APP_DIR/"
chown -R root:root "$APP_DIR"
chmod 0755 "$APP_DIR/Coconut.Signaling.Server"

echo "==> Serviço systemd"
install -m 0644 "$HERE/coconut-signaling.service" /etc/systemd/system/coconut-signaling.service
systemctl daemon-reload
systemctl enable --now coconut-signaling

echo "==> Caddy (TLS automático para $DOMAIN)"
sed "s/__DOMAIN__/$DOMAIN/" "$HERE/Caddyfile" > /etc/caddy/Caddyfile
caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
systemctl enable caddy
systemctl reload caddy || systemctl restart caddy

echo "==> Firewall da VM (o do provedor é configurado no painel dele)"
if command -v ufw >/dev/null 2>&1 && ufw status | grep -q "Status: active"; then
  ufw allow 22/tcp
  ufw allow 80/tcp
  ufw allow 443/tcp
  ufw allow 3478/udp
elif iptables -S INPUT 2>/dev/null | grep -q -- "-j REJECT"; then
  # Imagens Ubuntu da Oracle Cloud trazem iptables com REJECT no fim da cadeia INPUT.
  for rule in "-p tcp --dport 80" "-p tcp --dport 443" "-p udp --dport 3478"; do
    # shellcheck disable=SC2086
    iptables -C INPUT $rule -m state --state NEW -j ACCEPT 2>/dev/null || iptables -I INPUT 5 $rule -m state --state NEW -j ACCEPT
  done
  if command -v netfilter-persistent >/dev/null 2>&1; then
    netfilter-persistent save
  fi
fi

echo "==> Verificação"
for i in $(seq 1 20); do
  if curl -fsS http://127.0.0.1:5080/healthz >/dev/null 2>&1; then break; fi
  sleep 1
done
curl -fsS http://127.0.0.1:5080/healthz >/dev/null && echo "sinalização local: ok" || { echo "sinalização local: FALHOU"; journalctl -u coconut-signaling -n 30 --no-pager; exit 1; }
ss -ulpn | grep -q ':3478 ' && echo "STUN UDP 3478: escutando" || echo "STUN UDP 3478: NÃO está escutando"

echo
echo "Pronto. No app: Avançado → Servidor de sinalização = wss://$DOMAIN/ws"
echo "O certificado TLS pode levar até 1 minuto na primeira vez. Teste: curl https://$DOMAIN/healthz"
