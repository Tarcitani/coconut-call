# Estado atual

Atualizado: 2026-11-17 — MVP funcional 0.3.0, em pré-lançamento.

## Implementado e testado

- Voz Opus P2P em malha, admissão coordenada, administração, expulsão, sucessão e salas temporárias. Sinalização ASP.NET Core/WSS + STUN próprio, sem servidor de mídia/TURN/relay. Servidor do mantenedor já hospedado; endereço fora do repositório público.
- Tela de monitor inteiro: WGC → D3D11 → Media Foundation H.264 (hardware com alternativa por software), RTP/SRTP, decode MF e `SwapChainPanel`. Seleção de monitor, resolução, FPS e bitrate; codificação única para até 14 receptores. Qualidade adaptativa por perda/RTT, parada por congestionamento persistente para liberar conexão para voz.
- Um transmissor autorizado pelo servidor: solicitar, aceitar, recusar, cancelar e expirar; handover só após parar o anterior. Pacotes e frames vinculados ao peer autenticado e aos 128 bits da sessão. Entrada tardia recebe SPS/PPS no próximo IDR. Revogação limpa o vídeo imediatamente.
- Retomada da sinalização com token em memória e tolerância do servidor; vídeo suspenso até snapshot autenticado. Renegociação de enlaces interrompidos, tentativas limitadas. UI diferencia sinalização, admissão, P2P e áudio decodificado.
- Áudio WASAPI Communications/AEC, mudo antes do encoder, seleção/recuperação de dispositivos. Política de IA reaplicada na captura e monitorada após reabertura; erros visíveis. Trilhas de voz e tela separadas.
- WinUI 3 modular, ícone original, bandeja (abrir/mudo/encerrar), fechar oculta, instância única por usuário, protocolo `coconut://join/…` sem autoentrada, inicialização opcional desligada por padrão. Exportação de logs sanitizados sem configurações/chaves/mídia.
- Instalador offline Windows 11 x64 por usuário, runtimes incluídos, atualização preservando preferências. Atualização verifica manifesto Ed25519, tamanho e SHA-256 antes de executar; verificação/download automáticos e instalação por ação da pessoa, bloqueada em chamada. Não há Authenticode.

Arquitetura: [distribuição](distribuicao-windows.md), [ADR 0006](adr/0006-tela-e-integracao-windows.md), [protocolo](protocol/salas-e-admissao.md).

## Evidência atual

- `dotnet build Coconut.slnx -c Release`: sem avisos/erros. **568 testes**: núcleo 362, servidor 84, áudio 25, App.Core 63, cliente 22 e atualizações 12.
- Captura real nos dois monitores locais (inclusive retrato), encoder de hardware e software, remontagem RTP e decode GPU.
- Ensaio local com 15 participantes: 278 codificações únicas, todos os 14 receptores receberam/decodificaram 278 quadros, com áudio Opus simultâneo. Entrada tardia e perda/retomada de WebSocket exercitadas, com identidade preservada.
- App real em dois perfis isolados: admissão, captura/recepção de áudio, compartilhamento exibido nos dois, pedido, aceite e troca de transmissor, parada limpando vídeo, expiração, fechar/reabrir mantendo chamada e inicialização oculta. Instalação 0.2 → 0.3 verificada com assinatura/hash, 495 arquivos iguais e preferências preservadas. Registro completo, comandos e limites em [validação do MVP](benchmarks/2026-11-17-mvp.md).

## Validação externa ainda necessária

- Chamada entre pessoas com 0.3.0 em máquinas/redes distintas; áudio audível, AEC, NAT/CGNAT/IPv6. A primeira chamada humana da 0.1.0 admitiu sem áudio; não foi reproduzida neste PC. Os novos indicadores/logs ajudam a localizar falhas, mas não comprovam que aquele cenário foi corrigido.
- Windows limpo, Windows N com componentes de mídia, GPUs/drivers diferentes e desconexão física de dispositivos/monitores.
- Matriz completa 720p/1080p × 15/30/60, latência boca-ouvido/captura-tela, CPU/GPU/uplink real e estabilidade de pelo menos duas horas. O ensaio com 15 é local, não comprova capacidade de upload de uma rede doméstica.
- Tema claro, leitor de tela e revisão de acessibilidade completa.
- Certificado Authenticode não configurado; SmartScreen pode avisar. Ed25519 protege a atualização, não assina o editor do executável.
- Perfil SRTP efetivamente negociado não inspecionado. Limites adicionais de tentativas de convite e conexões simultâneas por origem permanecem propostas no modelo de ameaças.

Nenhuma decisão do usuário bloqueia o MVP funcional. Essas validações permanecem abertas e a versão é um pré-lançamento.

## Histórico e evidências anteriores

[P1 WebRTC](benchmarks/2026-11-16-P1-webrtc-local.md) · [P2 malha, 40/40 com 15 processos](benchmarks/2026-11-16-P2-malha-local.md) · [P3 áudio](benchmarks/2026-11-16-P3-audio-local.md) · [UI inicial](benchmarks/2026-11-16-UI-fumaca-local.md) · [hospedagem](benchmarks/2026-11-16-implantacao-vm.md) · [investigação áudio humano](benchmarks/2026-11-16-audio-chamada-real.md) · [instalador 0.2.0](benchmarks/2026-11-16-instalador-windows.md).

Releases: https://github.com/Tarcitani/coconut-call/releases. A release pública não inclui o endereço particular do servidor.
