# P2 — malha completa com o servidor real e N processos (local)

**Classe do resultado: LOCAL** (servidor e N processos na mesma máquina, loopback e interfaces locais). Não comprova redes distintas, NAT/CGNAT, perda real nem uplink doméstico.

## Ambiente
Windows 11 Pro build 26300 · Ryzen 7 5800X3D (16 threads) · 16 GB · .NET 10.0.12 · libdatachannel 0.24.6, libsrtp 2.8.1 (OpenSSL), opus 1.6.1.

## Hipótese e critérios (definidos antes)
Com o servidor de sinalização real e o protocolo de admissão, N processos que **entram ao mesmo tempo** formam a malha completa, e o áudio Opus codificado uma vez por nó chega a todos. Pressuposto testado: os dois lados criam a trilha `sendrecv` com `mid=audio` **antes** de aplicar a oferta e a libdatachannel casa pelo `mid` (confirmado).

C1 todos admitidos · C2 mesma época e lista de N em todos · C3 N−1 enlaces verificados por nó · C4 ≥ 45/50 quadros decodificados de cada par · C5 nenhum candidato relay.

## Procedimento
```
dotnet build Coconut.slnx
dotnet run --project prototypes/P2.MeshLocal -- 15
```
O coordenador inicia o servidor (`Coconut.Signaling.Server.dll`, porta livre em 127.0.0.1) e N processos. O nó 0 cria a sala; os demais entram juntos. Fases por arquivos: todos admitidos → 1 s de tom Opus (cada quadro codificado uma vez e enviado a todos os pares) → todos registram o estado (barreira) → saída. Sem servidores ICE. Saída em `prototypes/P2.MeshLocal/runs/` (não versionada).

## Resultados (código final)

| N | Enlaces | Malha formada | Admissão (pior) | CPU/nó (mediana) | RAM/nó (pior) |
|---|---|---|---|---|---|
| 2 | 1 | 0,66 s | 531 ms | 0,55 s | 57 MB |
| 3 | 3 | 0,65 s | 542 ms | 0,50 s | 58 MB |
| 5 | 10 | 0,71 s | 566 ms | 0,56 s | 59 MB |
| 8 | 28 | 1,41 s | 590 ms | 0,78 s | 61 MB |
| 15 | 105 | ~1,4 s | 801 ms mediana / 1020 ms pior | 1,03 s (máx. por execução, mediana) | 70,6 MB |

**N=15, 40 execuções consecutivas: 40/40 com sucesso.** Em cada uma, 210 fluxos direcionados com 50/50 quadros decodificados; época e lista idênticas em todos os nós; candidatos selecionados `host`/`prflx`. CPU inclui inicialização do .NET e a negociação inteira, não regime contínuo.

## Defeitos encontrados e corrigidos durante o P2
1. **Coordenador passava `http://` ao cliente WebSocket** (defeito do protótipo).
2. **Candidatos retidos descartados em cascata** (`PeerLink`): um candidato rejeitado abortava a aplicação dos demais e, no respondente, impedia a geração da resposta. Agora cada candidato é independente e a resposta é sempre gerada.
3. **Retrato do estado tirado durante as saídas** (protótipo): nós mais lentos registravam época após outros saírem. Barreira antes da saída.
4. **Enlace que cai antes da verificação ficava pendurado até o timeout de admissão (20 s).** Agora o ofertante renegocia do zero (até 3 tentativas, espera 100/200 ms) e só então reporta falha; o respondente substitui a conexão ao receber nova oferta; eventos da conexão substituída são ignorados.

Antes das correções 2 e 4: falhas de admissão em 4 de 36 execuções de N=15.

## Questão aberta (causa não provada)
Raramente o ICE vai a `Failed`/`Disconnected` ~60 ms depois de começar, **antes de qualquer candidato remoto via trickle** (ex.: `Connecting@38, Failed@104`, 1 descrição enviada/recebida). Frequência observada após as correções: 1 enlace em ~4.200 negociações sob rajada sintética (14 entradas simultâneas), recuperado na 2ª tentativa. Os logs nativos em nível Warning não mostraram a causa; com eles ativos a falha não se reproduziu em 45 execuções. Próximo passo de investigação: log nativo em nível Debug restrito ao par que falha, e verificar se a descrição remota já trazia `a=end-of-candidates` sem candidatos.

## Limitações
Sem perda, jitter ou banda limitada; todas as interfaces na mesma máquina; 15 processos dividem 16 threads. Uplink de 15 participantes com tela não medido (P6). Cliente (`Coconut.Client`) ainda sem testes unitários próprios — a evidência é este teste de integração.
