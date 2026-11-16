# Checklist de requisitos

Estados: `pendente` · `projetado` (especificado, sem código) · `implementado` (código, sem validação no ambiente pertinente) · `testado-unidade` · `validado-local` · `validado-internet`. Requisito só é **concluído** quando validado no ambiente pertinente.

Fonte dos requisitos: prompt mestre do produto (contrato §1–§7). Evidências apontam para testes ou registros.

| ID | Requisito | Estado | Evidência / observação |
|---|---|---|---|
| PLT-1 | C#/.NET LTS, Windows 11, WinUI 3 + Windows App SDK | projetado | .NET 10 SDK 10.0.404; WinAppSDK 2.6.5 escolhido ([auditoria §6](../research/auditoria-inicial.md)) |
| PLT-2 | GPLv3 no código próprio; licenças de dependências verificadas | implementado | `LICENSE`; SIPSorcery rejeitada por licença ([ADR 0002](../adr/0002-pilha-webrtc.md)) |
| NET-1 | Mídia somente P2P; sem SFU/TURN/relay | validado-local | [P1](../benchmarks/2026-11-16-P1-webrtc-local.md): sem servidores ICE, par host↔host, DTLS-SRTP; entre redes distintas pendente |
| NET-2 | Sinalização com transporte seguro e autenticação de sessão | pendente | Host WebSocket/TLS é o próximo incremento |
| NET-3 | Até 15 participantes em malha completa | testado-unidade | `Full_mesh_of_fifteen_is_admitted`, `Capacity_counts_pending_participants`; rede real pendente (P2/P6) |
| NET-4 | Falhas de conexão compreensíveis; condições de rede documentadas | projetado | `AdmissionFailed` com pares afetados; auditoria §5 |
| ADM-1 | Estado provisório, confirmação coordenada, timeout e rollback | testado-unidade | `AdmissionTests` (bilateral, timeout, falha, rollback preserva chamada) |
| ADM-2 | Entradas simultâneas, saídas e reconexões durante a negociação | testado-unidade | `Concurrent_joiners_*`, `Active_participant_leaving_*`, `Reconnecting_participant_is_still_required_*`, `ReviewRegressionTests`, `RoomInvariantTests` (300 sequências) |
| ADM-3 | Sinalização × P2P × mídia operacional diferenciados | projetado | [Protocolo §2](../protocol/salas-e-admissao.md) |
| ROOM-1 | Salas e nomes temporários, sem contas | testado-unidade | Identidade = `ParticipantId` efêmero |
| ROOM-2 | Encerramento automático da sala vazia com tolerância de reconexão | testado-unidade | `Empty_room_closes_only_after_reconnect_grace` |
| ROOM-3 | Criador admin; expulsar; encerrar para todos | testado-unidade | `MembershipTests` (kick/end/revogação) |
| ROOM-4 | Sucessão automática por antiguidade | testado-unidade | `Admin_succession_*`, `Seniority_survives_reconnection` |
| ROOM-5 | Saída voluntária × desconexão temporária | testado-unidade | `Voluntary_leave_*`, `Reconnect_grace_expiry_*` |
| ROOM-6 | Entrada por código e `coconut://join/CODIGO`; instância existente recebe convite | pendente | P5 |
| AUD-1 | Microfone aberto com mudo efetivo validado na mídia | pendente | Validação por inspeção do RTP |
| AUD-2 | Opus, eco tratado, processamento mínimo | pendente | P3 |
| AUD-3 | Dispositivos padrão, seleção manual, troca a quente, recuperação | pendente | |
| SCR-1 | Monitor inteiro, seleção multi-monitor | pendente | P4; máquina local tem 1 monitor |
| SCR-2 | Resolução/FPS/bitrate configuráveis e adaptativos | pendente | P4/P6 |
| SCR-3 | Um transmissor por sala; troca solicitar/aceitar/recusar/expirar/cancelar | testado-unidade | `ScreenShareTests` (15 casos) |
| SCR-4 | Voz preservada durante compartilhamento | pendente | P6 |
| WIN-1 | Fechar → bandeja; bandeja abre, silencia, encerra | pendente | P5 |
| WIN-2 | Inicialização automática opcional (desligada), na bandeja, instância única | pendente | P5 |
| DST-1 | Instalador e atualização automática verificada | pendente | P7 |
| SEC-1 | Análise de ameaças | projetado | [Modelo de ameaças](../security/modelo-de-ameacas.md) (inicial) |
| SEC-2 | Logs locais com retenção, exportação sanitizada e testes de sanitização | pendente | |
| PERF-1 | Benchmarks reprodutíveis | projetado | [Plano](../prototypes/plano.md) |
| UI-1 | UI modular (MVVM/composição), Fluent, temas, acessibilidade | pendente | Aguarda P4/P5 |
