# Checklist de requisitos

Estados: `pendente` · `projetado` (especificado, sem código) · `implementado` (código, sem validação no ambiente pertinente) · `testado-unidade` · `validado-local` · `validado-internet`. Requisito só é **concluído** quando validado no ambiente pertinente.

Fonte dos requisitos: prompt mestre do produto (contrato §1–§7). Evidências apontam para testes ou registros.

| ID | Requisito | Estado | Evidência / observação |
|---|---|---|---|
| PLT-1 | C#/.NET LTS, Windows 11, WinUI 3 + Windows App SDK | projetado | .NET 10 SDK 10.0.404; WinAppSDK 2.6.5 escolhido ([auditoria §6](../research/auditoria-inicial.md)) |
| PLT-2 | GPLv3 no código próprio; licenças de dependências verificadas | implementado | `LICENSE`; SIPSorcery rejeitada por licença ([ADR 0002](../adr/0002-pilha-webrtc.md)) |
| NET-1 | Mídia somente P2P; sem SFU/TURN/relay | validado-local | [P1](../benchmarks/2026-11-16-P1-webrtc-local.md): sem servidores ICE, par host↔host, DTLS-SRTP; entre redes distintas pendente; STUN próprio (sem terceiros) testado e interoperando com a libdatachannel |
| NET-2 | Sinalização com transporte seguro e autenticação de sessão | validado-internet | 83 testes do servidor + implantação real com Caddy/Let's Encrypt; app real conectado pela internet ([implantação](../benchmarks/2026-11-16-implantacao-vm.md)) |
| NET-3 | Até 15 participantes em malha completa | validado-local | [P2](../benchmarks/2026-11-16-P2-malha-local.md): 15 processos, 105 enlaces, áudio entre todos, 40/40; redes distintas e uplink com tela pendentes |
| NET-4 | Falhas de conexão compreensíveis; condições de rede documentadas | projetado | `AdmissionFailed` com pares afetados; auditoria §5 |
| ADM-1 | Estado provisório, confirmação coordenada, timeout e rollback | testado-unidade | `AdmissionTests` (bilateral, timeout, falha, rollback preserva chamada) |
| ADM-2 | Entradas simultâneas, saídas e reconexões durante a negociação | testado-unidade; entradas simultâneas validadas localmente (P2) | `Concurrent_joiners_*`, `Active_participant_leaving_*`, `Reconnecting_participant_is_still_required_*`, `ReviewRegressionTests`, `RoomInvariantTests` (300 sequências) |
| ADM-3 | Sinalização × P2P × mídia operacional diferenciados | projetado | [Protocolo §2](../protocol/salas-e-admissao.md) |
| ROOM-1 | Salas e nomes temporários, sem contas | testado-unidade | Identidade = `ParticipantId` efêmero |
| ROOM-2 | Encerramento automático da sala vazia com tolerância de reconexão | testado-unidade | `Empty_room_closes_only_after_reconnect_grace` |
| ROOM-3 | Criador admin; expulsar; encerrar para todos | validado-local (expulsar) | `MembershipTests`; app real: administrador removeu participante pela UI ([fumaça](../benchmarks/2026-11-16-UI-fumaca-local.md)); encerrar para todos pela UI não exercitado |
| ROOM-4 | Sucessão automática por antiguidade | testado-unidade | `Admin_succession_*`, `Seniority_survives_reconnection` |
| ROOM-5 | Saída voluntária × desconexão temporária | testado-unidade | `Voluntary_leave_*`, `Reconnect_grace_expiry_*` |
| ROOM-6 | Entrada por código e `coconut://join/CODIGO`; instância existente recebe convite | pendente | P5 |
| AUD-1 | Microfone aberto com mudo efetivo validado na mídia | testado-unidade; validado-local (mudo) | `VoicePipelineTests`: mudo verificado no áudio decodificado, provado por mutação; P3: motor real com mudo = 0 pacotes. Chamada entre pessoas pendente |
| AUD-2 | Opus, eco tratado, processamento mínimo | validado-local (parcial) | [ADR 0004](../adr/0004-audio-wasapi.md), [P3](../benchmarks/2026-11-16-P3-audio-local.md): AEC do Windows ativo, IA desligada; NS simples e AGC impostos pelo modo (decisão pendente); eco audível não avaliado |
| AUD-3 | Dispositivos padrão, seleção manual, troca a quente, recuperação | implementado | `AudioDeviceCatalog` (notificações), `VoiceEngine` (seleção, troca, volta ao padrão); abertura real verificada no P3; desconexão física não testada |
| SCR-1 | Monitor inteiro, seleção multi-monitor | pendente | P4; máquina local tem 1 monitor |
| SCR-2 | Resolução/FPS/bitrate configuráveis e adaptativos | pendente | P4/P6 |
| SCR-3 | Um transmissor por sala; troca solicitar/aceitar/recusar/expirar/cancelar | testado-unidade | `ScreenShareTests` (15 casos) |
| SCR-4 | Voz preservada durante compartilhamento | pendente | P6 |
| WIN-1 | Fechar → bandeja; bandeja abre, silencia, encerra | pendente | P5 |
| WIN-2 | Inicialização automática opcional (desligada), na bandeja, instância única | pendente | P5 |
| DST-1 | Instalador e atualização automática verificada | pendente | P7 |
| SEC-1 | Análise de ameaças | projetado | [Modelo de ameaças](../security/modelo-de-ameacas.md) (inicial) |
| SEC-2 | Logs locais com retenção, exportação sanitizada e testes de sanitização | em andamento | Servidor: teste `Logs_never_contain_secrets_*`. Cliente (retenção, exportação) pendente |
| PERF-1 | Benchmarks reprodutíveis | projetado | [Plano](../prototypes/plano.md) |
| UI-1 | UI modular (MVVM/composição), Fluent, temas, acessibilidade | implementado; validado-local (parcial) | `Coconut.App.Core` (ViewModels, 29 testes) + `Coconut.App` (views separadas, x:Bind, Mica, nomes de automação); fumaça de ponta a ponta com capturas; tema claro e leitor de tela não verificados |
