# ADR 0003 — Núcleo de sinalização determinístico, um ator por sala

Data: 2026-11-16 · Status: aceito (implementado e testado)

## Contexto
Admissão P2P, sucessão administrativa e troca de transmissor envolvem eventos concorrentes (entradas simultâneas, saídas, reconexões, prazos). Bugs de concorrência nessa camada seriam difíceis de reproduzir.

## Decisão
- `Coconut.Signaling.Core` modela a sala como máquina de estados **síncrona e sem E/S**: comandos entram, mensagens saem por um `Outbox`; o tempo vem de `TimeProvider`.
- O host (próximo incremento) processa os comandos de cada sala **em série** (fila por sala) e entrega o `Outbox` depois de cada comando.
- Estado em memória; nenhum banco de dados (salas são temporárias).

## Consequências
- Testes determinísticos sem rede nem `Thread.Sleep` (relógio manual).
- Escala horizontal futura exigirá afinidade de sala por instância — aceitável para o MVP local.
- Autenticação e transporte ficam no host; o núcleo confia apenas na identidade que o host associa à conexão.
