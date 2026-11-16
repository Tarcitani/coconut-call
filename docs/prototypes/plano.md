# Protótipos técnicos e benchmarks

Cada protótipo define hipótese, procedimento e critério **antes** da execução. Resultados vão para `docs/benchmarks/` (um arquivo por execução: hardware, versões, cenário, parâmetros, procedimento, resultado, limitações; local × simulado × internet sempre separados).

| ID | Risco | Hipótese | Procedimento | Critério de sucesso | Estado |
|---|---|---|---|---|---|
| P1 | R1 | libdatachannel compilada via vcpkg é chamável por P/Invoke do .NET 10 e estabelece DTLS-SRTP P2P sem TURN | `native/vcpkg.json` → `datachannel.dll`; console .NET com 2 peers em processos distintos, SDP trocado por arquivo/pipe; canal de dados com eco; trilha Opus com quadros sintéticos | Estado `connected` nos dois lados; eco ida-e-volta; quadros Opus recebidos com timestamps monotônicos; nenhum `iceServers` configurado | **Sucesso local** (9/9 execuções) — [evidência](../benchmarks/2026-11-16-P1-webrtc-local.md) |
| P2 | R1/R2 | Malha local de N processos (N=3, 5, 8, 15) negocia com o protocolo de admissão | Host de sinalização local + N clientes console | Todos admitidos; tempo de admissão por N; CPU/RAM por processo | **Sucesso local** (N=15: 40/40) — [evidência](../benchmarks/2026-11-16-P2-malha-local.md) |
| P3 | R3 | WASAPI *Communications* entrega AEC sem supressão de ruído forçada | Captura com e sem categoria; reprodução de sinal de referência; inspeção de efeitos ativos | Eco atenuado audivelmente e medido; lista de efeitos ativos registrada | **Parcial**: efeitos e AEC confirmados; IA desligada ([evidência](../benchmarks/2026-11-16-P3-audio-local.md)); eco audível pendente (precisa de pessoas) |
| P4 | R4 | WGC → MF H.264 (hardware) → decode MF → `SwapChainPanel` sustenta 1080p30 com CPU baixa | Protótipo WinUI isolado, loopback local sem rede | Latência captura→tela, CPU, GPU, fps em 720p/1080p × 15/30/60 | Pendente |
| P5 | R7 | WinUI 3 não empacotado (WinAppSDK 2.x) suporta bandeja, instância única e ativação `coconut://` redirecionada | App mínimo; segunda instância via link | Link com app aberto chega à instância existente; fechar janela mantém processo na bandeja | Pendente |
| P6 | R2 | Codificar uma vez e enviar para 14 trilhas mantém CPU estável; uplink calculado confere com o medido | Transmissor + 14 receptores locais | Uplink medido por par e total; CPU do transmissor | Depende de P1, P4 |
| P7 | R6 | Velopack + manifesto Ed25519 rejeita pacote adulterado | Feed local com pacote íntegro e adulterado | Adulterado rejeitado antes de executar; íntegro aplicado; chamada ativa não interrompida | Pendente |

## Benchmarks reprodutíveis (a implementar)

Inicialização a frio · repouso na bandeja · chamada 1:1 · grupo (3/5/8/15) · tela 720p/1080p × 15/30/60 fps com e sem hardware · uplink por participante · latência boca-ouvido e captura-tela · perda e reconexão (simuladas com atraso/perda injetados) · estabilidade prolongada (≥ 2 h) com amostragem de memória.
