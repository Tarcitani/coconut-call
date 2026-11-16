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

- **App WinUI 3** (`src/Coconut.App`, `src/Coconut.App.Core`): Windows App SDK 2.6.5 autocontido, não empacotado, compilado pelo `dotnet` CLI. Tela inicial (nome, criar, entrar por código ou `coconut://join/…`, aviso de IP, servidor em "Avançado"), chamada (participantes com indicador de fala, convite copiável, mudo, nível do microfone, dispositivos, sair, remover e encerrar para todos se administrador). 29 testes de ViewModel; [teste de fumaça de ponta a ponta](benchmarks/2026-11-16-UI-fumaca-local.md) com app real, servidor e bot.

## Validação pendente
- P1 entre redes distintas (NAT/CGNAT, IPv6) — depende de ambiente externo.
- Perfil SRTP efetivamente negociado após habilitar AES-GCM (não observado no log).
- Servidor com WSS e certificado real (hospedagem pública futura).
- Causa da queda rara de ICE antes do trickle (1 em ~4.200 enlaces, recuperada por renegociação) — ver P2.
- `Coconut.Client` sem testes unitários próprios (coberto só pelo P2); quedas de enlaces já verificados não tratadas.

- **Pronto para hospedagem** ([ADR 0005](adr/0005-hospedagem.md), [guia](deploy/vm-gratuita.md)): STUN próprio no servidor (RFC 5389, dual-stack, limite por origem; 16 testes; libdatachannel obtém `srflx` dele, controle negativo não obtém), cabeçalhos encaminhados só de proxy loopback (testado contra forja), app deriva `stun:<host>:3478` do servidor, pacotes autocontidos `linux-arm64`/`linux-x64` (binários ELF conferidos), systemd isolado, Caddy com TLS automático, nome via `sslip.io`.

- **Servidor no ar** numa VM Oracle *Always Free* (E2.1.Micro, São Paulo) do usuário — endereço fora do repositório público. Verificado pela internet: TLS Let's Encrypt, STUN (`srflx`), app real + bot de ponta a ponta pela sinalização na nuvem ([evidência](benchmarks/2026-11-16-implantacao-vm.md)).

## O que falta para duas pessoas conversarem
Tudo está pronto para a primeira chamada real. Falta:
1. **Teste com duas pessoas em redes diferentes:** cada uma roda o app, põe o servidor em **Avançado**, uma cria a sala e manda o link. Medir: conexão P2P (NAT/CGNAT), qualidade, eco, latência.
2. Distribuição do app para quem não compila: instalador (P7). Até lá, a pasta de build pode ser copiada.
3. Depois: tela (P4), bandeja/protocolo/instância única (P5).

## Decisões pendentes do usuário
- Nenhuma bloqueante. Decidido em 2026-11-16: modo de comunicação com AEC (ADR 0004); VM gratuita na nuvem (ADR 0005).
- A considerar: VMs *Always Free* ociosas podem ser recuperadas pela Oracle (ver riscos no guia); conta *Pay As You Go* ou domínio próprio reduzem o impacto, mas envolvem cartão/cadastro.
- Propostas futuras (sem urgência): rotação de convite pelo administrador e limite de tentativas de convite por origem.
