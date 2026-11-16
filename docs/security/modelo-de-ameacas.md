# Modelo de ameaças — versão inicial

Status: rascunho de 2026-11-16. Mitigações marcadas **(implementada)** têm teste; as demais são planejadas.

| Ameaça | Mitigação |
|---|---|
| Convite adivinhado | Código com ≥ 50 bits de entropia de CSPRNG; limite de tentativas por conexão/IP no servidor; resposta idêntica para código inexistente e sala fechada |
| Convite vazado | Sala temporária; administrador expulsa (implementada) com revogação de identidade (implementada); rotação de convite como mitigação proposta (exige decisão: amplia escopo) |
| Participante malicioso envia vídeo sem ser transmissor | Receptores só exibem a sessão anunciada pelo servidor (contrato `ShareStateChanged`; aplicação no cliente pendente) |
| Falsificação de papel/administração | Servidor deriva identidade da conexão autenticada; comandos administrativos validados no núcleo (implementada) |
| Reuso de token de sessão por expulso | Identidade revogada bloqueia `Resume`, `Join`, relay e relatórios (implementada) |
| Condições de corrida (admissão, troca de transmissor) | Ator único por sala; núcleo determinístico (implementada, ADR 0003) |
| Relay de SDP/ICE para não membros | `CanRelay` exige ambos membros e não revogados (implementada) |
| Negação de serviço na sinalização | Limites de tamanho de mensagem, taxa por conexão, número de salas por IP, timeouts de handshake |
| Abuso de salas públicas | Sem diretório de salas; só por convite; capacidade 15 |
| Links `coconut://` maliciosos | Validar formato estrito do código (alfabeto e tamanho), nunca executar/abrir conteúdo do link, confirmação visual antes de entrar |
| Atualização adulterada | Manifesto assinado Ed25519 + hash por pacote; rejeição antes de executar (P7) |
| Exposição de IP entre pares | **Aceita pelo produto**; aviso claro antes da primeira entrada |
| Vazamento em logs | Logs sem tokens, códigos de convite completos, SDP, candidatos ICE ou IPs; exportação sanitizada com testes |
| Mídia | DTLS-SRTP nativo do WebRTC; fingerprints DTLS trafegam pela sinalização protegida por TLS |
