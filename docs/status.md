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

## Validação pendente
- P1 entre redes distintas (NAT/CGNAT, IPv6) — depende de ambiente externo.
- Perfil SRTP efetivamente negociado após habilitar AES-GCM (não observado no log).

## Próximo
1. Host de sinalização (ASP.NET Core, WebSocket/TLS, fila por sala, tokens de sessão, limites de taxa) sobre o núcleo.
2. Extrair os bindings do P1 para `src/Coconut.Media.Native` e fazer o P2 (malha local de N processos usando o protocolo de admissão real).
3. P5 (WinUI 3 não empacotado: bandeja, instância única, `coconut://`).

## Decisões pendentes do usuário
- Nenhuma. Rotação de convite (mitigação de convite vazado) amplia levemente o escopo — será proposta com o host.
