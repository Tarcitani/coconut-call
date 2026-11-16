# Estado atual

Atualizado: 2026-11-16

## Concluído com evidência
- Ambiente inspecionado; SDK .NET 10.0.404 e VS 2022 Build Tools (MSVC 14.44) instalados com autorização do usuário.
- Auditoria inicial com fontes ([auditoria](research/auditoria-inicial.md)); ADRs 0001–0003.
- Núcleo de sinalização (`src/Coconut.Signaling.Core`): admissão P2P, reconexão, administração, sucessão, expulsão, encerramento e troca de transmissor. **362/362 testes passando** (`dotnet test Coconut.slnx`, 2026-11-16), build sem avisos com analisadores `latest-recommended`:
  - 45 testes de comportamento;
  - 16 regressões da revisão independente (prazos aplicados em ordem cronológica a cada comando, sala encerrada quando a malha esvazia com pendentes, retomada do transmissor, motivo em `DropPeer`, identidade não reutilizável, difusões só para a malha);
  - 300 sequências aleatórias com semente fixa checando invariantes a cada passo (admin ∈ malha, ≤ 1 transmissor e na malha, época monotônica, nenhuma mensagem a não envolvidos, admissão só com enlaces verificados) + verificação de que os caminhos de interesse são exercitados.
- Revisão independente (agente revisor, 2026-11-16): sem defeitos graves; 10 achados médios/baixos, todos tratados (8 corrigidos com teste, 2 decididos e documentados no protocolo §6.7–8).

- vcpkg em `C:\Dev\tools\vcpkg` (commit `8f363bdc`, autorizado pelo usuário); nativos compilados do código-fonte: libdatachannel 0.24.6 `[srtp]`, libsrtp 2.8.1 `[openssl]`, opus 1.6.1.
- **P1 — sucesso local** (9/9 execuções): dois processos .NET, sem servidores ICE, DTLS-SRTP, canal de dados com eco, 150/150 quadros Opus decodificados com tom 440 Hz íntegro. [Evidência](benchmarks/2026-11-16-P1-webrtc-local.md). ADR 0002 aceito.

- Repositório público: https://github.com/Tarcitani/coconut-call (publicação autorizada pelo usuário).
- **Servidor de sinalização** (`src/Coconut.Signaling.Server`, ASP.NET Core): WebSocket `/ws`, um ator por sala sobre o núcleo, convites de 50 bits, tokens de retomada guardados como hash, limites de tamanho/taxa/handshake/salas por origem, TLS obrigatório fora do loopback. **64/64 testes de integração** (estáveis em 5 repetições), incluindo sanitização de logs. Processo real verificado em loopback (`/healthz` 200) e recusando HTTP em `0.0.0.0`.

## Validação pendente
- P1 entre redes distintas (NAT/CGNAT, IPv6) — depende de ambiente externo.
- Perfil SRTP efetivamente negociado após habilitar AES-GCM (não observado no log).
- Servidor com WSS e certificado real (hospedagem pública futura).

## O app ainda não é utilizável
Falta o caminho até a primeira chamada de voz real. Ordem planejada:
1. `Coconut.Media.Native` (bindings extraídos do P1) + cliente de sinalização + gerenciador da malha P2P (aplica `connectToPeer`/`relay`/`reportLink`); P2 com N processos locais.
2. Áudio real: captura e reprodução WASAPI (P3 decide AEC), Opus, jitter buffer, mudo efetivo na mídia.
3. App WinUI 3 mínimo: criar/entrar por código, lista de participantes, mudo, sair.
4. Depois: tela (P4), bandeja/protocolo/instância única (P5), instalador/atualizador (P7).

## Decisões pendentes do usuário
- Nenhuma. Propostas futuras: rotação de convite pelo administrador e limite de tentativas de convite por origem.
