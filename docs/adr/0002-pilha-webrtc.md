# ADR 0002 — Pilha WebRTC: libdatachannel via P/Invoke

Data: 2026-11-16 · Status: **aceito** — critério do P1 cumprido localmente ([evidência](../benchmarks/2026-11-16-P1-webrtc-local.md)); validação entre redes distintas pendente.

## Contexto
Requisitos: GPLv3, mídia P2P com DTLS-SRTP nativo, sem TURN/SFU, malha de até 15, arquitetura principal em C#. Evidências em [auditoria §2](../research/auditoria-inicial.md#2-pilha-webrtc-risco-nº-1).

## Decisão
- Transporte WebRTC (ICE/STUN, DTLS-SRTP, SCTP, RTP/RTCP, packetizers) pela **libdatachannel** (MPL-2.0), compilada a partir do código-fonte com vcpkg em modo manifesto (`native/vcpkg.json`) e chamada por P/Invoke na C API.
- Codecs, captura, AEC, jitter buffer e adaptação de taxa em C# (com libopus e Media Foundation).
- A tela é codificada **uma vez** e o quadro é entregue a cada trilha de par.

## Alternativas rejeitadas
- **SIPSorcery:** restrições adicionais na licença, incompatíveis com GPLv3 §10.
- **libwebrtc:** build pesado e encoder por PeerConnection; mantido como reserva.
- **GStreamer webrtcbin:** reserva se P1 falhar; distribuição pesada.

## Consequências
- Exige toolchain C++ (VS Build Tools) para desenvolvimento e CI para builds reprodutíveis.
- O projeto implementa a adaptação de taxa a partir de RTCP (perda/RTT) — mais trabalho, mas controle total do uplink na malha.
- MPL-2.0: modificações em arquivos da libdatachannel devem ser publicadas sob MPL; uso como biblioteca dinâmica não impõe obrigações ao código GPLv3.

## Critério para aceitar
P1 demonstra dois processos .NET locais estabelecendo DTLS-SRTP via libdatachannel, com canal de dados ecoando e uma trilha Opus recebida, sem servidor TURN configurado.
