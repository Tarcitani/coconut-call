# Protocolo de salas, admissão, administração e compartilhamento

Status: **v0.1 — implementado no núcleo (`Coconut.Signaling.Core`), coberto por testes unitários, de regressão e de invariantes com sequências aleatórias; revisado de forma independente em 2026-11-16.** Transporte (WebSocket/TLS), autenticação de sessão e clientes reais ainda não existem. Ver [status](../status.md).

## 1. Princípios

- O servidor de sinalização é a **única autoridade** sobre composição da sala, administração e transmissor de tela. Clientes nunca afirmam o próprio papel; o servidor deriva tudo da identidade ligada à conexão.
- Cada sala é processada **sequencialmente** (um ator por sala). O núcleo é determinístico: recebe comandos e o relógio (`TimeProvider`) e devolve mensagens a enviar. Isso elimina condições de corrida dentro da sala por construção.
- **Prazos** (admissão, tolerância de reconexão, pedido de tela, handover) são aplicados **antes de cada comando e em ordem cronológica**, um por vez, com reavaliação das admissões após cada um. O resultado não depende do ritmo com que o host chama `Tick`; o `Tick` periódico serve apenas para entregar expirações quando não chega nenhum comando. Em prazos simultâneos: remoção por tolerância esgotada → compartilhamento → expiração de admissão.
- **Difusões de estado da chamada** (administração, reconexões, transmissor) vão só para a malha (`Active ∪ Reconnecting`). Pendentes recebem `ConnectToPeer`, `DropPeer`, `PeerLeft`, `PeerAdmitted` (para saber que novos membros passaram a ser exigidos) e o `RoomSnapshot` ao serem admitidos.
- Mídia nunca passa pelo servidor. Ele só encaminha ofertas/respostas SDP e candidatos ICE entre membros com relação válida.

## 2. Estados do participante

| Estado | Significado | Conta na capacidade (15) | Exigido na admissão de outros |
|---|---|---|---|
| `Pending` | Entrou com convite válido; negociando P2P com o conjunto ativo | Sim | Não |
| `Active` | Admitido; parte da malha | Sim | **Sim** |
| `Reconnecting` | Era `Active`; perdeu a sinalização; dentro da tolerância | Sim | **Sim** |

Três níveis de conectividade são distintos e nunca confundidos:

1. **Sinalização conectada** — WebSocket autenticado com o servidor.
2. **Enlace P2P verificado** — ICE + DTLS concluídos *e* eco no canal de dados confirmado. É o que a admissão exige.
3. **Mídia operacional** — pacotes RTP de áudio decodificados recebidos. Reportado separadamente para indicadores e diagnóstico; não bloqueia admissão (um participante silenciado continua admitido).

## 3. Admissão

**Conjunto ativo de decisão:** em qualquer instante, `R = { membros Active ∪ Reconnecting }`. Não é um retrato tirado na entrada: é recalculado a cada avaliação. Cada mudança em `R` incrementa a **época** da sala.

Fluxo:

1. Cliente apresenta convite → servidor cria `Pending` com prazo `now + AdmissionTimeout` (padrão 20 s) e envia `AdmissionStarted` com a lista de pares a conectar.
2. Servidor instrui cada membro de `R` **e cada outro `Pending`** a conectar-se ao novo participante (`ConnectToPeer`). Conectar-se a outros pendentes antecipa o enlace caso ambos sejam admitidos.
3. **Papel de ofertante determinístico:** quem entrou depois (maior número de sequência de entrada) envia a oferta. Isso evita *glare*.
4. Cada extremidade reporta o enlace (`ReportLink`) como `Verified` ou `Failed`. Um enlace só vale quando **as duas extremidades** reportaram `Verified`.
5. A cada evento relevante, o servidor avalia cada `Pending` em ordem de chegada: admitido sse há enlace verificado com **todo** membro de `R`. Admissão → estado `Active`, época +1, `PeerAdmitted` com a lista completa para todos.
6. Admissões são confirmadas uma por vez dentro do ator da sala; após cada uma, `R` cresce e os demais pendentes são reavaliados contra o novo `R`.
7. A ordem de chegada define apenas a **ordem de avaliação**, não uma fila: pendentes não fazem parte de `R`, então quem entrou depois pode ser admitido antes se concluir seus enlaces primeiro. Um enlace falho entre dois pendentes só derruba a admissão quando um deles passa a integrar `R`.

Falha e *rollback*:

- Qualquer extremidade reportar `Failed` num enlace exigido, ou o prazo expirar → `AdmissionFailed` para o pendente (com os pares que falharam e o estado de cada um — por exemplo `Reconnecting` —, **sem IPs**) e `DropPeer(motivo)` para os demais, que fecham as conexões com ele. Os enlaces entre membros já ativos não são tocados: **a chamada estabelecida é preservada.**
- Se o último membro de `R` sai, a sala é **encerrada** e os pendentes recebem `AdmissionFailed(RoomEmptied)`: um pendente nunca herda a sala sem ter verificado enlace com alguém que estava nela (mitiga convite vazado).
- Pendente que sai ou perde a sinalização → admissão cancelada, mesmo `DropPeer`.
- Membro de `R` que sai durante a admissão → é removido de `R`; o pendente é reavaliado (pode ser admitido de imediato).
- Membro de `R` em `Reconnecting` continua exigido. Se não voltar, a admissão expira com motivo explícito; o cliente pode tentar de novo. (Decisão consciente: admitir sem esse enlace quebraria a malha completa quando ele voltar.)

Capacidade: `Pending + Active + Reconnecting ≤ 15`. Excedente recebe `RoomFull`.

**Comportamento do cliente** (`Coconut.Client.MeshSession`, validado no [P2](../benchmarks/2026-11-16-P2-malha-local.md)):
- Cada enlace tem um canal de dados e uma trilha Opus `sendrecv` (`mid=audio`), criada nos dois lados antes de qualquer descrição.
- Verificação (nível 2): ao abrir o canal, cada lado envia `probe:<nonce>`; ao receber o eco `probe-ack:<nonce>` reporta `Verified`.
- SDP e candidatos via `relay` com trickle; candidato do relay = `"<mid>\n<candidato>"`. Candidatos que chegam antes da descrição remota ficam retidos; cada um é aplicado de forma independente.
- Enlace que cai antes de verificado: o ofertante renegocia com uma conexão nova (até 3 tentativas) e só então reporta `Failed`; o respondente substitui a conexão ao receber nova oferta. Quedas de enlaces já verificados ainda não são tratadas (§8).

## 4. Saída, desconexão e reconexão

- **Saída voluntária** (`Leave`): remoção imediata, `PeerLeft(Left)`. Voltar exige novo convite e gera nova identidade (perde antiguidade); o núcleo rejeita a reutilização de uma identidade já usada na sala (`IdentityReused`).
- **Perda de sinalização** de `Active`: vira `Reconnecting` por `ReconnectGrace` (padrão 30 s); `ParticipantReconnecting` para os demais. Os enlaces P2P podem continuar vivos — sinalização e mídia são independentes.
- **Retomada** (`Resume`) dentro da tolerância, com o token de sessão: volta a `Active` mantendo antiguidade e papéis; recebe `RoomSnapshot`, `ConnectToPeer` apenas para pendentes que entraram durante a ausência e, se for o transmissor, o pedido de troca ou o `StopSharing` pendente que pode ter perdido.
- Tolerância expirada → remoção como `PeerLeft(TimedOut)`.
- Perda de sinalização de `Pending` = cancelamento da admissão.

## 5. Administração

- O criador é o administrador inicial.
- Somente o administrador pode `Kick` (alvo ≠ ele mesmo) e `EndRoom`. Validação no servidor pela identidade da conexão.
- Expulso: removido, recebe `Kicked`, sua identidade é **revogada** (não pode retomar sessão nem enviar comandos). Demais recebem `PeerLeft(Kicked)` — ou `DropPeer(Kicked)` se o expulso ainda era pendente.
- **Sucessão** quando o administrador é removido (saída, expiração, falha): o membro `Active` com menor sequência de admissão (presente há mais tempo); se não houver `Active`, o `Reconnecting` mais antigo. `AdminChanged` para todos.
- Administrador em `Reconnecting` mantém o papel durante a tolerância.
- `EndRoom` → `RoomEnded` para todos; sala fechada.

## 6. Compartilhamento de tela (um transmissor por sala)

Estados: `Idle` → `Sharing(transmissor, sessão)` → opcionalmente com `Request(pedido, solicitante, prazo)` → `Handover(antigo, novo, prazo)`.

1. `RequestShare` com sala ociosa → concessão imediata (`ShareStateChanged` com nova sessão).
2. Com transmissão ativa → `ShareRequested` ao transmissor; `ShareRequestPending` ao solicitante. **Um pedido pendente por vez**; concorrentes recebem `ShareBusy`.
3. Transmissor recusa → `ShareRequestDeclined`. Aceita → `StopSharing` ao transmissor e estado `Handover` (prazo `HandoverTimeout`, padrão 5 s).
4. Transmissor confirma `ShareStopped` (ou o prazo expira e o servidor revoga a sessão antiga) → concessão ao solicitante.
5. Pedido expira (`ShareRequestTimeout`, padrão 30 s) → `ShareRequestExpired` a ambos. Solicitante cancela → `ShareRequestCanceled` ao transmissor.
6. Transmissor sai durante pedido/handover → concessão ao solicitante. Solicitante sai → pedido cancelado.
7. Transmissor encerra por conta própria com pedido em aberto → a tela é concedida ao solicitante (o pedido continua válido).
8. Solicitante cancela **depois** do aceite → o transmissor, que já recebeu `StopSharing`, conclui o encerramento e a sala fica ociosa; até a confirmação (ou o prazo de handover), novos pedidos recebem `ShareBusy`.

Receptores só exibem vídeo do transmissor da **sessão vigente** anunciada pelo servidor; quadros de outro par são descartados (defesa contra par malicioso).

## 7. Mensagens

Contratos em `src/Coconut.Signaling.Contracts/` (`ClientMessages.cs`, `ServerMessages.cs`); serialização única em `SignalingJson` (JSON, discriminador `type`, IDs como string GUID). Servidor em `src/Coconut.Signaling.Server/`.

**Transporte:** WebSocket em `/ws`, uma mensagem JSON de texto por quadro. Texto claro só em loopback; fora dele o servidor exige HTTPS/WSS (recusa iniciar e, por requisição, responde 403).

**Handshake:** a primeira mensagem, em até 10 s, deve ser `createRoom`, `joinRoom` (convite de 10 caracteres base32 de Crockford; aceita minúsculas, hífens e espaços) ou `resumeSession` (token de 256 bits; o servidor guarda só o SHA-256). A resposta é `sessionEstablished { you, resumeToken, inviteCode }`. Convite inexistente ou de sala encerrada → `inviteInvalid` (indistinguíveis); token desconhecido → `sessionInvalid`. Retomar fecha a conexão anterior do mesmo participante sem gerar perda de sinalização.

**Comandos:** `reportLink`, `relay` (SDP/candidato opaco, ≤ 16 KiB, só entre membros — nunca interpretado nem registrado), `leaveRoom`, `kick`, `endRoom`, `requestShare`, `respondShare`, `cancelShare`, `shareStopped`. Recusa → `commandRejected { command, reason }` com o código de `CommandResult`.

**Limites (padrões):** mensagem ≤ 64 KiB (`MessageTooBig`), 50 msg/s com rajada de 200 (`PolicyViolation "rate-limit"`), JSON inválido ou tipo desconhecido (`InvalidPayloadData`), fila de envio de 512 (cliente lento é desconectado), 3 salas abertas por endereço de origem (`RoomLimit`; endereço só em memória), nome de 1–32 caracteres sem caracteres de controle. Quem deixa de ser membro recebe a última mensagem e a conexão é encerrada.

## 8. Questões em aberto

- Falha de enlace entre dois membros já ativos após a admissão (troca de rede): política de ICE restart e tolerância. Hoje é apenas reportada.
- Um enlace reportado `Verified` continua valendo até um novo relatório; se ele cair sem relatório, a admissão de um pendente pode usar um estado antigo. Requer que o cliente reporte quedas — a definir com o protocolo do cliente.
- Rotação de convite pelo administrador como mitigação contra convite vazado (ver modelo de ameaças).
