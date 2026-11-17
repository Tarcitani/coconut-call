# Checklist de requisitos

Estados: `pendente` · `projetado` (especificado, sem código) · `implementado` (código, sem validação no ambiente pertinente) · `testado-unidade` · `validado-local` · `validado-internet`. Requisito só é **concluído** quando validado no ambiente pertinente.

Fonte dos requisitos: prompt mestre do produto (contrato §1–§7). Evidências apontam para testes ou registros.

| ID | Requisito | Estado | Evidência / observação |
|---|---|---|---|
| PLT-1 | C#/.NET LTS, Windows 11, WinUI 3 + Windows App SDK | validado-local | .NET 10, WinAppSDK 2.6.5; app autocontido real no Windows 11 |
| PLT-2 | GPLv3 no código próprio; licenças de dependências verificadas | implementado | `LICENSE`; SIPSorcery rejeitada por licença ([ADR 0002](../adr/0002-pilha-webrtc.md)) |
| NET-1 | Mídia somente P2P; sem SFU/TURN/relay | validado-local | [P1](../benchmarks/2026-11-16-P1-webrtc-local.md): sem servidores ICE, par host↔host, DTLS-SRTP; entre redes distintas pendente; STUN próprio (sem terceiros) testado e interoperando com a libdatachannel |
| NET-2 | Sinalização com transporte seguro e autenticação de sessão | validado-internet | 84 testes do servidor + implantação real com Caddy/Let's Encrypt; app real conectado pela internet ([implantação](../benchmarks/2026-11-16-implantacao-vm.md)) |
| NET-3 | Até 15 participantes em malha completa | validado-local | [P2](../benchmarks/2026-11-16-P2-malha-local.md): 15 processos, 105 enlaces, áudio entre todos, 40/40; redes distintas e uplink com tela pendentes |
| NET-4 | Falhas de conexão compreensíveis; condições de rede documentadas | validado-local (parcial) | Indicadores, admissão com pares afetados, retomada WebSocket e renegociação; README/LEIA-ME com limites de rede. NAT externo pendente |
| ADM-1 | Estado provisório, confirmação coordenada, timeout e rollback | testado-unidade | `AdmissionTests` (bilateral, timeout, falha, rollback preserva chamada) |
| ADM-2 | Entradas simultâneas, saídas e reconexões durante a negociação | testado-unidade; entradas simultâneas validadas localmente (P2) | `Concurrent_joiners_*`, `Active_participant_leaving_*`, `Reconnecting_participant_is_still_required_*`, `ReviewRegressionTests`, `RoomInvariantTests` (300 sequências) |
| ADM-3 | Sinalização × P2P × mídia operacional diferenciados | testado-unidade; validado-local | MeshSession, CallBackend, indicador só após áudio realmente decodificado; testes de voz e ensaio com dois apps |
| ROOM-1 | Salas e nomes temporários, sem contas | testado-unidade | Identidade = `ParticipantId` efêmero |
| ROOM-2 | Encerramento automático da sala vazia com tolerância de reconexão | testado-unidade | `Empty_room_closes_only_after_reconnect_grace` |
| ROOM-3 | Criador admin; expulsar; encerrar para todos | validado-local (expulsar) | `MembershipTests`; app real: administrador removeu participante pela UI ([fumaça](../benchmarks/2026-11-16-UI-fumaca-local.md)); encerrar para todos pela UI não exercitado |
| ROOM-4 | Sucessão automática por antiguidade | testado-unidade | `Admin_succession_*`, `Seniority_survives_reconnection` |
| ROOM-5 | Saída voluntária × desconexão temporária | testado-unidade | `Voluntary_leave_*`, `Reconnect_grace_expiry_*` |
| ROOM-6 | Entrada por código e `coconut://join/CODIGO`; instância existente recebe convite | testado-unidade; validado-local | DesktopMvpTests (parser estrito e pipe real); segunda invocação encaminha convite sem autoentrada ou trocar chamada |
| AUD-1 | Microfone aberto com mudo efetivo validado na mídia | testado-unidade; validado-local (mudo) | `VoicePipelineTests`: mudo verificado no áudio decodificado, provado por mutação; P3: motor real com mudo = 0 pacotes. Chamada entre pessoas pendente |
| AUD-2 | Opus, eco tratado, processamento mínimo | validado-local (parcial) | [ADR 0004](../adr/0004-audio-wasapi.md), [P3](../benchmarks/2026-11-16-P3-audio-local.md): AEC do Windows ativo, IA desligada; NS simples e AGC impostos pelo modo (decisão pendente); eco audível não avaliado |
| AUD-3 | Dispositivos padrão, seleção manual, troca a quente, recuperação | implementado; validado-local (parcial) | Catálogo/motor, troca G435 observada; AudioEffectPolicy reaplicada após reabrir/periodicamente, 25 testes áudio. Desconexão física e eco humano pendentes |
| SCR-1 | Monitor inteiro, seleção multi-monitor | validado-local | Captura real em dois monitores, incluindo retrato; hardware/software → RTP → GPU; [MVP](../benchmarks/2026-11-17-mvp.md) |
| SCR-2 | Resolução/FPS/bitrate configuráveis e adaptativos | testado-unidade; validado-local (parcial) | UI 480/720/1080, 15/30/60, 256–12000 kbps; controlador RTCP testado; hardware/software local. Matriz completa e rede congestionada externa pendentes |
| SCR-3 | Um transmissor por sala; troca solicitar/aceitar/recusar/expirar/cancelar | testado-unidade; validado-local (parcial) | 15 testes do núcleo, testes cliente de sessão/revogação e VM; fluxo real de pedido, aceite, handover, parada e expiração; [ADR 0006](../adr/0006-tela-e-integracao-windows.md) |
| SCR-4 | Voz preservada durante compartilhamento | validado-local (parcial) | 15 participantes locais com vídeo e Opus simultâneo; adaptação para liberar conexão da voz. Qualidade audível entre pessoas em rede congestionada pendente |
| WIN-1 | Fechar → bandeja; bandeja abre, silencia, encerra | validado-local (parcial) | Fechar ocultou mantendo chamada e reabertura retomou vídeo; menu implementa abrir/mudo/encerrar. [MVP](../benchmarks/2026-11-17-mvp.md) |
| WIN-2 | Inicialização automática opcional (desligada), na bandeja, instância única | testado-unidade; validado-local (parcial) | Named pipe CurrentUserOnly, mutex e ativação; Run HKCU opt-in; --startup oculto, reabertura com mesmo PID e saída ociosa observados; testes broker real e parser |
| DST-1 | Instalador e atualização automática verificada | testado-unidade; validado-local (instalador e aplicação verificada) | 0.2 → 0.3 real, 495 hashes e preferências preservadas; Ed25519/tamanho/hash e rehash antes de executar; 12 testes (adulteração, truncamento, chamada ativa, redirects); download/check automáticos, instalar por clique ocioso. Sem Authenticode; Windows limpo pendente |
| SEC-1 | Análise de ameaças | projetado | [Modelo de ameaças](../security/modelo-de-ameacas.md) (inicial) |
| SEC-2 | Logs locais com retenção, exportação sanitizada e testes de sanitização | testado-unidade; validado-local (logs) | FileDiagnosticLog 7 dias/5 MB por dia, LogSanitizer, exportação ZIP novamente sanitizada exclui settings/chaves/mídia; DesktopMvpTests e testes servidor |
| PERF-1 | Benchmarks reprodutíveis | validado-local (parcial) | P1/P2/P3 e P4.ScreenProbe; N=15, hardware/software, entrada tardia e retomada. Matriz completa/latências/2h pendentes |
| UI-1 | UI modular (MVVM/composição), Fluent, temas, acessibilidade | implementado; validado-local (parcial) | 63 testes App.Core, views separadas, x:Bind, Mica, nomes de automação; dois apps reais com voz/tela. Tema claro/leitor de tela pendentes |
