# Modelo de ameaças — MVP 0.3

Status: atualizado em 2026-11-17. Mitigações marcadas **(implementada)** têm teste; as demais são planejadas.

| Ameaça | Mitigação |
|---|---|
| Convite adivinhado | Código de 50 bits por CSPRNG (implementada); resposta idêntica para código inexistente e sala fechada (implementada); cada tentativa custa uma conexão WebSocket nova. **Pendente:** limite de tentativas de convite por endereço de origem |
| Sinalização interceptada | Texto claro só em loopback; fora dele exige TLS — recusa iniciar e responde 403 por requisição (implementada, testada) |
| Token de sessão vazado do servidor | Servidor guarda só o SHA-256 do token (implementada) |
| Convite vazado | Sala temporária; administrador expulsa (implementada) com revogação de identidade (implementada); rotação de convite como mitigação proposta (exige decisão: amplia escopo) |
| Participante malicioso envia vídeo sem ser transmissor | Peer autenticado + sessão completa em cada pacote e frame; revogação limpa GPU; limites de remontagem e testes de troca/sessão antiga (implementada) |
| Falsificação de papel/administração | Servidor deriva identidade da conexão autenticada; comandos administrativos validados no núcleo (implementada) |
| Reuso de token de sessão por expulso | Identidade revogada bloqueia `Resume`, `Join`, relay e relatórios (implementada) |
| Condições de corrida (admissão, troca de transmissor) | Ator único por sala; núcleo determinístico (implementada, ADR 0003) |
| Relay de SDP/ICE para não membros | `CanRelay` exige ambos membros e não revogados (implementada) |
| Negação de serviço na sinalização | Tamanho de mensagem, taxa por conexão, salas por origem, prazo de handshake, fila de envio limitada (implementadas e testadas). **Pendente:** limite de conexões simultâneas por origem |
| Abuso de salas públicas | Sem diretório de salas; só por convite; capacidade 15 |
| Links `coconut://` maliciosos | Validar formato estrito do código (alfabeto e tamanho), nunca executar/abrir conteúdo do link, ação explícita para entrar; parser estrito e pipe CurrentUserOnly com tamanho limitado (implementada, DesktopMvpTests) |
| Atualização adulterada | Ed25519 com chave pública embutida, versão/validade/URL restritas, tamanho/hash e rehash sob bloqueio de escrita antes de executar; chamada bloqueia aplicação (implementada, 12 testes). Sem certificado Authenticode; assinatura não protege de comprometimento da chave privada/host de build |
| STUN usado como refletor/amplificador | Só responde a Binding Request válido (resposta ≤ 44 bytes), limite por origem, nada para tráfego não STUN (implementada, testada) |
| Forja de IP de origem atrás do proxy | `X-Forwarded-*` só aceito de proxy em loopback (Caddy na mesma VM); cliente direto não consegue forjar (testado) |
| Exposição de IP entre pares | **Aceita pelo produto**; aviso claro antes da primeira entrada |
| Vazamento em logs | Servidor registra só um identificador interno aleatório da sala; teste verifica ausência de convite, tokens, nomes, IPs e candidatos nos logs (implementada). Exportação do cliente sanitiza novamente, inclui só logs limitados e exclui configurações/chaves/mídia (implementada, DesktopMvpTests) |
| Mídia | DTLS-SRTP nativo do WebRTC; fingerprints DTLS trafegam pela sinalização protegida por TLS |
