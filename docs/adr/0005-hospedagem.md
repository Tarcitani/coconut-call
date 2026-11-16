# ADR 0005 — Hospedagem: VM gratuita com Caddy (TLS) e STUN próprio

Data: 2026-11-16 · Status: aceito (escolha do usuário); implantação real pendente

## Contexto
Para duas pessoas em redes diferentes conversarem, a sinalização precisa ser alcançável com TLS (o servidor recusa texto claro fora do loopback) e os clientes atrás de NAT precisam de STUN. Orçamento zero; mídia continua exclusivamente P2P.

## Decisão
- **VM gratuita na nuvem** (escolha do usuário em 2026-11-16), exemplo documentado com Oracle Cloud Always Free; qualquer Ubuntu com IP público serve.
- **STUN próprio** no processo do servidor (RFC 5389, só Binding, UDP 3478, dual-stack, limite por origem, sem log de endereços). Nenhum terceiro vê os IPs dos participantes. Interoperabilidade com a libdatachannel verificada (candidato `srflx` obtido; controle negativo sem STUN não obtém).
- **Caddy** (Apache-2.0) como proxy reverso com TLS automático (Let's Encrypt); servidor só em loopback; `ForwardedHeaders` confiando apenas em proxies de loopback.
- **Nome sem conta via `sslip.io`** (`<ip-com-hífens>.sslip.io`), substituível por domínio próprio.
- **Pacote autocontido** `linux-x64`/`linux-arm64`, systemd com isolamento, instalação idempotente ([guia](../deploy/vm-gratuita.md)).

## Alternativas rejeitadas
- **Túnel a partir do PC do usuário:** TLS sem abrir portas, mas UDP (STUN) não passa; exigiria STUN de terceiros vendo os IPs.
- **STUN público de terceiros:** expõe os endereços dos participantes a outra empresa.
- **TURN:** proibido pelo contrato (mídia só P2P).

## Riscos
Gratuidade pode mudar (a Oracle já reduziu limites em 2026); VM ociosa pode ser recuperada; recriar a VM muda o IP e o endereço `sslip.io`. Detalhes e fonte no guia.
