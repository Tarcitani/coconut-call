# Implantação real na VM gratuita (Oracle Cloud, São Paulo)

**Classe: INTERNET** para sinalização e STUN; **LOCAL** para a mídia (os dois participantes do teste estavam na mesma máquina). Endereço do servidor omitido de propósito (repositório público).

## Ambiente
- VM **VM.Standard.E2.1.Micro** (AMD, Always Free), Ubuntu 24.04.5 LTS x86_64, 952 MB de RAM, região Brazil East (São Paulo).
- Pacote `coconut-signaling-linux-x64` (45,6 MB) instalado com `install.sh`; Caddy do repositório oficial; nome via `sslip.io`.

## Problemas encontrados e corrigidos
1. **VM.Standard.A1.Flex (ARM) sem capacidade** em São Paulo ("Capacidade insuficiente… AD-1", região com um único domínio) → usado o E2.1.Micro, também gratuito.
2. **Espelho Ubuntu regional da Oracle fora do ar** ("no longer has a Release file") e VM sem rota IPv6 (APT travava tentando IPv6) → `install.sh` força IPv4 no APT quando não há rota IPv6 e troca o espelho de nuvem por `archive.ubuntu.com` se ele falhar.
3. **`tar` do Git Bash** (GNU/MSYS) trata `C:\…` como host remoto → `publish.ps1` usa o `tar.exe` do Windows.
4. **Roteiro de fumaça:** variáveis do PowerShell ignoram maiúsculas (`$Server` = `$server`), o que fez uma execução rodar localmente sem avisar → renomeado, e o `finally` agora encerra app, bot e servidor mesmo em falha.

## Verificado (2026-11-16)
| Verificação | Resultado |
|---|---|
| `https://<host>/healthz` a partir de outra rede | `ok`, HTTP 200, verificação TLS bem-sucedida |
| Certificado | Let's Encrypt (YE2), `CN=<ip-com-hífens>.sslip.io`, válido por 90 dias |
| `Tools.StunCheck stun:<host>:3478` | candidato `srflx/IPv4` obtido do STUN da VM |
| App real + bot pela sinalização na nuvem (`ui-smoke.ps1 -Server wss://<host>/ws`) | sala criada, bot admitido (enlace P2P verificado em 71 ms), removido pela UI, saída ao início |
| Serviços | `coconut-signaling` e `caddy` ativos; servidor com ~89 MB de RSS |
| Logs | servidor: só identificador aleatório de sala (único IP é o próprio `127.0.0.1:5080` na partida); Caddy: só linhas da API administrativa local na partida |

## Pendente
Chamada entre duas pessoas em **redes diferentes**: taxa de sucesso P2P (NAT/CGNAT), qualidade, eco e latência.
