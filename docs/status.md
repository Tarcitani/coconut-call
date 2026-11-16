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

- **Cliente e malha P2P** (`src/Coconut.Media.Native`, `src/Coconut.Client`): interop de produção (`RtcPeer` com registro de IDs seguro contra callbacks tardios; TURN recusado por construção), Opus, RTP, cliente de sinalização e `MeshSession` (segue o servidor, trickle ICE, verificação por eco, renegociação de enlaces que caem antes de verificados, áudio codificado uma vez e enviado a todos). **P2 — sucesso local**: N=2…15; com 15 processos entrando juntos, 40/40 execuções com 105 enlaces e áudio íntegro entre todos. [Evidência](benchmarks/2026-11-16-P2-malha-local.md).

- **Áudio** (`src/Coconut.Audio`, `src/Coconut.Client/Voice`, [ADR 0004](adr/0004-audio-wasapi.md)): interop WASAPI própria (48 kHz mono, conversão pelo Windows), catálogo de dispositivos com notificações, categoria Communications com AEC e referência apontada à saída, **supressão de ruído por IA desligada** (confirmado pelo Windows), jitter buffer com ocultação de perda, mixer, motor de voz com mudo antes do codificador, troca de dispositivo e recuperação. 23 testes (mudo provado por mutação) e sonda em hardware real ([P3](benchmarks/2026-11-16-P3-audio-local.md)).

## Validação pendente
- P1 entre redes distintas (NAT/CGNAT, IPv6) — depende de ambiente externo.
- Perfil SRTP efetivamente negociado após habilitar AES-GCM (não observado no log).
- Servidor com WSS e certificado real (hospedagem pública futura).
- Causa da queda rara de ICE antes do trickle (1 em ~4.200 enlaces, recuperada por renegociação) — ver P2.
- `Coconut.Client` sem testes unitários próprios (coberto só pelo P2); quedas de enlaces já verificados não tratadas.

## O app ainda não é utilizável
Falta o caminho até a primeira chamada de voz real. Ordem planejada:
1. ~~Interop + cliente de sinalização + malha P2P; P2~~ — concluído localmente.
2. ~~Áudio real~~ — motor pronto e verificado em hardware; **falta uma chamada entre pessoas** para qualidade, eco e latência.
3. **Próximo:** app WinUI 3 mínimo (criar/entrar por código, lista de participantes, mudo, escolha de dispositivos, sair), usando `MeshSession` + `VoiceEngine`.
4. Depois: tela (P4), bandeja/protocolo/instância única (P5), instalador/atualizador (P7).

## Decisões pendentes do usuário
- **Processamento de voz do Windows:** no modo de comunicação (necessário para o cancelamento de eco), o Windows impõe supressão de ruído simples e controle automático de ganho, sem permitir desligá-los. Opções: (a) manter, recomendado para quem usa caixa de som; (b) oferecer uma opção "áudio sem processamento" que troca para o modo padrão — sem AEC e sem AGC (a supressão simples do microfone testado continua). A supressão por IA já está desligada em ambos.
- Propostas futuras (sem urgência): rotação de convite pelo administrador e limite de tentativas de convite por origem.
