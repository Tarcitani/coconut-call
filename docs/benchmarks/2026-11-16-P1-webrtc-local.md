# P1 — libdatachannel via P/Invoke, P2P DTLS-SRTP sem TURN (local)

**Classe do resultado: LOCAL** (dois processos na mesma máquina). Não comprova funcionamento entre redes distintas, NAT/CGNAT nem com 15 participantes.

## Ambiente
- Windows 11 Pro build 26300 · Ryzen 7 5800X3D · 16 GB · .NET 10.0.12 (SDK 10.0.404)
- Nativos via vcpkg (`native/vcpkg.json`, baseline `8f363bdc11359ede4bc51e5eb32a2617538049c8`, MSVC 14.44, triplet `x64-windows`): libdatachannel 0.24.6 `[core,srtp]`, libsrtp 2.8.1 `[core,openssl]`, opus 1.6.1, OpenSSL 3.
- `rtc.h` instalado com SHA-256 `ad1e7ed7…a520d`, idêntico ao da tag v0.24.6 usado para escrever as estruturas do P/Invoke.

## Hipótese e critérios (definidos antes da execução)
Hipótese: libdatachannel compilada a partir do código-fonte é chamável por P/Invoke do .NET 10 e estabelece mídia P2P protegida por DTLS-SRTP **sem nenhum servidor ICE**.

| Critério | Resultado (execução 20261116-192535) |
|---|---|
| C1 conectado nos dois processos | ✅ conexão em 83,5 ms |
| C2 sem candidatos relay | ✅ só `host` (3 por lado); par selecionado host↔host |
| C3 DTLS-SRTP | ✅ `m=audio … UDP/TLS/RTP/SAVPF 111`, `a=fingerprint:sha-256`, `setup actpass/active` |
| C4 canal de dados 20/20 ecos | ✅ RTT mediana 0,20 ms, máx. 0,99 ms |
| C5 Opus ≥ 140/150 decodificados | ✅ 150 enviados, 150 RTP recebidos, 150 decodificados, 0 lacunas |
| C6 timestamps RTP (passo 960) | ✅ 100% |
| C7 tom 440 Hz íntegro | ✅ razão 440 Hz/1 kHz ≈ 4,5·10¹⁰; RMS recebido 5664 vs. 5657 esperado (8000/√2) |

## Procedimento
```
vcpkg install --x-manifest-root=native --x-install-root=native/vcpkg_installed --triplet x64-windows
dotnet run --project prototypes/P1.WebRtcLoopback
```
O coordenador inicia ofertante e respondente como processos separados; SDP completo (sem trickle) trocado por arquivos; `rtcConfiguration.iceServersCount = 0`. O ofertante codifica 3 s de senoide 440 Hz em Opus (20 ms) e envia pela trilha; o respondente interpreta o RTP decifrado, decodifica com libopus e mede frequência e amplitude. Saída: `prototypes/P1.WebRtcLoopback/runs/<carimbo>/summary.json` (não versionado; logs locais podem conter endereços).

## Repetições
- 6/6 execuções com sucesso antes do ajuste de SRTP; 3/3 depois.

## Achados
1. **Perfil SRTP:** com libsrtp sem OpenSSL, a libdatachannel avisou *"AES-GCM for SRTP is not supported, falling back to default profile"* (AES-CM-128/HMAC-SHA1-80, perfil obrigatório e ainda DTLS-SRTP padrão). Com `libsrtp[openssl]` o aviso desapareceu. **Não observado diretamente:** qual perfil foi negociado depois do ajuste (o log em nível Warning não informa).
2. **API da versão compilada ≠ documentação do branch principal:** v0.24.6 não tem `rtcSetFrameCallback` nem depacketizers; o callback da trilha entrega RTP decifrado. O Coconut fará a leitura de RTP e o jitter buffer em C#.
3. **Feature `srtp` não é padrão** no port vcpkg; sem ela não há transporte de mídia. O manifesto a declara explicitamente.

## Limitações
Loopback local (RTT ~0,2 ms, sem perda): nada sobre jitter, perda, NAT, IPv6, CPU com 14 pares ou reconexão. Próximos: P2 (malha local N processos com o protocolo de admissão) e testes entre redes distintas.
