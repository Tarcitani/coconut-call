# Implantar o servidor do Coconut numa VM gratuita

O servidor reúne **sinalização** (WebSocket atrás de TLS) e **STUN** (UDP 3478). A mídia continua ponto a ponto: nenhum áudio ou vídeo passa pela VM. Decisão e alternativas: [ADR 0005](../adr/0005-hospedagem.md).

Passos marcados **(você)** exigem a sua conta ou o painel do provedor; o Coconut não cria contas nem guarda credenciais.

## 1. Criar a VM (você)
Exemplo com o nível *Always Free* da Oracle Cloud (qualquer VM Ubuntu 22.04/24.04 com IP público serve):
- Imagem **Ubuntu 24.04** (Canonical).
- Formato **VM.Standard.A1.Flex** (ARM) com 1 OCPU e 6 GB — sobra para o servidor → use o pacote `linux-arm64`. Alternativa: **VM.Standard.E2.1.Micro** (AMD, 1 GB) → pacote `linux-x64`.
- Acesso **só por chave SSH** (gere com `ssh-keygen -t ed25519`); nunca senha.
- Anote o **IP público** da VM.

## 2. Liberar portas no provedor (você)
Na Oracle: *VCN → Security List → Ingress Rules*, origem `0.0.0.0/0`:
- TCP **80** e **443** (certificado e sinalização com TLS);
- UDP **3478** (STUN).
O `install.sh` também libera essas portas no firewall interno da VM (incluindo as regras `iptables` que as imagens Ubuntu da Oracle trazem).

## 3. Gerar o pacote (no seu PC)
```
powershell -NoProfile -ExecutionPolicy Bypass -File deploy\publish.ps1 -Runtime linux-arm64
```
Saída: `artifacts\deploy\coconut-signaling-linux-arm64.tar.gz` (~43 MB, autocontido — a VM não precisa de .NET).

## 4. Enviar e instalar
```
scp artifacts\deploy\coconut-signaling-linux-arm64.tar.gz ubuntu@IP_DA_VM:~
ssh ubuntu@IP_DA_VM
tar -xzf coconut-signaling-linux-arm64.tar.gz && cd coconut-signaling-linux-arm64
sudo bash install.sh 203-0-113-7.sslip.io
```
Troque `203-0-113-7` pelo IP público com hífens. O `sslip.io` resolve esse nome para o IP sem conta nem cadastro, e o Caddy obtém o certificado TLS automaticamente (Let's Encrypt). Com domínio próprio, use-o no lugar.

## 5. Verificar
```
curl https://203-0-113-7.sslip.io/healthz                       # deve responder "ok"
dotnet run --project prototypes/Tools.StunCheck -- stun:203-0-113-7.sslip.io:3478   # deve dizer "SRFLX OBTIDO"
```
No app: **Avançado → Servidor de sinalização** = `wss://203-0-113-7.sslip.io/ws`. O app usa automaticamente o STUN do mesmo host.

## 6. Atualizar
Repita os passos 3 e 4: o `install.sh` é idempotente (substitui os binários e reinicia o serviço).

## Segurança e privacidade
- O servidor escuta só em `127.0.0.1:5080`; o Caddy termina o TLS e encaminha. O servidor só confia em cabeçalhos encaminhados vindos de loopback.
- Sem log de acesso no Caddy (nível ERROR); o servidor registra apenas um identificador aleatório de sala. IPs ficam só em memória (limites por origem).
- STUN responde só a *Binding Request* válido, com limite por origem (evita uso como refletor).
- Serviço systemd sem privilégios, sem escrita em disco (`ProtectSystem=strict`).
- Recomendado: `sudo apt install unattended-upgrades` para atualizações de segurança automáticas.

## Limitações e riscos (verificados em 2026-11-16)
- **Gratuidade não é permanente nem garantida.** A Oracle reduziu o nível gratuito ARM para 1.500 horas de OCPU e 9.000 GB-hora por mês (equivalente a 2 OCPUs e 12 GB) — [Always Free Resources](https://docs.oracle.com/en-us/iaas/Content/FreeTier/resourceref.htm).
- **VM ociosa pode ser recuperada.** A mesma página: instâncias *Always Free* são consideradas ociosas se, por 7 dias, CPU (percentil 95), rede e memória (A1) ficarem abaixo de 20%, e podem ser recuperadas. Um servidor de sinalização pouco usado se encaixa nisso. A página não informa se contas *Pay As You Go* ficam isentas. Se a VM for recriada, o IP — e portanto o endereço `sslip.io` — muda, e cada pessoa precisa atualizar o servidor no app.
- **Dependência do `sslip.io`** para o nome (serviço gratuito de terceiros); um domínio próprio elimina essa dependência.
- **Não testado pelo autor desta documentação:** a instalação numa VM real. O pacote foi gerado e verificado (binários ELF ARM64/x86-64, scripts em LF, sintaxe do `install.sh`), mas a execução no Linux depende do primeiro uso.
