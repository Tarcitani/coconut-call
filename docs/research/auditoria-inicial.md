# Auditoria arquitetural inicial — 2026-11-16

Legenda: **[F]** fato verificado em fonte primária (link e data) · **[H]** hipótese a testar · **[P]** preferência técnica.

## 1. Ambiente de desenvolvimento (observado em 2026-11-16)

| Item | Valor |
|---|---|
| SO | Windows 11 Pro, build 26300, x64 |
| CPU / RAM / GPU | AMD Ryzen 7 5800X3D (8C/16T) · 15,9 GB · AMD Radeon RX 5700 XT (driver 32.0.21045.16010) |
| Monitores detectados | 1 (seleção multi-monitor não testável localmente com hardware real) |
| .NET | SDK 10.0.404 (instalado nesta sessão via winget), runtime 10.0.12 |
| C++ | VS 2022 Build Tools 17.14 (MSVC 14.44, CMake) — instalado nesta sessão |
| Windows SDK | 10.0.26100.0 |
| Windows App Runtime instalados | 1.6 (CBS), 1.7, 1.8, 2.x |
| Git / gh / winget | 2.55 / 2.103 / 1.29 |
| NuGet | Nenhuma fonte global configurada → `nuget.config` do repositório fixa nuget.org |

## 2. Pilha WebRTC (risco nº 1)

Critério: licença compatível com GPLv3; manutenção ativa; ICE/STUN sem TURN obrigatório; DTLS-SRTP nativo; **controle da codificação** (malha de 15 exige codificar a tela uma vez e distribuir).

| Opção | Licença | Situação | Avaliação |
|---|---|---|---|
| **SIPSorcery** (C# puro) | BSD-3 + restrições de campo de uso e geográficas declaradas superiores ao BSD **[F]** [LICENSE.md](https://github.com/sipsorcery-org/sipsorcery/blob/master/LICENSE.md); restrição introduzida em 2025-08-24 (histórico do arquivo via API do GitHub) | Ativa | **Rejeitada.** GPLv3 §10 proíbe restrições adicionais; a combinação não poderia ser distribuída sob GPLv3. Fork de versão anterior transferiria toda a manutenção de segurança ao projeto. |
| **libdatachannel** (C++ com C API) | MPL-2.0 desde 0.18 **[F]** [README](https://github.com/paullouisageneau/libdatachannel) — compatível com GPLv3 | v0.24.6 em 2026-09-26, sem binários publicados **[F]** (API de releases) | **Escolhida para prototipar.** ICE (libjuice), DTLS (OpenSSL/GnuTLS/MbedTLS), SRTP, packetizers Opus/H.264/VP8/AV1, NACK, PLI, SR, `rtcSetMediaInterceptorCallback` **[F]** [DOC.md](https://github.com/paullouisageneau/libdatachannel/blob/master/DOC.md). Não tem codecs, AEC, jitter buffer nem controle de congestionamento completo: ficam em C#/libs dedicadas. |
| libwebrtc (Google) + wrapper C | BSD-3 | Ativa | Possível, mas build pesado e **um encoder por PeerConnection** (14 codificações da tela) **[H]**. Reserva. |
| GStreamer `webrtcbin` | LGPL | Binários MSVC oficiais **[F]** ([1.26.x](https://discourse.gstreamer.org/t/gstreamer-1-26-9-stable-bug-fix-release/5541)); `d3d11screencapturesrc` com WGC exige build MSVC ≥1.22 **[F]** ([docs](https://gstreamer.freedesktop.org/documentation/d3d11/d3d11screencapturesrc.html)); `amfh264enc` **[F]** | Reserva se libdatachannel falhar. Distribuição pesada; bindings C# de maturidade incerta **[H]**. |
| MixedReality-WebRTC | MIT | Arquivado (libwebrtc antigo) | Rejeitada por segurança. |
| DataChannelDotnet.Wrap (NuGet) | MIT | Depreciado e não listado pelo autor **[F]** ([NuGet](https://www.nuget.org/packages/DataChannelDotnet.Wrap)) | Rejeitada. Bindings próprios via P/Invoke na C API. |

Decisão registrada em [ADR 0002](../adr/0002-pilha-webrtc.md).

**Correção pós-P1 (2026-11-16):** a `DOC.md` citada acima é do branch principal. A v0.24.6 compilada **não** tem `rtcSetFrameCallback` nem depacketizers ([rtc.h da tag](https://github.com/paullouisageneau/libdatachannel/blob/v0.24.6/include/rtc/rtc.h)); o callback da trilha entrega RTP decifrado. Detalhes em [P1](../benchmarks/2026-11-16-P1-webrtc-local.md).

## 3. Mídia

- **Opus:** libopus nativa (BSD-3, implementação de referência) via vcpkg **[P]**; Concentus (C# puro) como alternativa se interop for problema.
- **Eco:** o requisito pede áudio natural e processamento mínimo. Hipótese **[H]**: capturar com WASAPI na categoria *Communications* habilita o AEC do driver/sistema sem acrescentar outros filtros; validar no P3 se o Windows 11 também aplica supressão de ruído ("Voice Clarity") nessa categoria — isso conflitaria com "sem filtros artificiais obrigatórios". Alternativa: AEC3 do WebRTC APM (BSD) só com cancelamento de eco.
- **Tela:** Windows Graphics Capture (D3D11) → encoder H.264 por Media Foundation (MFT de hardware AMD/NVIDIA/Intel) → packetizer H.264 da libdatachannel **[H]**. Decodificação MF por hardware e renderização em `SwapChainPanel` no WinUI. A licença de patentes H.264 é coberta pelo codec do Windows **[H — confirmar]**.
- **Mudo efetivo:** o caminho de envio deixa de entregar quadros do microfone ao encoder (envia silêncio/DTX), validado por inspeção do RTP recebido, não pela UI.

## 4. Capacidade da malha (cálculo, não medição)

Uplink de cada participante = 14 × fluxo. Valores **[H]** a medir no P6:

| Fluxo | Por par | × 14 pares |
|---|---|---|
| Voz Opus ~32 kbit/s + overhead RTP/SRTP/UDP/IP a 20 ms | ~50 kbit/s | ~0,7 Mbit/s |
| Tela 720p 15 fps | ~1 Mbit/s | ~14 Mbit/s |
| Tela 1080p 30 fps | ~2,5 Mbit/s | ~35 Mbit/s |

Consequências: a tela deve ser **codificada uma vez** e o mesmo quadro empacotado para cada par; a taxa é ajustada pelo par mais fraco (ou em poucos degraus); PLIs de vários pares são agregados com limite de taxa para não gerar quadros-chave em excesso. Com 15 participantes, a viabilidade depende do uplink do transmissor — o produto deve medir e avisar, não prometer.

## 5. Conectividade sem TURN

Sem retransmissão, pares atrás de NAT simétrico/CGNAT em ambos os lados podem não se conectar **[H]**, situação comum em provedores residenciais e móveis. Mitigações compatíveis: ICE com IPv6 (candidatos host/srflx dual-stack), vários servidores STUN, mensagens de falha específicas por par. O protocolo de admissão já rejeita a entrada sem afetar a chamada. Taxa real de falha só pode ser medida pela internet com redes distintas (pendente).

## 6. Plataforma Windows

- **Windows App SDK:** estável atual 2.6.5 (2026-11-02); linha 2.0 em suporte até 2027-04-29; 1.8 fora de suporte desde 2026-09-24 **[F]** ([release channels](https://learn.microsoft.com/windows/apps/windows-app-sdk/release-channels)). Usar 2.x. Instanciação e ativação rica (protocolo) estão disponíveis no canal estável **[F]**.
- **Instância única e protocolo:** `AppInstance.FindOrRegisterForKey` + `RedirectActivationToAsync` **[F, mesma página]**; registro de `coconut://` em HKCU pelo instalador (app não empacotado) **[H]**.
- **Bandeja:** WinUI 3 não tem ícone de bandeja nativo. H.NotifyIcon.WinUI (MIT, com suporte a não empacotado e menu com tema claro/escuro) **[F]** ([repo](https://github.com/HavenDV/H.NotifyIcon)), ou `Shell_NotifyIcon` via CsWin32 se a dependência não se justificar **[P]**.
- **.NET:** 10 é LTS **[conhecimento prévio — confirmar a data de fim de suporte na política oficial]**.

## 7. Instalador e atualização (orçamento zero)

- **Velopack** (MIT **[F]** [LICENSE](https://github.com/velopack/velopack/blob/develop/LICENSE)): instalador + atualização a partir de GitHub Releases ou arquivos estáticos **[F]** ([docs](https://docs.velopack.io/)). Ainda não verificado: compatibilidade com WinUI 3 não empacotado, como a integridade é verificada, se há verificação de assinatura além de Authenticode **[H]**.
- **Autenticidade sem certificado pago:** proposta **[P]** — manifesto de versão assinado com Ed25519 (chave privada fora do repositório; pública embutida no app); o atualizador rejeita pacotes cujo hash não confere com o manifesto assinado. Isso garante autenticidade *das atualizações*, mas **não** dá reputação Authenticode: o instalador inicial continuará disparando o SmartScreen. Limitação a documentar para o usuário.
- MSIX/App Installer exige certificado confiável para instalação sem atrito **[H]** — exige despesa ou programa de assinatura gratuito para OSS (avaliar; decisão do usuário se envolver cadastro).

## 8. Sinalização

ASP.NET Core (Kestrel) com WebSocket sobre TLS **[P]**; sem banco de dados (estado em memória por sala; salas são temporárias). Núcleo determinístico já implementado ([protocolo](../protocol/salas-e-admissao.md), [ADR 0003](../adr/0003-nucleo-de-sinalizacao.md)).

## 9. Riscos priorizados

| # | Risco | Impacto | Protótipo |
|---|---|---|---|
| R1 | Pilha WebRTC compatível com GPLv3 e compilável de forma reprodutível | Bloqueia mídia | P1 |
| R2 | Uplink da malha com tela em 15 participantes | Viabilidade do limite | P6 |
| R3 | AEC sem processamento adicional indesejado | Qualidade de voz | P3 |
| R4 | WGC + encoder/decoder de hardware + renderização WinUI | CPU/GPU e latência | P4 |
| R5 | NAT/CGNAT sem TURN | Taxa de falha de entrada | Teste pela internet (pendente de ambiente) |
| R6 | Confiança do instalador/atualizador sem certificado pago | Segurança e adoção | P7 |
| R7 | WinUI 3 não empacotado: bandeja, instância única, protocolo | Integração Windows | P5 |
